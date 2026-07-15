using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    public class PluginScanner : IDisposable
    {
        private readonly PluginLoader _loader = new();
        private readonly ScriptPluginLoader _scriptLoader = new();
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

                // ✅ 新增：扫描根目录的单文件脚本（.csx, .js, .ts）
                var scriptPatterns = new[] { "*.csx", "*.js", "*.ts" };
                foreach (var pattern in scriptPatterns)
                {
                    var standaloneScripts = Directory.GetFiles(scanDir, pattern, SearchOption.TopDirectoryOnly);
                    foreach (var scriptFile in standaloneScripts)
                    {
                        var ext = Path.GetExtension(scriptFile).ToLowerInvariant();
                        if (ext == ".csx")
                            await TryLoadStandaloneScriptAsync(scriptFile);
                        else if (ext == ".js" || ext == ".ts")
                            await TryLoadStandaloneJsScriptAsync(scriptFile);
                    }
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
            return ext == ".dll" || ext == ".csx" || ext == ".js" || ext == ".ts" || name == "manifest.json";
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
                            if (entry.Instance is ILongPlugin plugin)
                            {
                                plugin.StopAsync().ContinueWith(t =>
                                {
                                    if (t.IsFaulted)
                                        Log.Error(t.Exception, "插件 {PluginId} 停止时出错", oldId);
                                });
                            }
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

            ILongPlugin plugin;
            PluginLoadContext? loadContext = null;

            // 检测脚本插件 (.csx)
            if (string.Equals(manifest.Runtime, "csharp-script", StringComparison.OrdinalIgnoreCase))
            {
                var scriptResult = await _scriptLoader.LoadAsync(pluginDir, manifest);
                if (!scriptResult.IsSuccess)
                {
                    Log.Error("脚本插件 {PluginId} 加载失败: {Error}",
                        manifest.Id, scriptResult.Error);
                    return;
                }

                plugin = new ScriptPluginAdapter(
                    scriptResult.Globals!, manifest.Id, manifest.Name, manifest.Version);
            }
            // 检测 Web 插件 (HTML/JS)
            else if (string.Equals(manifest.Runtime, "webview", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var webRuntime = new WebPluginRuntime(manifest, pluginDir);
                    plugin = new WebPluginAdapter(
                        webRuntime, manifest.Id, manifest.Name, manifest.Version,
                        pluginDir, manifest.EntryPoint);
                }
                catch (Exception ex)
                {
                    Log.Error("Web 插件 {PluginId} 创建失败: {Error}", manifest.Id, ex.Message);
                    return;
                }
            }
            else
            {
                var loadResult = await _loader.LoadAsync(pluginDir, manifest);
                if (!loadResult.IsSuccess)
                {
                    Log.Error("插件 {PluginId} 加载失败: {Error}",
                        manifest.Id, loadResult.Error);
                    return;
                }

                plugin = loadResult.Instance!;
                loadContext = loadResult.Context;
            }

            // ★ 先注册再初始化，以便 InitializeAsync 中权限检查能生效
            registry.Register(manifest, plugin, loadContext, pluginDir);
            _dirToPluginId[pluginDir] = manifest.Id;

            var hostApi = HostProvider.Instance;

            using (PluginAccessContext.Enter(manifest.Id))
            {
                var initOk = await plugin.InitializeAsync(hostApi);
                if (!initOk)
                {
                    Log.Error("插件 {PluginId} 初始化失败", manifest.Id);
                    registry.Unregister(manifest.Id);
                    if (loadContext != null) _loader.Unload(loadContext);
                    else _scriptLoader.Unload(manifest.Id);
                    return;
                }
            }

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
                        if (loadContext != null) _loader.Unload(loadContext);
                        else _scriptLoader.Unload(manifest.Id);
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

        /// <summary>
        /// 加载单文件 .csx 脚本（热插拔模式）
        /// 用户只需将 .csx 文件放入 Plugins/ 根目录即可使用，无需创建目录和 manifest.json
        /// </summary>
        private async Task TryLoadStandaloneScriptAsync(string scriptPath)
        {
            var fileName = Path.GetFileNameWithoutExtension(scriptPath);
            var scriptDir = Path.GetDirectoryName(scriptPath);

            if (string.IsNullOrEmpty(scriptDir))
                return;

            // 自动生成虚拟 manifest
            var manifest = new PluginManifest
            {
                Id = $"script-{fileName}",
                Name = fileName,
                Version = "1.0.0",
                Runtime = "csharp-script",
                EntryPoint = Path.GetFileName(scriptPath),
                Capabilities = new List<string>(), // 单文件脚本默认无权限限制
                Author = "User"
            };

            Log.Information("发现单文件脚本: {Name} ({Path})", fileName, scriptPath);

            var registry = HostProvider.Instance.PluginStore;

            // 检查是否已加载
            if (registry.Get(manifest.Id) != null)
            {
                Log.Debug("单文件脚本 {PluginId} 已加载，跳过", manifest.Id);
                return;
            }

            // 加载脚本
            var scriptResult = await _scriptLoader.LoadAsync(scriptDir, manifest);
            if (!scriptResult.IsSuccess)
            {
                Log.Error("单文件脚本 {Name} 加载失败: {Error}", fileName, scriptResult.Error);
                return;
            }

            var plugin = new ScriptPluginAdapter(
                scriptResult.Globals!, manifest.Id, manifest.Name, manifest.Version);

            // 注册并初始化
            registry.Register(manifest, plugin, null, scriptDir);
            _dirToPluginId[scriptPath] = manifest.Id; // 使用文件路径作为键

            var hostApi = HostProvider.Instance;

            using (PluginAccessContext.Enter(manifest.Id))
            {
                var initOk = await plugin.InitializeAsync(hostApi);
                if (!initOk)
                {
                    Log.Error("单文件脚本 {Name} 初始化失败", fileName);
                    registry.Unregister(manifest.Id);
                    _scriptLoader.Unload(manifest.Id);
                    return;
                }

                var startOk = await plugin.StartAsync();
                if (!startOk)
                {
                    Log.Error("单文件脚本 {Name} 启动失败", fileName);
                    _scriptLoader.Unload(manifest.Id);
                    return;
                }
            }

            registry.SetState(manifest.Id, PluginState.Running);
            DiscoveredManifests.Add(manifest);

            var entry = registry.Get(manifest.Id)!;
            LoadedPlugins.Add(entry);

            Log.Information("✅ 单文件脚本 {Name} 加载成功（热插拔模式）", fileName);
        }

        /// <summary>
        /// 加载单文件 .js/.ts 脚本（热插拔模式）
        /// 自动包装为 HTML 插件，提供 long.* API 访问
        /// </summary>
        private async Task TryLoadStandaloneJsScriptAsync(string scriptPath)
        {
            var fileName = Path.GetFileNameWithoutExtension(scriptPath);
            var scriptDir = Path.GetDirectoryName(scriptPath);
            var ext = Path.GetExtension(scriptPath).ToLowerInvariant();

            if (string.IsNullOrEmpty(scriptDir))
                return;

            Log.Information("发现单文件 JS/TS 脚本: {Name} ({Path})", fileName, scriptPath);

            // 创建临时插件目录
            var tempPluginDir = Path.Combine(scriptDir, $".long_temp_{fileName}");
            if (Directory.Exists(tempPluginDir))
                Directory.Delete(tempPluginDir, true);
            Directory.CreateDirectory(tempPluginDir);

            // 生成包装 HTML
            var scriptContent = await File.ReadAllTextAsync(scriptPath);
            var isTypeScript = ext == ".ts";

            var htmlContent = GenerateJsWrapperHtml(fileName, scriptContent, isTypeScript);
            var htmlPath = Path.Combine(tempPluginDir, "index.html");
            await File.WriteAllTextAsync(htmlPath, htmlContent);

            // 复制脚本文件（保留原始源码）
            var scriptDestPath = Path.Combine(tempPluginDir, Path.GetFileName(scriptPath));
            File.Copy(scriptPath, scriptDestPath, true);

            // 生成 manifest
            var manifest = new PluginManifest
            {
                Id = $"js-{fileName}",
                Name = fileName,
                Version = "1.0.0",
                Runtime = "html",
                EntryPoint = "index.html",
                Capabilities = new List<string>(), // 单文件脚本默认无权限限制
                Author = "User"
            };

            var registry = HostProvider.Instance.PluginStore;

            // 检查是否已加载
            if (registry.Get(manifest.Id) != null)
            {
                Log.Debug("单文件 JS/TS 脚本 {PluginId} 已加载，跳过", manifest.Id);
                return;
            }

            // 加载 HTML 插件
            var plugin = new WebPluginRuntime(manifest, tempPluginDir);
            registry.Register(manifest, plugin, null, tempPluginDir);
            _dirToPluginId[scriptPath] = manifest.Id; // 使用原始脚本路径作为键

            var hostApi = HostProvider.Instance;

            using (PluginAccessContext.Enter(manifest.Id))
            {
                var initOk = await plugin.InitializeAsync();
                if (!initOk)
                {
                    Log.Error("单文件 JS/TS 脚本 {Name} 初始化失败", fileName);
                    registry.Unregister(manifest.Id);
                    return;
                }

                // WebPluginRuntime 没有 Start/Stop 方法，初始化后即运行
                registry.SetState(manifest.Id, PluginState.Running);
            }
            DiscoveredManifests.Add(manifest);

            var entry = registry.Get(manifest.Id)!;
            LoadedPlugins.Add(entry);

            Log.Information("✅ 单文件 JS/TS 脚本 {Name} 加载成功（热插拔模式）", fileName);
        }

        private string GenerateJsWrapperHtml(string pluginName, string scriptContent, bool isTypeScript)
        {
            var scriptTag = isTypeScript
                ? $"<script type=\"module\">\n// TypeScript 代码（运行时编译）\n{scriptContent}\n</script>"
                : $"<script>\n{scriptContent}\n</script>";

            return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>{pluginName}</title>
    <style>
        body {{
            margin: 0;
            padding: 16px;
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: #1a1a1d;
            color: #f8fafc;
        }}
        .script-info {{
            padding: 12px;
            background: #2a2a2d;
            border-radius: 6px;
            border-left: 3px solid #38bdf8;
            margin-bottom: 16px;
        }}
        .script-info h3 {{
            margin: 0 0 8px 0;
            font-size: 14px;
            color: #38bdf8;
        }}
        .script-info p {{
            margin: 0;
            font-size: 12px;
            color: #999;
        }}
        #output {{
            white-space: pre-wrap;
            font-family: 'Consolas', monospace;
            font-size: 12px;
        }}
    </style>
</head>
<body>
    <div class=""script-info"">
        <h3>📜 {pluginName} {(isTypeScript ? "(TypeScript)" : "(JavaScript)")}</h3>
        <p>单文件脚本 · 热插拔模式 · 完整 long.* API 访问</p>
    </div>
    <div id=""output""></div>

    <!-- 拦截 console.log 显示在页面上 -->
    <script>
        const output = document.getElementById('output');
        const originalLog = console.log;
        console.log = function(...args) {{
            originalLog.apply(console, args);
            output.textContent += args.join(' ') + '\\n';
        }};
    </script>

    {scriptTag}
</body>
</html>";
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
