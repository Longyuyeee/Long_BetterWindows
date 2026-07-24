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
        private readonly WebView2 _webView;
        private readonly string _pluginsRoot;

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
                _webView.Dispose();
            };
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
                    throw new UnauthorizedAccessException("只能编辑 Plugins 目录中的文件。");

                var dir = Path.GetDirectoryName(safePath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(safePath, content, Encoding.UTF8);
                SendJs("fileSaved", new { path = safePath, success = true });
            }
            catch (Exception ex) { SendJs("fileSaved", new { path, success = false, error = ex.Message }); }
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
                SendJs("error", new { msg = "DLL 插件需要编译环境，请使用项目脚手架；内置编辑器当前支持 Web 和 C# 脚本插件。" });
                return;
            }

            var safeDir = Regex.Replace(id, "[^a-zA-Z0-9_-]", "-").Trim('-');
            if (string.IsNullOrWhiteSpace(safeDir)) safeDir = "my-plugin";
            var pluginDir = Path.Combine(_pluginsRoot, safeDir);
            if (Directory.Exists(pluginDir)) { SendJs("error", new { msg = "目录已存在" }); return; }
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
                    "// " + name + "\n// 可用变量: Host (IHostApi), Id, Name\n\nStart = async () =>\n{\n    // 插件启动时执行\n    // Host.Notification.Show(\"Hello!\");\n};\n\nStop = async () =>\n{\n    // 插件停止时执行\n};\n");
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
                        name = info.DisplayName,
                        icon = info.Icon,
                        description = info.Description,
                        level = info.Level.ToString().ToLowerInvariant()
                    };
                })
                .ToList();
            SendJs("capabilitiesListed", caps);
        }

        public static string BuildWebPluginTemplate(string name)
        {
            var safeTitle = System.Net.WebUtility.HtmlEncode(name);
            var safeJsName = JsonSerializer.Serialize(name);
            return $$"""
                <!DOCTYPE html>
                <html lang="zh-CN">
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
                        <p class="long-page-description">插件已就绪。界面会自动继承 Long 主题、动效和无障碍状态。</p>
                      </div>
                    </header>
                    <section class="long-card long-stack" aria-labelledby="welcome-title">
                      <div>
                        <h2 id="welcome-title">开始创作</h2>
                        <p class="long-secondary">使用 Long Web UI Kit 组件，并通过 long.* API 调用宿主能力。</p>
                      </div>
                      <div class="long-toolbar">
                        <button class="long-button--primary" id="helloButton">测试 Toast</button>
                        <span class="long-badge">UI Kit 1.0</span>
                      </div>
                      <div class="long-status" id="status" role="status" aria-live="polite"></div>
                    </section>
                  </main>
                  <script>
                    const pluginName = {{safeJsName}};
                    document.getElementById('helloButton').addEventListener('click', async () => {
                      await long.ui.showToast(`Hello from ${pluginName}`);
                      document.getElementById('status').textContent = 'Toast 已发送';
                    });
                  </script>
                </body>
                </html>
                """;
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
