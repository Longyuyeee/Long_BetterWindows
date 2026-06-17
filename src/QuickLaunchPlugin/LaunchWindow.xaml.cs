using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Appearance;

namespace QuickLaunchPlugin;

public partial class LaunchWindow : Window
{
    private readonly List<SmartEntry> _apps = new();
    private Action<string?>? _onSelect;

    public LaunchWindow()
    {
        InitializeComponent();
        LoadApps();
    }

    public static void Show(Action<string?> onSelect)
    {
        var area = LongBetterWindows.Host.Services.MonitorHelper.GetCursorWorkArea();
        var window = new LaunchWindow
        {
            _onSelect = onSelect,
            Left = area.Left + (area.Width - 440) / 2,
            Top = area.Top + area.Height * 0.25,
        };
        window.Show();
        window.SearchBox.Focus();
    }

    private static List<SmartEntry>? _cachedApps;

    private void LoadApps()
    {
        if (_cachedApps != null)
        {
            _apps.AddRange(_cachedApps);
            return;
        }

        var paths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };

        foreach (var startMenu in paths)
        {
            if (!Directory.Exists(startMenu)) continue;
            foreach (var lnk in Directory.GetFiles(startMenu, "*.lnk", SearchOption.AllDirectories))
            {
                try
                {
                    _apps.Add(new SmartEntry
                    {
                        Name = Path.GetFileNameWithoutExtension(lnk),
                        Path = lnk,
                        Icon = "📦",
                        Category = "应用",
                    });
                }
                catch { }
            }
        }

        _apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        _cachedApps = new List<SmartEntry>(_apps);
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();

        if (string.IsNullOrEmpty(query))
        {
            ResultsList.Visibility = Visibility.Collapsed;
            HintText.Text = "搜索应用...";
            return;
        }

        var results = new List<SmartEntry>();

        // 1. 数学表达式
        if (TryEvaluateMath(query, out var mathResult))
        {
            results.Add(new SmartEntry
            {
                Name = $"{query} = {mathResult}",
                Path = mathResult,
                Icon = "🔢",
                Category = "计算",
                Subtitle = "Enter 复制到剪贴板",
            });
        }

        // 2. URL 检测
        if (IsUrl(query))
        {
            var url = query.StartsWith("http") ? query : "https://" + query;
            results.Add(new SmartEntry
            {
                Name = $"打开 {query}",
                Path = url,
                Icon = "🌐",
                Category = "链接",
                Subtitle = "Enter 浏览器打开",
            });
        }

        // 3. 应用搜索
        var appResults = _apps
            .Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToList();
        results.AddRange(appResults);

        // 4. 内容搜索 (grep: 以 > 开头)
        if (query.StartsWith(">") && query.Length > 2)
        {
            var grepQuery = query.Substring(1).Trim();
            if (grepQuery.Length >= 2)
            {
                var grepResults = SearchContent(grepQuery).Take(4).ToList();
                results.AddRange(grepResults);
            }
        }
        // 5. 文件搜索（桌面/文档/下载）
        else if (query.Length >= 2)
        {
            var fileResults = SearchFiles(query).Take(3).ToList();
            results.AddRange(fileResults);
        }

        ResultsList.ItemsSource = results;
        ResultsList.Visibility = results.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;

        if (results.Count > 0)
            ResultsList.SelectedIndex = 0;

        HintText.Text = results.Count > 0
            ? $"{results.Count} 个结果"
            : "无匹配结果";
    }

    private static bool TryEvaluateMath(string expr, out string result)
    {
        result = "";
        if (!Regex.IsMatch(expr, @"^[\d\s+\-*/().%\^]+$")) return false;
        if (!Regex.IsMatch(expr, @"\d")) return false;
        if (expr.Length < 2) return false;

        try
        {
            var clean = expr.Replace("^", "^^").Replace("%", "/100.0");
            var dt = new DataTable();
            var value = dt.Compute(expr, null);
            result = value?.ToString() ?? "";
            return !string.IsNullOrEmpty(result) && result != expr;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<SmartEntry> SearchFiles(string query)
    {
        var dirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            IEnumerable<string> files;
            try { files = Directory.GetFiles(dir, "*" + query + "*", SearchOption.TopDirectoryOnly); }
            catch { continue; }

            foreach (var f in files.Take(8))
            {
                var name = Path.GetFileName(f);
                var ext = Path.GetExtension(f).ToLower();
                var icon = ext switch
                {
                    ".pdf" => "📕", ".doc" or ".docx" => "📝", ".xls" or ".xlsx" => "📊",
                    ".png" or ".jpg" or ".jpeg" or ".gif" => "🖼",
                    ".txt" or ".md" => "📄", ".zip" or ".rar" or ".7z" => "📦",
                    _ => "📁"
                };
                yield return new SmartEntry { Name = name, Path = f, Icon = icon, Category = "文件", Subtitle = dir };
            }
        }
    }

    private static List<SmartEntry> SearchContent(string query)
    {
        var results = new List<SmartEntry>();
        var dirs = new[] { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".cs", ".json", ".xml", ".html", ".css", ".js", ".py", ".log", ".csv" };

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            string[] files;
            try { files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (var f in files.Take(200))
            {
                if (results.Count >= 4) return results;
                if (!exts.Contains(Path.GetExtension(f))) continue;
                try
                {
                    var content = File.ReadAllText(f);
                    var idx = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var start = Math.Max(0, idx - 20);
                        var len = Math.Min(80, content.Length - start);
                        var preview = content.Substring(start, len).Replace("\n", " ").Replace("\r", "");
                        results.Add(new SmartEntry
                        {
                            Name = Path.GetFileName(f), Path = f, Icon = "🔍",
                            Category = "内容匹配", Subtitle = "..." + preview + "...",
                        });
                    }
                }
                catch { }
            }
        }
        return results;
    }

    private static bool IsUrl(string text)
    {
        return Regex.IsMatch(text, @"^https?://") ||
               Regex.IsMatch(text, @"^[\w-]+\.(com|cn|org|net|io|dev|app|co)([/\w\-?=%.&]*)?$");
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (ResultsList.Items.Count > 0)
            {
                ResultsList.Focus();
                if (ResultsList.SelectedIndex < 0)
                    ResultsList.SelectedIndex = 0;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (ResultsList.SelectedItem is SmartEntry entry)
                SelectEntry(entry);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _onSelect?.Invoke(null);
            Close();
            e.Handled = true;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is SmartEntry entry)
            SelectEntry(entry);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !SearchBox.IsFocused)
        {
            _onSelect?.Invoke(null);
            Close();
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        _onSelect?.Invoke(null);
        Close();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 根据主题适配颜色
        var isDark = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme()
            == Wpf.Ui.Appearance.ApplicationTheme.Dark;
        MainBorder.Background = isDark
            ? new SolidColorBrush(Color.FromRgb(0x1E, 0x1F, 0x22))
            : new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF7));
        SearchBox.Foreground = isDark
            ? new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8))
            : new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1F));

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fadeIn);
    }

    private void SelectEntry(SmartEntry entry)
    {
        if (entry.Category == "计算")
        {
            // 复制计算结果到剪贴板
            try { System.Windows.Clipboard.SetText(entry.Path!); } catch { }
        }

        _onSelect?.Invoke(entry.Path);
        Close();
    }
}

public class SmartEntry
{
    public string Name { get; init; } = string.Empty;
    public string? Path { get; init; }
    public string Icon { get; init; } = "📦";
    public string Category { get; init; } = "";
    public string? Subtitle { get; init; }
}
