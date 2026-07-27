using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    internal sealed record LoadedDirectoryPlugin(
        string Id,
        PluginRuntimeLoadResult Runtime);

    public class PluginScanner : IDisposable
    {
        private readonly PluginRuntimeLoader _runtimeLoader = new PluginRuntimeLoader();
        private readonly StandalonePluginLoader _standaloneLoader;
        private readonly PluginSourceDiscovery _sourceDiscovery;
        private readonly PluginChangeMonitor _changeMonitor;
        private readonly Dictionary<string, LoadedDirectoryPlugin> _directoryPlugins =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StandalonePluginHandle> _standalonePlugins =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _reloadGate = new(1, 1);
        private readonly Func<string>? _currentLanguage;

        public PluginScanner(
            string? pluginsDir = null,
            Func<string>? currentLanguage = null)
        {
            _sourceDiscovery = new PluginSourceDiscovery(pluginsDir);
            _standaloneLoader = new StandalonePluginLoader(
                HostProvider.Instance.PluginStore);
            _changeMonitor = new PluginChangeMonitor(
                _sourceDiscovery.ScanDirectories,
                HandlePluginFileChangeAsync);
            _currentLanguage = currentLanguage;
        }

        public List<PluginManifest> DiscoveredManifests { get; } = new();
        public List<PluginEntry> LoadedPlugins { get; } = new();

        public async Task ScanAsync()
        {
            foreach (var scanDir in _sourceDiscovery.ScanDirectories)
                Log.Information("扫描插件目录: {Dir}", scanDir);

            var sources = _sourceDiscovery.Discover();
            foreach (var scriptFile in sources.StandaloneScripts)
                await TryLoadStandaloneAsync(scriptFile);

            Log.Information("发现 {Count} 个插件目录", sources.PluginDirectories.Count);

            foreach (var dir in sources.PluginDirectories)
            {
                await TryLoadPluginAsync(dir);
            }

            Log.Information("插件扫描完成: {Loaded}/{Total} 加载成功",
                LoadedPlugins.Count, DiscoveredManifests.Count);

            StartFileWatchers();
        }

        public void StartFileWatchers()
            => _changeMonitor.Start();

        internal async Task<bool> UnloadPluginAsync(string pluginId)
        {
            await _reloadGate.WaitAsync();
            try
            {
                var pluginDirectory = _directoryPlugins
                    .Where(item => string.Equals(
                        item.Value.Id,
                        pluginId,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Key)
                    .FirstOrDefault();
                if (pluginDirectory is null) return false;
                await UnloadDirectoryPluginAsync(pluginDirectory);
                return true;
            }
            finally
            {
                _reloadGate.Release();
            }
        }

        internal async Task ReloadPluginDirectoryAsync(string pluginDirectory)
        {
            var fullPath = Path.GetFullPath(pluginDirectory);
            await _reloadGate.WaitAsync();
            try
            {
                await UnloadDirectoryPluginAsync(fullPath);
                if (File.Exists(Path.Combine(fullPath, "manifest.json")))
                    await TryLoadPluginAsync(fullPath);
            }
            finally
            {
                _reloadGate.Release();
            }
        }

        internal async Task NotifyLanguageChangedAsync(string language)
        {
            await _reloadGate.WaitAsync();
            try
            {
                foreach (var entry in LoadedPlugins.ToArray())
                    await NotifyPluginLanguageAsync(entry, language);
            }
            finally
            {
                _reloadGate.Release();
            }
        }

        private async Task HandlePluginFileChangeAsync(PluginFileChange change)
        {
            await _reloadGate.WaitAsync();
            try
            {
                if (change.OldPath is not null
                    && !string.Equals(
                        change.OldPath, change.NewPath, StringComparison.OrdinalIgnoreCase))
                {
                    await ReloadPluginByFileAsync(change.OldPath, reloadIfAvailable: false);
                }

                if (change.NewPath is not null)
                    await ReloadPluginByFileAsync(change.NewPath, reloadIfAvailable: true);
            }
            finally
            {
                _reloadGate.Release();
            }
        }

        private async Task ReloadPluginByFileAsync(
            string filePath,
            bool reloadIfAvailable)
        {
            if (IsStandaloneScript(filePath))
            {
                await ReloadStandaloneScriptAsync(
                    Path.GetFullPath(filePath), reloadIfAvailable);
                return;
            }

            var pluginDir = PluginSourceDiscovery.FindPluginRootDirectory(filePath)
                ?? FindTrackedPluginDirectory(filePath);
            if (pluginDir is null)
                return;

            Log.Information("检测到插件变更: {Dir}", pluginDir);
            await UnloadDirectoryPluginAsync(pluginDir);

            if (reloadIfAvailable
                && File.Exists(Path.Combine(pluginDir, "manifest.json")))
            {
                await TryLoadPluginAsync(pluginDir);
            }
        }

        private string? FindTrackedPluginDirectory(string filePath)
        {
            var fullPath = Path.GetFullPath(filePath);
            return _directoryPlugins.Keys
                .Where(directory => fullPath.StartsWith(
                    directory.TrimEnd(Path.DirectorySeparatorChar)
                        + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(directory => directory.Length)
                .FirstOrDefault();
        }

        private async Task UnloadDirectoryPluginAsync(string pluginDirectory)
        {
            if (!_directoryPlugins.Remove(pluginDirectory, out var loaded))
                return;

            var registry = HostProvider.Instance.PluginStore;
            var entry = registry.Get(loaded.Id);
            if (entry?.Instance is ILongPlugin plugin)
            {
                try
                {
                    using (PluginAccessContext.Enter(loaded.Id))
                        await plugin.StopAsync();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "插件 {PluginId} 停止时出错", loaded.Id);
                }
            }

            registry.Unregister(loaded.Id);
            _runtimeLoader.Release(loaded.Runtime, loaded.Id);
            LoadedPlugins.RemoveAll(plugin => plugin.Id == loaded.Id);
            DiscoveredManifests.RemoveAll(manifest => manifest.Id == loaded.Id);
        }

        private bool IsStandaloneScript(string filePath)
        {
            return _sourceDiscovery.IsStandaloneScript(filePath);
        }

        private async Task ReloadStandaloneScriptAsync(
            string scriptPath,
            bool reloadIfAvailable)
        {
            if (_standalonePlugins.Remove(scriptPath, out var handle))
            {
                await _standaloneLoader.UnloadAsync(handle);
                LoadedPlugins.RemoveAll(entry => entry.Id == handle.Manifest.Id);
                DiscoveredManifests.RemoveAll(manifest => manifest.Id == handle.Manifest.Id);
            }

            if (reloadIfAvailable && File.Exists(scriptPath))
                await TryLoadStandaloneAsync(scriptPath);
        }

        private async Task TryLoadStandaloneAsync(string scriptPath)
        {
            var result = await _standaloneLoader.LoadAsync(scriptPath);
            if (!result.IsSuccess)
            {
                Log.Warning("单文件插件加载失败: {Path} - {Error}",
                    scriptPath, result.Error);
                return;
            }

            var handle = result.Handle!;
            _standalonePlugins[handle.SourcePath] = handle;
            DiscoveredManifests.Add(handle.Manifest);
            LoadedPlugins.Add(handle.Entry);
            Log.Information("单文件插件 {PluginId} 加载成功", handle.Manifest.Id);
        }

        private async Task TryLoadPluginAsync(string pluginDir)
        {
            var manifestResult = await ManifestReader.ReadAsync(pluginDir);

            if (!manifestResult.IsSuccess)
            {
                Log.Warning("插件清单无效: {Dir} - {ErrorCode} - {Error}",
                    pluginDir, manifestResult.ErrorCode, manifestResult.Error);
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

            var runtime = await _runtimeLoader.LoadAsync(pluginDir, manifest);
            if (!runtime.IsSuccess)
            {
                Log.Error("插件 {PluginId} 运行时加载失败: {Error}",
                    manifest.Id, runtime.Error);
                return;
            }
            var plugin = runtime.Instance!;

            // ★ 先注册再初始化，以便 InitializeAsync 中权限检查能生效
            registry.Register(manifest, plugin, runtime.LoadContext, pluginDir);

            var hostApi = HostProvider.Instance;

            using (PluginAccessContext.Enter(manifest.Id))
            {
                var initOk = await plugin.InitializeAsync(hostApi);
                if (!initOk)
                {
                    Log.Error("插件 {PluginId} 初始化失败", manifest.Id);
                    registry.Unregister(manifest.Id);
                    _runtimeLoader.Release(runtime, manifest.Id);
                    return;
                }
            }

            // 检查用户配置：仅 auto_start=true 时自动启动
            var entry = registry.Get(manifest.Id)!;
            if (_currentLanguage is not null)
                await NotifyPluginLanguageAsync(entry, _currentLanguage());
            _directoryPlugins[pluginDir] = new LoadedDirectoryPlugin(
                manifest.Id, runtime);
            var autoStart = entry.GetAutoStartPreference();
            Log.Information(
                "插件 {PluginId} 自动启动决策: Enabled={Enabled}, Source={Source}",
                manifest.Id,
                autoStart.Enabled,
                autoStart.Source);

            if (autoStart.Enabled)
            {
                using (PluginAccessContext.Enter(manifest.Id))
                {
                    var startOk = await plugin.StartAsync();
                    if (!startOk)
                    {
                        Log.Error("插件 {PluginId} 启动失败", manifest.Id);
                        try
                        {
                            await plugin.StopAsync();
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "插件 {PluginId} 启动失败后的清理未完全完成", manifest.Id);
                        }

                        // 保留插件实例，用户可解决热键冲突后从管理界面重新启用。
                        registry.SetState(manifest.Id, PluginState.Error);
                        LoadedPlugins.Add(entry);
                        return;
                    }
                    else
                    {
                        registry.SetState(manifest.Id, PluginState.Running);
                        Log.Information("插件 {PluginId} 已自动启动", manifest.Id);
                    }
                }
            }
            else
            {
                Log.Information("插件 {PluginId} 已加载（auto_start=false，待用户启用）", manifest.Id);
            }

            LoadedPlugins.Add(entry);
        }

        private static async Task NotifyPluginLanguageAsync(
            PluginEntry entry,
            string language)
        {
            if (entry.Manifest.Localization is not { } localization
                || entry.Instance is not IPluginLanguageLifecycle lifecycle)
                return;

            if (!PluginLocalizationLoader.TryLoad(
                    entry.Directory,
                    localization,
                    language,
                    out var context,
                    out var error))
            {
                Log.Warning(
                    "Plugin {PluginId} localization could not be loaded: {Error}",
                    entry.Id,
                    error);
                return;
            }

            var registry = HostProvider.Instance.PluginStore;
            if (!registry.ApplyLocalization(entry.Id, context!))
                entry.ApplyLanguageContext(context!);

            try
            {
                using (PluginAccessContext.Enter(entry.Id))
                    await lifecycle.OnLanguageChangedAsync(context!);
            }
            catch (Exception exception)
            {
                Log.Warning(
                    exception,
                    "Plugin {PluginId} language notification failed",
                    entry.Id);
            }
        }

        public void Dispose()
        {
            _changeMonitor.Dispose();

            Log.Information("PluginScanner 已释放，文件监控已停止。");
        }
    }
}
