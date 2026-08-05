using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    internal sealed class LoadedDirectoryPlugin
    {
        public LoadedDirectoryPlugin(
            string id,
            PluginRuntimeLoadResult? runtime)
        {
            Id = id;
            Runtime = runtime;
        }

        public string Id { get; }
        public PluginRuntimeLoadResult? Runtime { get; set; }
    }

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
        private readonly Action<string>? _startupMark;
        private readonly Func<string, bool>? _suppressAutoStart;

        public PluginScanner(
            string? pluginsDir = null,
            Func<string>? currentLanguage = null,
            Action<string>? startupMark = null,
            Func<string, bool>? suppressAutoStart = null)
        {
            _sourceDiscovery = new PluginSourceDiscovery(pluginsDir);
            _standaloneLoader = new StandalonePluginLoader(
                HostProvider.Instance.PluginStore);
            _changeMonitor = new PluginChangeMonitor(
                _sourceDiscovery.ScanDirectories,
                HandlePluginFileChangeAsync);
            _currentLanguage = currentLanguage;
            _startupMark = startupMark;
            _suppressAutoStart = suppressAutoStart;
        }

        public List<PluginManifest> DiscoveredManifests { get; } = new();
        public List<PluginEntry> LoadedPlugins { get; } = new();

        public async Task ScanAsync()
        {
            foreach (var scanDir in _sourceDiscovery.ScanDirectories)
                Log.Information("扫描插件目录: {Dir}", scanDir);

            _startupMark?.Invoke("plugin_source_discovery_begin");
            var sources = _sourceDiscovery.Discover();
            _startupMark?.Invoke("plugin_source_discovery_end");

            _startupMark?.Invoke("standalone_plugin_load_begin");
            foreach (var scriptFile in sources.StandaloneScripts)
                await TryLoadStandaloneAsync(scriptFile);
            _startupMark?.Invoke("standalone_plugin_load_end");

            Log.Information("发现 {Count} 个插件目录", sources.PluginDirectories.Count);

            _startupMark?.Invoke("plugin_manifest_read_begin");
            var manifests = new List<(string Directory, ManifestResult Result)>();
            foreach (var dir in sources.PluginDirectories)
                manifests.Add((dir, await ManifestReader.ReadAsync(dir)));
            _startupMark?.Invoke("plugin_manifest_read_end");

            _startupMark?.Invoke("plugin_registration_begin");
            using (HostProvider.Instance.PluginStore.BeginChangeBatch())
            {
                foreach (var (directory, result) in manifests)
                    await TryRegisterPluginAsync(directory, result);
            }
            _startupMark?.Invoke("plugin_registration_end");

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
            if (!_directoryPlugins.TryGetValue(pluginDirectory, out var tracked))
                return;

            var registry = HostProvider.Instance.PluginStore;
            var entry = registry.Get(tracked.Id);
            if (entry is not null)
                await entry.LifecycleGate.WaitAsync();

            try
            {
                if (!_directoryPlugins.Remove(pluginDirectory, out var loaded))
                    return;

                if (entry?.Instance is ILongPlugin plugin)
                {
                    try
                    {
                        using (PluginAccessContext.Enter(loaded.Id))
                        {
                            if (!await plugin.StopAsync())
                            {
                                registry.RuntimeHealth.RecordLifecycleFailure(
                                    loaded.Id,
                                    PluginRuntimeFailureKind.StopFailed);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        registry.RuntimeHealth.RecordLifecycleFailure(
                            loaded.Id,
                            PluginRuntimeFailureKind.StopFailed,
                            isException: true);
                        Log.Warning(ex, "插件 {PluginId} 停止时出错", loaded.Id);
                    }
                }

                if (entry?.Instance is IPluginResourceLifecycle resources)
                {
                    try
                    {
                        using (PluginAccessContext.Enter(loaded.Id))
                            await resources.ReleaseResourcesAsync();
                    }
                    catch (Exception ex)
                    {
                        registry.RuntimeHealth.RecordLifecycleFailure(
                            loaded.Id,
                            PluginRuntimeFailureKind.ResourceReleaseFailed,
                            isException: true);
                        Log.Warning(
                            ex,
                            "Plugin {PluginId} resource release failed during unload",
                            loaded.Id);
                    }
                }
                try
                {
                    await registry.ReleaseHostResourcesAsync(loaded.Id);
                }
                catch (Exception ex)
                {
                    registry.RuntimeHealth.RecordLifecycleFailure(
                        loaded.Id,
                        PluginRuntimeFailureKind.ResourceReleaseFailed,
                        isException: true);
                    Log.Warning(
                        ex,
                        "Plugin {PluginId} host resource release failed during unload",
                        loaded.Id);
                }
                registry.Unregister(loaded.Id);
                if (loaded.Runtime is not null)
                    _runtimeLoader.Release(loaded.Runtime, loaded.Id);
                LoadedPlugins.RemoveAll(plugin => plugin.Id == loaded.Id);
                DiscoveredManifests.RemoveAll(manifest => manifest.Id == loaded.Id);
            }
            finally
            {
                entry?.LifecycleGate.Release();
            }
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
            await TryRegisterPluginAsync(pluginDir, manifestResult);
        }

        private async Task TryRegisterPluginAsync(
            string pluginDir,
            ManifestResult manifestResult)
        {
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

            if (!registry.RegisterDeferred(
                    manifest,
                    pluginDir,
                    entry => ActivatePluginAsync(pluginDir, entry)))
            {
                return;
            }

            var entry = registry.Get(manifest.Id)!;
            _directoryPlugins[pluginDir] = new LoadedDirectoryPlugin(
                manifest.Id,
                null);
            if (_currentLanguage is not null)
                await NotifyPluginLanguageAsync(entry, _currentLanguage());
            var autoStart = entry.GetAutoStartPreference();
            var autoStartSuppressed =
                autoStart.Enabled
                && _suppressAutoStart?.Invoke(manifest.Id) == true;
            Log.Information(
                "插件 {PluginId} 自动启动决策: Enabled={Enabled}, Source={Source}, Suppressed={Suppressed}",
                manifest.Id,
                autoStart.Enabled,
                autoStart.Source,
                autoStartSuppressed);

            if (autoStart.Enabled && !autoStartSuppressed)
            {
                if (!await registry.StartPluginAsync(
                        manifest.Id,
                        persistAutoStart: false))
                {
                    LoadedPlugins.Add(entry);
                    return;
                }

                Log.Information("插件 {PluginId} 已自动激活并启动", manifest.Id);
            }
            else
            {
                Log.Information(
                    autoStartSuppressed
                        ? "插件 {PluginId} 已由质量归因模式抑制自动启动"
                        : "插件 {PluginId} 已注册，运行时等待按需激活",
                    manifest.Id);
            }

            LoadedPlugins.Add(entry);
        }

        private async Task<object?> ActivatePluginAsync(
            string pluginDirectory,
            PluginEntry entry)
        {
            var runtime = await _runtimeLoader.LoadAsync(
                pluginDirectory,
                entry.Manifest);
            if (!runtime.IsSuccess)
            {
                Log.Error(
                    "插件 {PluginId} 运行时加载失败: {Error}",
                    entry.Id,
                    runtime.Error);
                return null;
            }

            var plugin = runtime.Instance!;
            using (PluginAccessContext.Enter(entry.Id))
            {
                if (!await plugin.InitializeAsync(HostProvider.Instance))
                {
                    Log.Error("插件 {PluginId} 初始化失败", entry.Id);
                    _runtimeLoader.Release(runtime, entry.Id);
                    return null;
                }
            }

            if (_currentLanguage is not null)
                await NotifyPluginLanguageAsync(
                    entry,
                    _currentLanguage(),
                    plugin);
            if (!_directoryPlugins.TryGetValue(
                    pluginDirectory,
                    out var tracked)
                || !string.Equals(
                    tracked.Id,
                    entry.Id,
                    StringComparison.OrdinalIgnoreCase))
            {
                _runtimeLoader.Release(runtime, entry.Id);
                Log.Information(
                    "插件 {PluginId} 激活期间已卸载，运行时已释放",
                    entry.Id);
                return null;
            }

            tracked.Runtime = runtime;
            Log.Information("插件 {PluginId} 运行时已按需激活", entry.Id);
            return plugin;
        }

        private static async Task NotifyPluginLanguageAsync(
            PluginEntry entry,
            string language,
            object? activatedInstance = null)
        {
            if (entry.Manifest.Localization is not { } localization)
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

            if ((activatedInstance ?? entry.Instance)
                is not IPluginLanguageLifecycle lifecycle)
            {
                return;
            }

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
