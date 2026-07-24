using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using LongBetterWindows.Host.Helpers;
using LongBetterWindows.Host.Services;
using Microsoft.Web.WebView2.Wpf;

namespace LongBetterWindows.Host.Views
{
    public partial class PluginDevTools : Window
    {
        private static readonly string[] WorkbenchLocalizationKeys =
        [
            "developer.workbench.pageTitle",
            "developer.workbench.plugins",
            "developer.workbench.apiSearch",
            "developer.workbench.new",
            "developer.workbench.save",
            "developer.workbench.preview",
            "developer.workbench.search",
            "developer.workbench.apiHints",
            "developer.workbench.logs",
            "developer.workbench.logs.tooltip",
            "developer.workbench.refresh",
            "developer.workbench.empty.title",
            "developer.workbench.empty.desc",
            "developer.workbench.search.placeholder",
            "developer.workbench.search.previous",
            "developer.workbench.search.next",
            "developer.workbench.search.close",
            "developer.workbench.logs.select",
            "developer.workbench.logs.refresh",
            "developer.workbench.logs.clear",
            "developer.workbench.logs.close",
            "developer.workbench.logs.empty",
            "developer.workbench.newDialog.title",
            "developer.workbench.newDialog.type",
            "developer.workbench.newDialog.webType",
            "developer.workbench.newDialog.scriptType",
            "developer.workbench.newDialog.name",
            "developer.workbench.newDialog.namePlaceholder",
            "developer.workbench.newDialog.id",
            "developer.workbench.newDialog.capabilities",
            "developer.workbench.plugins.empty",
            "developer.workbench.logs.selectRequired",
            "developer.workbench.logs.loading",
            "developer.workbench.logs.pluginEmpty",
            "developer.workbench.logs.cleared",
            "developer.workbench.files",
            "developer.workbench.readOnly",
            "developer.workbench.closeUnsaved",
            "developer.workbench.confirm.title",
            "developer.workbench.confirm.discard",
            "developer.workbench.status.saved",
            "developer.workbench.status.created",
            "developer.workbench.status.previewing",
            "developer.workbench.error.save",
            "developer.workbench.error.dllUnsupported",
            "developer.workbench.error.directoryExists",
            "developer.workbench.capabilities.empty",
            "action.create",
            "action.cancel",
        ];

