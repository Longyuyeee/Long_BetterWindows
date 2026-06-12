using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace LongBetterWindows.Host.Views
{
    public partial class PluginDevTools : Window
    {
        private readonly WebView2 _webView = new();

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
            new PluginDevTools { Owner = owner }.Show();
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
                case "newPlugin": NewPlugin(msg.Template ?? "web", msg.Name ?? "my-plugin", msg.Id ?? "com.example.plugin"); break;
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

        private void NewPlugin(string template, string name, string id)
        {
            var srcDir = FindSrcDir();
            if (srcDir == null) { SendJs("error", new { msg = "src/ 目录未找到" }); return; }
            var safeDir = id.Replace('.', '-').Replace("\\", "-").Replace("/", "-");
            var pluginDir = Path.Combine(srcDir, safeDir);
            if (Directory.Exists(pluginDir)) { SendJs("error", new { msg = "目录已存在" }); return; }
            Directory.CreateDirectory(pluginDir);

            if (template == "web" || template == "webview")
            {
                File.WriteAllText(Path.Combine(pluginDir, "manifest.json"),
                    $"{{\n  \"id\": \"{id}\",\n  \"version\": \"1.0.0\",\n  \"name\": \"{name}\",\n  \"entry_point\": \"index.html\",\n  \"runtime\": \"webview\",\n  \"capabilities\": []\n}}\n");
                File.WriteAllText(Path.Combine(pluginDir, "index.html"),
                    "<!DOCTYPE html>\n<html>\n<head><meta charset='UTF-8'><title>" + name + "</title></head>\n<body>\n  <h2>" + name + "</h2>\n  <script>\n    console.log('Plugin:', long);\n  </script>\n</body>\n</html>\n");
            }
            else
            {
                File.WriteAllText(Path.Combine(pluginDir, "manifest.json"),
                    $"{{\n  \"id\": \"{id}\",\n  \"version\": \"1.0.0\",\n  \"name\": \"{name}\",\n  \"entry_point\": \"plugin.csx\",\n  \"runtime\": \"csharp-script\",\n  \"capabilities\": []\n}}\n");
                File.WriteAllText(Path.Combine(pluginDir, "plugin.csx"),
                    "// " + name + "\nStart = async () => { /* code */ };\nStop = async () => { /* cleanup */ };\n");
            }

            SendJs("pluginCreated", new { dir = pluginDir.Replace("\\", "/"), name, id });
            ListPlugins();
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
        }
    }
}
