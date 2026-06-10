using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    public class PluginScanner
    {
        private readonly PluginLoader _loader = new();
        private readonly string _pluginsDir;

        public PluginScanner(string? pluginsDir = null)
        {
            _pluginsDir = pluginsDir ?? Path.Combine(
                AppContext.BaseDirectory, "Plugins");

            if (!Directory.Exists(_pluginsDir))
                Directory.CreateDirectory(_pluginsDir);
        }

        public List<PluginManifest> DiscoveredManifests { get; } = new();
        public List<PluginEntry> LoadedPlugins { get; } = new();

        public async Task ScanAsync()
        {
            Log.Information("开始扫描插件目录: {Dir}", _pluginsDir);

            var subdirs = Directory.GetDirectories(_pluginsDir);
            Log.Information("发现 {Count} 个插件目录", subdirs.Length);

            foreach (var dir in subdirs)
            {
                await TryLoadPluginAsync(dir);
            }

            Log.Information("插件扫描完成: {Loaded}/{Total} 加载成功",
                LoadedPlugins.Count, DiscoveredManifests.Count);
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
