using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using LongBetterWindows.Host.Helpers;
using Microsoft.Web.WebView2.Wpf;

namespace LongBetterWindows.Host.Views
{
    public partial class PluginDevTools : Window
    {
        private readonly WebView2 _webView;

        public PluginDevTools()
        {
            Width = 900; Height = 650;
            MinWidth = 600; MinHeight = 400;
            Title = "插件开发工具";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.ToolWindow;
            _webView = new WebView2();
            Content = _webView;

            Loaded += async (_, _) =>
            {
                await _webView.EnsureCoreWebView2Async();
                _webView.CoreWebView2.WebMessageReceived += OnJsMessage;

                var htmlPath = FindHtmlPath();
                if (htmlPath != null && File.Exists(htmlPath))
                    _webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
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
            }
        }

        private void ListPlugins()
        {
            var dirs = new List<object>();
            var srcDir = FindSrcDir();
            if (srcDir != null && Directory.Exists(srcDir))
            {
                foreach (var d in Directory.GetDirectories(srcDir))
                {
                    var name = Path.GetFileName(d);
                    if (name == "Templates" || name == "LongBetterWindows.Host") continue;
                    dirs.Add(new { name, path = d.Replace("\\", "/"), hasManifest = File.Exists(Path.Combine(d, "manifest.json")) });
                }
            }
            SendJs("pluginsListed", dirs);
        }

        private void ListFiles(string dir)
        {
            var files = new List<object>();
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (f.Contains("\\obj\\") || f.Contains("\\bin\\")) continue;
                    files.Add(new { name = Path.GetRelativePath(dir, f).Replace("\\", "/"), path = f.Replace("\\", "/") });
                }
            }
            SendJs("filesListed", new { dir, files });
        }

        private void OpenFile(string path)
        {
            if (!File.Exists(path)) return;
            var content = File.ReadAllText(path);
            SendJs("fileOpened", new { path, content });
        }

        private void SaveFile(string path, string content)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, content, Encoding.UTF8);
                SendJs("fileSaved", new { path, success = true });
            }
            catch (Exception ex) { SendJs("fileSaved", new { path, success = false, error = ex.Message }); }
        }

        private void PreviewFile(string path)
        {
            if (!File.Exists(path)) return;

            if (path.EndsWith(".html") || path.EndsWith(".htm"))
            {
                var w = new Window
                {
                    Title = "预览: " + Path.GetFileName(path),
                    Width = 500, Height = 500,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                };
                var wv = new WebView2();
                w.Content = wv;
                w.Loaded += async (_, _) =>
                {
                    await wv.EnsureCoreWebView2Async();
                    wv.CoreWebView2.Navigate(new Uri(path).AbsoluteUri);
                };
                w.Show();
            }
            else
            {
                // 其他文件类型：在编辑器中只读预览
                var content = File.ReadAllText(path);
                SendJs("fileOpened", new { path, content, readOnly = true });
            }
        }

        private void NewPlugin(string template, string name, string id, List<string>? capabilities)
        {
            var srcDir = FindSrcDir();
            if (srcDir == null) { SendJs("error", new { msg = "src/ 目录未找到" }); return; }
            var safeDir = id.Replace('.', '-').Replace("\\", "-").Replace("/", "-");
            var pluginDir = Path.Combine(srcDir, safeDir);
            if (Directory.Exists(pluginDir)) { SendJs("error", new { msg = "目录已存在" }); return; }
            Directory.CreateDirectory(pluginDir);

            var capsJson = capabilities != null && capabilities.Count > 0
                ? string.Join(", ", capabilities.Select(c => $"\"{c}\""))
                : "";

            if (template == "web" || template == "webview")
            {
                File.WriteAllText(Path.Combine(pluginDir, "manifest.json"),
                    $"{{\n  \"id\": \"{id}\",\n  \"version\": \"1.0.0\",\n  \"name\": \"{name}\",\n  \"entry_point\": \"index.html\",\n  \"runtime\": \"webview\",\n  \"capabilities\": [{capsJson}]\n}}\n");
                File.WriteAllText(Path.Combine(pluginDir, "index.html"),
                    "<!DOCTYPE html>\n<html lang=\"zh-CN\">\n<head>\n  <meta charset=\"UTF-8\">\n  <title>" + name + "</title>\n  <style>\n    * { margin: 0; padding: 0; box-sizing: border-box; }\n    body { font-family: -apple-system, 'Segoe UI', sans-serif; padding: 24px; background: #1E1F22; color: #E8E8E8; }\n    button { padding: 8px 16px; border: none; border-radius: 6px; background: #007AFF; color: #fff; cursor: pointer; font-size: 13px; }\n    button:hover { background: #0062CC; }\n  </style>\n</head>\n<body>\n  <h2 style=\"margin-bottom:12px\">" + name + "</h2>\n  <p style=\"color:#999;margin-bottom:16px\">插件已就绪，开始编写你的逻辑吧。</p>\n  <button onclick=\"long.ui.showToast('Hello from " + name + "')\">测试 Toast</button>\n  <script>\n    console.log('Plugin loaded:', long);\n  </script>\n</body>\n</html>\n");
            }
            else if (template == "dll" || template == "dotnet")
            {
                var safeName = name.Replace(" ", "").Replace("-", "_");
                File.WriteAllText(Path.Combine(pluginDir, "manifest.json"),
                    $"{{\n  \"id\": \"{id}\",\n  \"version\": \"1.0.0\",\n  \"name\": \"{name}\",\n  \"entry_point\": \"{safeName}.dll\",\n  \"runtime\": \"dotnet\",\n  \"capabilities\": [{capsJson}],\n  \"default_settings\": {{ \"auto_start\": true }}\n}}\n");
                File.WriteAllText(Path.Combine(pluginDir, safeName + ".csproj"),
                    "<Project Sdk=\"Microsoft.NET.Sdk\">\n\n  <PropertyGroup>\n    <OutputType>Library</OutputType>\n    <TargetFramework>net8.0-windows</TargetFramework>\n    <Nullable>enable</Nullable>\n    <ImplicitUsings>enable</ImplicitUsings>\n    <UseWPF>true</UseWPF>\n  </PropertyGroup>\n\n  <ItemGroup>\n    <Reference Include=\"LongBetterWindows.Host\">\n      <HintPath>..\\LongBetterWindows.Host\\bin\\Debug\\net8.0-windows\\LongBetterWindows.Host.dll</HintPath>\n    </Reference>\n  </ItemGroup>\n\n</Project>\n");
                File.WriteAllText(Path.Combine(pluginDir, safeName + "Impl.cs"),
                    "using LongBetterWindows.Host.Core;\n\nnamespace " + safeDir + "\n{\n    public class " + safeName + "Impl : ILongPlugin\n    {\n        public string Id => \"" + id + "\";\n        public string Name => \"" + name + "\";\n        public string Version => \"" + "1.0.0" + "\";\n        public PluginState State { get; private set; }\n\n        private IHostApi? _host;\n\n        public async Task<bool> InitializeAsync(IHostApi host)\n        {\n            _host = host;\n            State = PluginState.Loaded;\n            return true;\n        }\n\n        public async Task<bool> StartAsync()\n        {\n            State = PluginState.Running;\n            // TODO: 插件启动逻辑\n            return true;\n        }\n\n        public async Task<bool> StopAsync()\n        {\n            State = PluginState.Disabled;\n            // TODO: 清理逻辑\n            return true;\n        }\n    }\n}\n");
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
                .Select(c => new { id = c, name = c })
                .ToList();
            SendJs("capabilitiesListed", caps);
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