        private static readonly (string Api, string DescriptionKey, string Capability)[] WorkbenchApiCatalog =
        [
            ("long.app.getVersion()", "developer.workbench.api.getVersion", ""),
            ("long.app.log()", "developer.workbench.api.log", ""),
            ("long.clipboard.getText()", "developer.workbench.api.clipboardGet", "system.clipboard"),
            ("long.clipboard.setText()", "developer.workbench.api.clipboardSet", "system.clipboard"),
            ("long.hotkey.register()", "developer.workbench.api.hotkeyRegister", "system.hotkey"),
            ("long.shell.getSelectedItems()", "developer.workbench.api.shellSelection", "shell.selection"),
            ("long.process.start()", "developer.workbench.api.processStart", "system.process"),
            ("long.process.getList()", "developer.workbench.api.processList", "system.process"),
            ("long.process.kill()", "developer.workbench.api.processKill", "system.process"),
            ("long.fileOps.copy()", "developer.workbench.api.fileCopy", "file.ops"),
            ("long.fileOps.move()", "developer.workbench.api.fileMove", "file.ops"),
            ("long.fileOps.delete()", "developer.workbench.api.fileDelete", "file.ops"),
            ("long.fileSystem.enumerate()", "developer.workbench.api.fsEnumerate", "filesystem.advanced"),
            ("long.fileSystem.hash()", "developer.workbench.api.fsHash", "filesystem.advanced"),
            ("long.fileSystem.findDuplicates()", "developer.workbench.api.fsDuplicates", "filesystem.advanced"),
            ("long.fileSystem.searchContent()", "developer.workbench.api.fsSearch", "filesystem.advanced"),
            ("long.performance.getCpuUsage()", "developer.workbench.api.cpuUsage", "system.performance"),
            ("long.performance.getMemoryInfo()", "developer.workbench.api.memoryInfo", "system.performance"),
            ("long.performance.getSystemInfo()", "developer.workbench.api.systemInfo", "system.performance"),
            ("long.networkPort.getTcpListeners()", "developer.workbench.api.tcpListeners", "network.ports"),
            ("long.networkPort.findPortOwner()", "developer.workbench.api.portOwner", "network.ports"),
            ("long.network.getSpeed()", "developer.workbench.api.networkSpeed", "network.monitor"),
            ("long.audio.getVolume()", "developer.workbench.api.volumeGet", "system.audio"),
            ("long.audio.setVolume()", "developer.workbench.api.volumeSet", "system.audio"),
            ("long.power.getStatus()", "developer.workbench.api.powerStatus", "system.power"),
            ("long.power.lock()", "developer.workbench.api.powerLock", "system.power"),
            ("long.theme.get()", "developer.workbench.api.themeGet", "system.theme"),
            ("long.theme.set()", "developer.workbench.api.themeSet", "system.theme"),
            ("long.wallpaper.set()", "developer.workbench.api.wallpaperSet", "system.wallpaper"),
            ("long.brightness.set()", "developer.workbench.api.brightnessSet", "display.brightness"),
            ("long.pinyin.get()", "developer.workbench.api.pinyinGet", "text.pinyin"),
            ("long.pinyin.match()", "developer.workbench.api.pinyinMatch", "text.pinyin"),
            ("long.input.keyPress()", "developer.workbench.api.keyPress", "system.input"),
            ("long.input.mouseClick()", "developer.workbench.api.mouseClick", "system.input"),
            ("long.cache.getStatistics()", "developer.workbench.api.cacheStats", "system.cache"),
            ("long.cache.cleanTemp()", "developer.workbench.api.cacheClean", "system.cache"),
            ("long.schedule.create()", "developer.workbench.api.scheduleCreate", "system.schedule"),
            ("long.schedule.getAll()", "developer.workbench.api.scheduleList", "system.schedule"),
            ("long.ui.showToast()", "developer.workbench.api.showToast", "system.notification"),
            ("long.ui.confirm()", "developer.workbench.api.confirm", "ui.window"),
            ("long.ui.prompt()", "developer.workbench.api.prompt", "ui.window"),
            ("long.ui.createWindow()", "developer.workbench.api.createWindow", "ui.window"),
        ];

        private readonly WebView2 _webView;
        private readonly string _pluginsRoot;
        private bool _pageReady;
        private bool _languageSubscribed;

        public PluginDevTools()
        {
            _pluginsRoot = Path.Combine(AppContext.BaseDirectory, "Plugins");
            Directory.CreateDirectory(_pluginsRoot);

            Width = 900; Height = 650;
            MinWidth = 600; MinHeight = 400;
            Title = ServicesInitializer.I18n.T("developer.workbench.windowTitle");
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.ToolWindow;
            _webView = new WebView2();
            Content = _webView;

            Loaded += async (_, _) =>
            {
                try
                {
                    await _webView.EnsureCoreWebView2Async();
                    _webView.CoreWebView2.WebMessageReceived += OnJsMessage;
                    _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                    _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                    ServicesInitializer.I18n.LanguageChanged += OnLanguageChanged;
                    _languageSubscribed = true;

                    var htmlPath = FindHtmlPath();
                    if (htmlPath != null && File.Exists(htmlPath))
                        _webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PluginDevTools init error: {ex.Message}");
                }
            };

            Closed += (_, _) =>
            {
                if (_languageSubscribed)
                    ServicesInitializer.I18n.LanguageChanged -= OnLanguageChanged;
                if (_webView.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.WebMessageReceived -= OnJsMessage;
                    _webView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                    _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                }
                _webView.Dispose();
            };
        }

