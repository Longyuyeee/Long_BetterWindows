using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using LongBetterWindows.Host.Helpers;
using Microsoft.Web.WebView2.Wpf;

namespace LongBetterWindows.Host.Views
{
    public partial class DocViewer : Window
    {
        private readonly WebView2 _webView;
        private string _markdown = string.Empty;
        private bool _themeSubscribed;

        public DocViewer()
        {
            InitializeComponent();
            _webView = new WebView2();
            WebViewHost.Children.Add(_webView);

            Loaded += async (_, _) =>
            {
                await _webView.EnsureCoreWebView2Async();
                _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                _webView.PreviewKeyDown += OnWebViewPreviewKeyDown;
                App.ThemeChanged += OnThemeChanged;
                _themeSubscribed = true;
                _webView.Focus();
            };

            Closed += (_, _) =>
            {
                if (_themeSubscribed)
                    App.ThemeChanged -= OnThemeChanged;
                if (_webView.CoreWebView2 != null)
                    _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                _webView.PreviewKeyDown -= OnWebViewPreviewKeyDown;
                _webView.Dispose();
            };
        }

        public static void ShowDoc(Window owner, string title, string markdown)
        {
            var viewer = new DocViewer
            {
                Title = title,
                Owner = owner,
                Opacity = 0,
            };

            viewer.Show();
            AnimationHelper.FadeIn(viewer, durationMs: 200);
            viewer.RenderMarkdown(markdown);
        }

        private async void RenderMarkdown(string markdown)
        {
            _markdown = markdown;
            try
            {
                await _webView.EnsureCoreWebView2Async();
                var html = BuildHtml(
                    title: Title,
                    markdown: markdown,
                    isDark: !App.IsLightTheme,
                    language: Services.ServicesInitializer.I18n.CurrentLanguage);
                _webView.CoreWebView2.NavigateToString(html);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DocViewer render error: {ex.Message}");
            }
        }

