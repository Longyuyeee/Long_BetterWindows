using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

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
        var area = SystemParameters.WorkArea;
        var window = new LaunchWindow
        {
            _onSelect = onSelect,
            Left = area.Left + (area.Width - 440) / 2,
            Top = area.Top + area.Height * 0.25,
        };
        window.Show();
        window.SearchBox.Focus();
    }

    private void LoadApps()
    {
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
            .Take(6)
            .ToList();
        results.AddRange(appResults);

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