        private void OnNavigationStarting(
            object? sender,
            Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
        {
            _pageReady = false;
        }

        private void OnNavigationCompleted(
            object? sender,
            Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            _pageReady = e.IsSuccess;
            if (_pageReady)
                SendLocalization();
        }

        private void OnLanguageChanged(string language)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnLanguageChanged(language));
                return;
            }

            Title = ServicesInitializer.I18n.T("developer.workbench.windowTitle");
            SendLocalization();
        }

        private void SendLocalization()
        {
            if (!_pageReady)
                return;

            var strings = WorkbenchLocalizationKeys.ToDictionary(
                key => key,
                key => ServicesInitializer.I18n.T(key),
                StringComparer.Ordinal);
            var apiCatalog = WorkbenchApiCatalog.Select(entry => new
            {
                api = entry.Api,
                description = ServicesInitializer.I18n.T(entry.DescriptionKey),
                capability = entry.Capability,
            });
            SendJs(
                "localization",
                new
                {
                    language = ServicesInitializer.I18n.CurrentLanguage,
                    strings,
                    apiCatalog,
                });
        }

        public static void Open(Window owner)
        {
            var tools = new PluginDevTools { Owner = owner, Opacity = 0 };
            tools.Show();
            AnimationHelper.FadeIn(tools, durationMs: 200);
        }

        private static string? FindHtmlPath()
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                var path = Path.Combine(dir, "Views", "PluginDevTools.html");
                if (File.Exists(path)) return path;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return null;
        }

        private void OnJsMessage(object? s, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            var msg = JsonSerializer.Deserialize<EditorMsg>(e.WebMessageAsJson);
            if (msg == null) return;

            switch (msg.Action)
            {
                case "listPlugins": ListPlugins(); break;
                case "listFiles": ListFiles(msg.Path ?? ""); break;
                case "openFile": OpenFile(msg.Path ?? ""); break;
                case "saveFile": SaveFile(msg.Path ?? "", msg.Content ?? ""); break;
                case "preview": PreviewFile(msg.Path ?? ""); break;
                case "newPlugin": NewPlugin(msg.Template ?? "web", msg.Name ?? "my-plugin", msg.Id ?? "com.example.plugin", msg.Capabilities); break;
                case "listCapabilities": ListCapabilities(); break;
                case "getLogs": GetPluginLogs(msg.Id ?? ""); break;
            }
        }

        private void ListPlugins()
        {
            var dirs = new List<object>();
            foreach (var d in Directory.GetDirectories(_pluginsRoot))
            {
                var name = Path.GetFileName(d);
                if (name.StartsWith(".long_temp_", StringComparison.OrdinalIgnoreCase)) continue;
                var manifestPath = Path.Combine(d, "manifest.json");
                if (!File.Exists(manifestPath)) continue;

                var id = name;
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                    var root = document.RootElement;
                    if (root.TryGetProperty("id", out var idValue))
                        id = idValue.GetString() ?? id;
                    if (root.TryGetProperty("name", out var nameValue))
                        name = nameValue.GetString() ?? name;
                }
                catch { }

                dirs.Add(new { id, name, path = d.Replace("\\", "/"), hasManifest = true });
            }
            SendJs("pluginsListed", dirs);
        }

        private void GetPluginLogs(string pluginId)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                SendJs("pluginLogs", new { pluginId, lines = Array.Empty<string>() });
                return;
            }

            var logDirs = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "logs"),
                Path.Combine(AppContext.BaseDirectory, "logs"),
            };

            var logFile = logDirs
                .Where(Directory.Exists)
                .SelectMany(dir => Directory.GetFiles(dir, "log*.txt"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            var lines = logFile == null
                ? new List<string>()
                : File.ReadLines(logFile)
                    .Where(line => line.Contains(pluginId, StringComparison.OrdinalIgnoreCase))
                    .TakeLast(200)
                    .ToList();

            SendJs("pluginLogs", new { pluginId, lines });
        }

        private void ListFiles(string dir)
        {
            var files = new List<object>();
            if (TryResolvePluginPath(dir, out var safeDir) && Directory.Exists(safeDir))
            {
                foreach (var f in Directory.GetFiles(safeDir, "*", SearchOption.AllDirectories))
                {
                    if (f.Contains("\\obj\\") || f.Contains("\\bin\\")) continue;
                    files.Add(new { name = Path.GetRelativePath(safeDir, f).Replace("\\", "/"), path = f.Replace("\\", "/") });
                }
            }
            SendJs("filesListed", new { dir = string.IsNullOrEmpty(safeDir) ? dir : safeDir, files });
        }

        private void OpenFile(string path)
        {
            if (!TryResolvePluginPath(path, out var safePath) || !File.Exists(safePath)) return;
            var content = File.ReadAllText(safePath);
            SendJs("fileOpened", new { path = safePath, content });
        }

        private void SaveFile(string path, string content)
        {
            try
            {
                if (!TryResolvePluginPath(path, out var safePath))
                    throw new UnauthorizedAccessException();

                var dir = Path.GetDirectoryName(safePath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(safePath, content, Encoding.UTF8);
                SendJs("fileSaved", new { path = safePath, success = true });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"PluginDevTools save error: {ex.Message}");
                SendJs(
                    "fileSaved",
                    new
                    {
                        path,
                        success = false,
                        errorKey = "developer.workbench.error.save",
                    });
            }
        }

        private void PreviewFile(string path)
        {
            if (!TryResolvePluginPath(path, out var safePath) || !File.Exists(safePath)) return;

            if (safePath.EndsWith(".html") || safePath.EndsWith(".htm"))
            {
                var w = new Window
                {
                    Title = string.Format(
                        ServicesInitializer.I18n.T("developer.workbench.previewTitle"),
                        Path.GetFileName(safePath)),
                    Width = 500, Height = 500,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                };
                var wv = new WebView2();
                w.Content = wv;
                w.Loaded += async (_, _) =>
                {
                    await wv.EnsureCoreWebView2Async();
                    wv.CoreWebView2.Navigate(new Uri(safePath).AbsoluteUri);
                };
                w.Show();
            }
            else
            {
                // 其他文件类型：在编辑器中只读预览
                var content = File.ReadAllText(safePath);
                SendJs("fileOpened", new { path = safePath, content, readOnly = true });
            }
        }

        private void NewPlugin(string template, string name, string id, List<string>? capabilities)
        {
            if (template is "dll" or "dotnet")
            {
                SendJs(
                    "error",
                    new { key = "developer.workbench.error.dllUnsupported" });
                return;
            }

            var safeDir = Regex.Replace(id, "[^a-zA-Z0-9_-]", "-").Trim('-');
            if (string.IsNullOrWhiteSpace(safeDir)) safeDir = "my-plugin";
            var pluginDir = Path.Combine(_pluginsRoot, safeDir);
            if (Directory.Exists(pluginDir))
            {
                SendJs(
                    "error",
                    new { key = "developer.workbench.error.directoryExists" });
                return;
            }
            Directory.CreateDirectory(pluginDir);

            var capsJson = capabilities != null && capabilities.Count > 0
                ? string.Join(", ", capabilities.Select(c => $"\"{c}\""))
                : "";

            if (template == "web" || template == "webview")
            {
                File.WriteAllText(Path.Combine(pluginDir, "manifest.json"),
                    $"{{\n  \"id\": \"{id}\",\n  \"version\": \"1.0.0\",\n  \"name\": \"{name}\",\n  \"entry_point\": \"index.html\",\n  \"runtime\": \"webview\",\n  \"capabilities\": [{capsJson}]\n}}\n");
                File.WriteAllText(
                    Path.Combine(pluginDir, "index.html"),
                    BuildWebPluginTemplate(name));
            }
            else if (template == "dll" || template == "dotnet")
            {
                var safeName = name.Replace(" ", "").Replace("-", "_");
                File.WriteAllText(Path.Combine(pluginDir, "manifest.json"),
                    $"{{\n  \"id\": \"{id}\",\n  \"version\": \"1.0.0\",\n  \"name\": \"{name}\",\n  \"entry_point\": \"{safeName}.dll\",\n  \"runtime\": \"dotnet\",\n  \"capabilities\": [{capsJson}],\n  \"default_settings\": {{ \"auto_start\": true }}\n}}\n");
                File.WriteAllText(Path.Combine(pluginDir, safeName + ".csproj"),
                    "<Project Sdk=\"Microsoft.NET.Sdk\">\n\n  <PropertyGroup>\n    <OutputType>Library</OutputType>\n    <TargetFramework>net8.0-windows</TargetFramework>\n    <Nullable>enable</Nullable>\n    <ImplicitUsings>enable</ImplicitUsings>\n    <UseWPF>true</UseWPF>\n  </PropertyGroup>\n\n  <ItemGroup>\n    <Reference Include=\"LongBetterWindows.Host\">\n      <HintPath>..\\LongBetterWindows.Host\\bin\\Debug\\net8.0-windows\\LongBetterWindows.Host.dll</HintPath>\n    </Reference>\n  </ItemGroup>\n\n</Project>\n");
                File.WriteAllText(Path.Combine(pluginDir, safeName + "Impl.cs"),
                    "using LongBetterWindows.Host.Core;\n\nnamespace " + safeDir + "\n{\n    public class " + safeName + "Impl : ILongPlugin\n    {\n        public string Id => \"" + id + "\";\n        public string Name => \"" + name + "\";\n        public string Version => \"" + "1.0.0" + "\";\n        public PluginState State { get; private set; }\n\n        private IHostApi? _host;\n\n        public async Task<bool> InitializeAsync(IHostApi host)\n        {\n            _host = host;\n            State = PluginState.Loaded;\n            return true;\n        }\n\n        public async Task<bool> StartAsync()\n        {\n            State = PluginState.Running;\n            // TODO: 插件启动逻辑\n            return true;\n        }\n\n        public async Task<bool> StopAsync()\n        {\n            State = PluginState.Stopped;\n            // TODO: 清理逻辑\n            return true;\n        }\n    }\n}\n");
            }
            else // script
            {
                File.WriteAllText(Path.Combine(pluginDir, "manifest.json"),
                    $"{{\n  \"id\": \"{id}\",\n  \"version\": \"1.0.0\",\n  \"name\": \"{name}\",\n  \"entry_point\": \"plugin.csx\",\n  \"runtime\": \"csharp-script\",\n  \"capabilities\": [{capsJson}]\n}}\n");
                File.WriteAllText(Path.Combine(pluginDir, "plugin.csx"),
                    BuildScriptPluginTemplate(
                        name,
                        key => ServicesInitializer.I18n.T(key)));
            }

            SendJs("pluginCreated", new { dir = pluginDir.Replace("\\", "/"), name, id });
            ListPlugins();
        }

        private void ListCapabilities()
        {
            var caps = Engine.ManifestReader.KnownCapabilities
                .OrderBy(c => c)
                .Select(c =>
                {
                    var info = CapabilityMetadata.GetInfo(c);
                    return new
                    {
                        id = c,
                        name = ServicesInitializer.I18n.T(
                            $"capability.{c}.name",
                            info.DisplayName),
                        icon = info.Icon,
                        description = ServicesInitializer.I18n.T(
                            $"capability.{c}.description",
                            info.Description),
                        level = info.Level.ToString().ToLowerInvariant()
                    };
                })
                .ToList();
            SendJs("capabilitiesListed", caps);
        }

        public static string BuildWebPluginTemplate(
            string name,
            string? language = null,
            Func<string, string>? translate = null)
        {
            language ??= ServicesInitializer.I18n.CurrentLanguage;
            translate ??= key => ServicesInitializer.I18n.T(key);
            var safeTitle = System.Net.WebUtility.HtmlEncode(name);
            var safeJsName = JsonSerializer.Serialize(name);
            var safeLanguage = System.Net.WebUtility.HtmlEncode(language);
            var readyDescription = System.Net.WebUtility.HtmlEncode(
                translate("developer.workbench.template.readyDescription"));
            var welcomeTitle = System.Net.WebUtility.HtmlEncode(
                translate("developer.workbench.template.welcomeTitle"));
            var guide = System.Net.WebUtility.HtmlEncode(
                translate("developer.workbench.template.guide"));
            var testToast = System.Net.WebUtility.HtmlEncode(
                translate("developer.workbench.template.testToast"));
            var toastSent = JsonSerializer.Serialize(
                translate("developer.workbench.template.toastSent"));
            return $$"""
                <!DOCTYPE html>
                <html lang="{{safeLanguage}}">
                <head>
                  <meta charset="UTF-8">
                  <meta name="viewport" content="width=device-width, initial-scale=1">
                  <title>{{safeTitle}}</title>
                </head>
                <body>
                  <main class="long-page long-stack">
                    <header class="long-page-header">
                      <div>
                        <div class="long-eyebrow">LONG PLUGIN</div>
                        <h1 class="long-page-title">{{safeTitle}}</h1>
                        <p class="long-page-description">{{readyDescription}}</p>
                      </div>
                    </header>
                    <section class="long-card long-stack" aria-labelledby="welcome-title">
                      <div>
                        <h2 id="welcome-title">{{welcomeTitle}}</h2>
                        <p class="long-secondary">{{guide}}</p>
                      </div>
                      <div class="long-toolbar">
                        <button class="long-button--primary" id="helloButton">{{testToast}}</button>
                        <span class="long-badge">UI Kit 1.0</span>
                      </div>
                      <div class="long-status" id="status" role="status" aria-live="polite"></div>
                    </section>
                  </main>
                  <script>
                    const pluginName = {{safeJsName}};
                    document.getElementById('helloButton').addEventListener('click', async () => {
                      await long.ui.showToast(`Hello from ${pluginName}`);
                      document.getElementById('status').textContent = {{toastSent}};
                    });
                  </script>
                </body>
                </html>
                """;
        }

        private static string BuildScriptPluginTemplate(
            string name,
            Func<string, string> translate)
        {
            return
                $"// {name}\n" +
                $"// {translate("developer.workbench.template.script.variables")}: Host (IHostApi), Id, Name\n\n" +
                "Start = async () =>\n{\n" +
                $"    // {translate("developer.workbench.template.script.start")}\n" +
                "    // Host.Notification.Show(\"Hello!\");\n" +
                "};\n\n" +
                "Stop = async () =>\n{\n" +
                $"    // {translate("developer.workbench.template.script.stop")}\n" +
                "};\n";
        }

        private static string? FindSrcDir()
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                var src = Path.Combine(dir, "src");
                if (Directory.Exists(src)) return src;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return null;
        }

        private bool TryResolvePluginPath(string path, out string safePath)
        {
            safePath = string.Empty;
            try
            {
                var root = Path.GetFullPath(_pluginsRoot)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var fullPath = Path.GetFullPath(path);
                if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
                safePath = fullPath;
                return true;
            }
            catch { return false; }
        }

        private async void SendJs(string type, object? data = null)
        {
            var json = JsonSerializer.Serialize(new { type, data });
            await _webView.CoreWebView2.ExecuteScriptAsync($"handleMessage({json})");
        }

        private class EditorMsg
        {
            public string Action { get; set; } = "";
            public string? Path { get; set; }
            public string? Content { get; set; }
            public string? Template { get; set; }
            public string? Name { get; set; }
            public string? Id { get; set; }
            public List<string>? Capabilities { get; set; }
            public bool? ReadOnly { get; set; }
        }
    }
}