        private void OnThemeChanged(bool isLight)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnThemeChanged(isLight));
                return;
            }

            RenderMarkdown(_markdown);
        }

        private void OnWebMessageReceived(
            object? sender,
            Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (e.TryGetWebMessageAsString() == "closeWindow")
                Close();
        }

        private void OnWebViewPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;

            e.Handled = true;
            Close();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private static string BuildHtml(
            string title,
            string markdown,
            bool isDark,
            string language)
        {
            var escaped = EscapeHtml(markdown);
            var sb = new StringBuilder();
            var bg = isDark ? "#1E1F22" : "#FFFFFF";
            var text = isDark ? "#E8E8E8" : "#1D1D1F";
            var codeBg = isDark ? "#2D2D30" : "#F0F0F5";
            var preBg = isDark ? "#252528" : "#F5F5F7";
            var border = isDark ? "#3A3A3D" : "#E0E0E0";
            var thBg = isDark ? "#2D2D30" : "#F5F5F7";
            var blockquote = isDark ? "#888888" : "#666666";
            var accent = "#007AFF";

            sb.AppendLine($"<!DOCTYPE html><html lang='{language}'><head><meta charset='UTF-8'>");
            sb.AppendLine("<style>");
            sb.AppendLine($"body{{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;max-width:720px;margin:32px auto;padding:0 24px;color:{text};line-height:1.7;background:{bg};}}");
            sb.AppendLine($"h1{{font-size:24px;border-bottom:2px solid {accent};padding-bottom:8px;color:{accent};}}");
            sb.AppendLine($"h2{{font-size:18px;margin-top:24px;color:{accent};}}");
            sb.AppendLine($"h3{{font-size:15px;margin-top:20px;}}");
            sb.AppendLine($"code{{background:{codeBg};padding:2px 6px;border-radius:4px;font-size:13px;font-family:'Consolas','Monaco',monospace;}}");
            sb.AppendLine($"pre{{background:{preBg};padding:16px;border-radius:8px;overflow-x:auto;font-size:13px;line-height:1.5;}}");
            sb.AppendLine("pre code{background:none;padding:0;}");
            sb.AppendLine("table{border-collapse:collapse;width:100%;margin:12px 0;}");
            sb.AppendLine($"th,td{{border:1px solid {border};padding:8px 12px;text-align:left;font-size:13px;}}");
            sb.AppendLine($"th{{background:{thBg};font-weight:600;}}");
            sb.AppendLine("ul,ol{padding-left:20px;}");
            sb.AppendLine("li{margin:4px 0;}");
            sb.AppendLine($"a{{color:{accent};}}");
            sb.AppendLine($"blockquote{{border-left:3px solid {accent};padding-left:16px;color:{blockquote};margin:12px 0;}}");
            sb.AppendLine($"hr{{border:none;border-top:1px solid {border};margin:24px 0;}}");
            sb.AppendLine($"strong{{color:{text};}}");
            sb.AppendLine("*{scrollbar-width:thin;scrollbar-color:#7C879E transparent;}");
            sb.AppendLine("::-webkit-scrollbar{width:10px;height:10px;}::-webkit-scrollbar-track{background:transparent;}::-webkit-scrollbar-thumb{background:#7C879E;border:3px solid transparent;border-radius:8px;background-clip:padding-box;}::-webkit-scrollbar-thumb:hover{background:#626D82;background-clip:padding-box;}");
            sb.AppendLine("</style></head><body>");

            // Markdown content (must be before renderer script so it exists in DOM when JS runs)
            sb.AppendLine($"<script type='text/markdown' id='content'>{escaped}</script>");

            // Simple JS markdown renderer (runs after content element is parsed)
            sb.AppendLine("<script>");
            sb.AppendLine("(function(){");
            sb.AppendLine("var el = document.getElementById('content');");
            sb.AppendLine("if (!el) return;");
            sb.AppendLine("var md = el.textContent;");
            sb.AppendLine("var html = md");
            // Code blocks first (before other regexes touch backticks)
            sb.AppendLine("  .replace(/```(\\w*)\\n?([\\s\\S]*?)```/g, '<pre><code>$2</code></pre>')");
            // Headers
            sb.AppendLine("  .replace(/^### (.+)$/gm, '<h3>$1</h3>')");
            sb.AppendLine("  .replace(/^## (.+)$/gm, '<h2>$1</h2>')");
            sb.AppendLine("  .replace(/^# (.+)$/gm, '<h1>$1</h1>')");
            // Bold / italic / inline code
            sb.AppendLine("  .replace(/\\*\\*(.+?)\\*\\*/g, '<strong>$1</strong>')");
            sb.AppendLine("  .replace(/\\*(.+?)\\*/g, '<em>$1</em>')");
            sb.AppendLine("  .replace(/`([^`]+)`/g, '<code>$1</code>')");
            // Horizontal rule
            sb.AppendLine("  .replace(/^---+$/gm, '<hr>')");
            // Blockquotes
            sb.AppendLine("  .replace(/^&gt;\\s*(.*)$/gm, function(_,text){text=text.replace(/^&gt;\\s*/, '');return text ? '<blockquote>'+text+'</blockquote>' : '';})");
            // Links
            sb.AppendLine("  .replace(/\\[(.+?)\\]\\((.+?)\\)/g, '<a href=\"$2\">$1</a>')");
            // Tables
            sb.AppendLine("  .replace(/^\\|(.+)\\|$/gm, function(m){");
            sb.AppendLine("    var cells = m.split('|').filter(function(c){return c.trim();});");
            sb.AppendLine("    var tag = /^\\|[-:\\s|]+\\|$/.test(m) ? 'td' : 'th';");
            sb.AppendLine("    return '<tr>'+cells.map(function(c){return '<'+tag+'>'+c.trim()+'</'+tag+'>'}).join('')+'</tr>';");
            sb.AppendLine("  })");
            sb.AppendLine("  .replace(/((?:<tr>.*<\\/tr>\\n?)+)/g, '<table>$1</table>')");
            // Lists: group consecutive li items into ul blocks
            sb.AppendLine("  .replace(/((?:^- .+\\n?)+)/gm, function(block){");
            sb.AppendLine("    return '<ul>' + block.replace(/^- (.+)$/gm, '<li>$1</li>') + '</ul>';");
            sb.AppendLine("  })");
            // Paragraphs: process block by block, skip pre-rendered HTML blocks
            sb.AppendLine("  .replace(/(?:^|\\n\\n)((?!<(?:h[1-3]|ul|ol|pre|table|blockquote|hr))[\\s\\S]+?)(?=\\n\\n|$)/gm,");
            sb.AppendLine("    function(_, text){ var t = text.trim();");
            sb.AppendLine("      return t ? '<p>' + t.replace(/\\n/g, '<br>') + '</p>' : ''; })");
            sb.AppendLine("  .replace(/\\n\\n/g, '');");
            // Trim trailing newlines
            sb.AppendLine("  html = html.trim();");
            sb.AppendLine("document.body.innerHTML = html;");
            sb.AppendLine("})();");
            sb.AppendLine("document.addEventListener('keydown',function(e){if(e.key==='Escape'){e.preventDefault();window.chrome.webview.postMessage('closeWindow');}},true);");
            sb.AppendLine("</script>");

            sb.AppendLine("</body></html>");

            return sb.ToString();
        }

        private static string EscapeHtml(string text)
        {
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }
}
