using System.IO;
using System.Text;
using System.Windows;
using LongBetterWindows.Host.Helpers;
using Microsoft.Web.WebView2.Wpf;

namespace LongBetterWindows.Host.Views
{
    public partial class DocViewer : Window
    {
        private readonly WebView2 _webView;

        public DocViewer()
        {
            Width = 800;
            Height = 600;
            MinWidth = 500;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.ToolWindow;
            ShowInTaskbar = false;

            _webView = new WebView2();
            Content = _webView;

            Loaded += async (_, _) =>
            {
                await _webView.EnsureCoreWebView2Async();
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
            await _webView.EnsureCoreWebView2Async();

            var html = BuildHtml(title: Title, markdown: markdown);
            _webView.CoreWebView2.NavigateToString(html);
        }

        private static string BuildHtml(string title, string markdown)
        {
            var escaped = EscapeHtml(markdown);
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='UTF-8'>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;max-width:720px;margin:32px auto;padding:0 24px;color:#1d1d1f;line-height:1.7;background:#fff;}");
            sb.AppendLine("h1{font-size:24px;border-bottom:2px solid #007AFF;padding-bottom:8px;color:#007AFF;}");
            sb.AppendLine("h2{font-size:18px;margin-top:24px;color:#007AFF;}");
            sb.AppendLine("h3{font-size:15px;margin-top:20px;}");
            sb.AppendLine("code{background:#f0f0f5;padding:2px 6px;border-radius:4px;font-size:13px;font-family:'Consolas','Monaco',monospace;}");
            sb.AppendLine("pre{background:#f5f5f7;padding:16px;border-radius:8px;overflow-x:auto;font-size:13px;line-height:1.5;}");
            sb.AppendLine("pre code{background:none;padding:0;}");
            sb.AppendLine("table{border-collapse:collapse;width:100%;margin:12px 0;}");
            sb.AppendLine("th,td{border:1px solid #e0e0e0;padding:8px 12px;text-align:left;font-size:13px;}");
            sb.AppendLine("th{background:#f5f5f7;font-weight:600;}");
            sb.AppendLine("ul,ol{padding-left:20px;}");
            sb.AppendLine("li{margin:4px 0;}");
            sb.AppendLine("a{color:#007AFF;}");
            sb.AppendLine("blockquote{border-left:3px solid #007AFF;padding-left:16px;color:#666;margin:12px 0;}");
            sb.AppendLine("hr{border:none;border-top:1px solid #e0e0e0;margin:24px 0;}");
            sb.AppendLine("strong{color:#1d1d1f;}");
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
            sb.AppendLine("  .replace(/^>(.+)$/gm, '<blockquote>$1</blockquote>')");
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
