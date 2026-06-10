using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    public class PluginScanner
    {
        private readonly PluginLoader _loader = new();
        private readonly List<string> _scanDirs = new();

        public PluginScanner(string? pluginsDir = null)
        {
            var primary = pluginsDir ?? Path.Combine(
                AppContext.BaseDirectory, "Plugins");
            _scanDirs.Add(primary);

            // 开发环境：向上查找解决方案级 Plugins 目录
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
            catch
            {
                // 权限不足等场景
            }

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

                var startOk = await plugin.StartAsync();
                if (!startOk)
                {
                    Log.Error("插件 {PluginId} 启动失败", manifest.Id);
                    _loader.Unload(loadResult.Context!);
                    return;
                }
            }

            registry.Register(manifest, plugin, loadResult.Context!, pluginDir);
            registry.SetState(manifest.Id, PluginState.Running);

            LoadedPlugins.Add(registry.Get(manifest.Id)!);
            Log.Information("插件 {PluginId} 已启动", manifest.Id);
        }
    }
}
