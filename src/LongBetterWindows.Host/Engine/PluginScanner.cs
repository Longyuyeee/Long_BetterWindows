using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    public class PluginScanner : IDisposable
    {
        private readonly PluginLoader _loader = new();
        private readonly List<string> _scanDirs = new();
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly Dictionary<string, string> _dirToPluginId = new(); // pluginDir → pluginId
        private readonly object _reloadLock = new();
        private CancellationTokenSource? _debounceCts;

        public PluginScanner(string? pluginsDir = null)
        {
            var primary = pluginsDir ?? Path.Combine(
                AppContext.BaseDirectory, "Plugins");
            _scanDirs.Add(primary);

            var devDir = FindDevPluginsDir();
            if (devDir != null && devDir != primary)
                _scanDirs.Add(devDir);

            foreach (var dir in _scanDirs)
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
        }

        public List<PluginManifest> DiscoveredManifests { get; } = new();
        public List<PluginEntry> LoadedPlugins { get; } = new();

        public async Task ScanAsync()
        {
            var allDirs = new HashSet<string>();

            foreach (var scanDir in _scanDirs)
            {
                Log.Information("扫描插件目录: {Dir}", scanDir);

                if (!Directory.Exists(scanDir))
                    continue;

                foreach (var dir in Directory.GetDirectories(scanDir))
                {
                    allDirs.Add(dir);
                }
            }

            Log.Information("发现 {Count} 个插件目录", allDirs.Count);

            foreach (var dir in allDirs)
            {
                await TryLoadPluginAsync(dir);
            }

            Log.Information("插件扫描完成: {Loaded}/{Total} 加载成功",
                LoadedPlugins.Count, DiscoveredManifests.Count);

            StartFileWatchers();
        }

        public void StartFileWatchers()
        {
            foreach (var scanDir in _scanDirs)
            {
                if (!Directory.Exists(scanDir)) continue;

                var watcher = new FileSystemWatcher(scanDir)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true,
                };

                watcher.Changed += OnPluginFileChanged;
                watcher.Created += OnPluginFileChanged;
                watcher.Deleted += OnPluginFileChanged;
                watcher.Renamed += OnPluginFileRenamed;

                _watchers.Add(watcher);
                Log.Debug("文件监控已启动: {Dir}", scanDir);
            }
        }

        private void OnPluginFileChanged(object sender, FileSystemEventArgs e)
        {
            if (!IsPluginFile(e.FullPath)) return;
            DebounceReload(e.FullPath);
        }

        private void OnPluginFileRenamed(object sender, RenamedEventArgs e)
        {
            if (!IsPluginFile(e.FullPath)) return;
            DebounceReload(e.FullPath);
        }

        private static bool IsPluginFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var name = Path.GetFileName(path).ToLowerInvariant();
            return ext == ".dll" || name == "manifest.json";
        }

        private void DebounceReload(string filePath)
        {
            lock (_reloadLock)
            {
                _debounceCts?.Cancel();
                _debounceCts = new CancellationTokenSource();
                var token = _debounceCts.Token;

                Task.Delay(1000, token).ContinueWith(async _ =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        await ReloadPluginByFileAsync(filePath);
                    }
                }, TaskScheduler.Default);
            }
        }

        private async Task ReloadPluginByFileAsync(string filePath)
        {
            var pluginDir = FindPluginRootDir(filePath);
            if (pluginDir == null) return;

            Log.Information("检测到插件变更: {Dir}", pluginDir);

            // 卸载旧版本
            lock (_reloadLock)
            {
                if (_dirToPluginId.TryGetValue(pluginDir, out var oldId))
                {
                    var registry = HostProvider.Instance.PluginStore;
                    var entry = registry.Get(oldId);

                    if (entry != null)
                    {
                        Log.Information("卸载旧版插件: {PluginId}", oldId);

                        using (PluginAccessContext.Enter(oldId))
                        {
                            entry.Instance.StopAsync().ContinueWith(_ => { });
                        }

                        registry.Unregister(oldId);
                    }

                    LoadedPlugins.RemoveAll(p => p.Id == oldId);
                    DiscoveredManifests.RemoveAll(m => m.Id == oldId);
                    _dirToPluginId.Remove(pluginDir);
                }
            }

            // 加载新版本
            await TryLoadPluginAsync(pluginDir);
        }

        private static string? FindPluginRootDir(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (dir == null) return null;

            // 检查是否为插件根目录（包含 manifest.json）
            for (int i = 0; i < 3 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "manifest.json")))
                    return dir;

                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }

        private static string? FindDevPluginsDir()
        {
            try
            {
                var dir = AppContext.BaseDirectory;

                for (int i = 0; i < 5; i++)
                {
                    var parent = Directory.GetParent(dir);
                    if (parent == null) break;

                    dir = parent.FullName;
                    var pluginsDir = Path.Combine(dir, "Plugins");

                    if (Directory.Exists(pluginsDir))
                        return pluginsDir;
                }
            }
            catch { }

            return null;
        }

        private async Task TryLoadPluginAsync(string pluginDir)
        {
            var manifestResult = await ManifestReader.ReadAsync(pluginDir);

            if (!manifestResult.IsSuccess)
            {
                Log.Warning("插件清单无效: {Dir} - {Error}",
                    pluginDir, manifestResult.Error);
                return;
            }

            var manifest = manifestResult.Manifest!;
            DiscoveredManifests.Add(manifest);

            Log.Information("发现插件: {Name} ({Id} v{Version})",
                manifest.Name, manifest.Id, manifest.Version);

            var registry = HostProvider.Instance.PluginStore;

            if (registry.Get(manifest.Id) != null)
            {
                Log.Warning("插件 {PluginId} 已注册，跳过", manifest.Id);
                return;
            }

            var loadResult = await _loader.LoadAsync(pluginDir, manifest);

            if (!loadResult.IsSuccess)
            {
                Log.Error("插件 {PluginId} 加载失败: {Error}",
                    manifest.Id, loadResult.Error);
                return;
            }

            var plugin = loadResult.Instance!;
            var hostApi = HostProvider.Instance;

            using (PluginAccessContext.Enter(manifest.Id))
            {
                var initOk = await plugin.InitializeAsync(hostApi);
                if (!initOk)
                {
                    Log.Error("插件 {PluginId} 初始化失败", manifest.Id);
                    _loader.Unload(loadResult.Context!);
                    return;
                }
            }

            registry.Register(manifest, plugin, loadResult.Context!, pluginDir);
            _dirToPluginId[pluginDir] = manifest.Id;

            // 检查用户配置：仅 auto_start=true 时自动启动
            var entry = registry.Get(manifest.Id)!;
            var autoStart = entry.GetSetting("auto_start") ?? "true";

            if (autoStart == "true")
            {
                using (PluginAccessContext.Enter(manifest.Id))
                {
                    var startOk = await plugin.StartAsync();
                    if (!startOk)
                    {
                        Log.Error("插件 {PluginId} 启动失败", manifest.Id);
                        _loader.Unload(loadResult.Context!);
                        return;
                    }
                }

                registry.SetState(manifest.Id, PluginState.Running);
                Log.Information("插件 {PluginId} 已自动启动", manifest.Id);
            }
            else
            {
                Log.Information("插件 {PluginId} 已加载（auto_start=false，待用户启用）", manifest.Id);
            }

            LoadedPlugins.Add(entry);
        }

        public void Dispose()
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            _watchers.Clear();
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();

            Log.Information("PluginScanner 已释放，文件监控已停止。");
        }
    }
}
