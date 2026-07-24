using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace QuickLaunchPlugin;

public sealed record LaunchWindowLocalization(
    string Title,
    string SearchAutomationName,
    string EmptyHint,
    string ResultCountFormat,
    string NoMatches,
    string NavigationHint,
    string OpenLinkFormat,
    string ApplicationCategory,
    string CalculationCategory,
    string LinkCategory,
    string FileCategory,
    string ContentCategory,
    string ApplicationSubtitle,
    string CalculationSubtitle,
    string LinkSubtitle);

public partial class LaunchWindow : Window
{
    private readonly List<SmartEntry> _apps = new();
    private List<SmartEntry> _currentResults = [];
    private LaunchWindowLocalization _localization;
    private Action<SmartEntry?>? _onSelect;

    public LaunchWindow(LaunchWindowLocalization localization)
    {
        _localization = localization;
        InitializeComponent();
        LoadApps();
        ApplyLocalization(localization);
    }

    public static LaunchWindow Show(
        Action<SmartEntry?> onSelect,
        LaunchWindowLocalization localization,
        string? initialQuery = null)
    {
        var area = LongBetterWindows.Host.Services.MonitorHelper.GetCursorWorkArea();
        var window = new LaunchWindow(localization)
        {
            _onSelect = onSelect,
            Left = area.Left + (area.Width - 640) / 2,
            Top = area.Top + area.Height * 0.25,
        };
        window.Show();
        if (!string.IsNullOrWhiteSpace(initialQuery))
            window.SearchBox.Text = initialQuery;
        window.SearchBox.Focus();
        window.SearchBox.CaretIndex = window.SearchBox.Text.Length;
        return window;
    }

    private static List<SmartEntry>? _cachedApps;
    private static readonly object AppCacheLock = new();

    private void LoadApps()
    {
        _apps.AddRange(GetApplications());
    }

    internal static IReadOnlyList<SmartEntry> GetApplications()
    {
        lock (AppCacheLock)
        {
            if (_cachedApps != null)
                return _cachedApps;

            var apps = new List<SmartEntry>();
            var paths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            };

            foreach (var startMenu in paths)
            {
                if (!Directory.Exists(startMenu)) continue;
                var enumeration = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.System,
                };
                foreach (var lnk in Directory.EnumerateFiles(
                             startMenu, "*.lnk", enumeration))
                {
                    try
                    {
                        apps.Add(new SmartEntry
                        {
                            Name = Path.GetFileNameWithoutExtension(lnk),
                            Path = lnk,
                            Icon = "📦",
                            Category = "application",
                        });
                    }
                    catch { }
                }
            }

            apps.Sort((a, b) => string.Compare(
                a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _cachedApps = apps;
            return _cachedApps;
        }
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();

        if (string.IsNullOrEmpty(query))
        {
            _currentResults = [];
            ResultsList.ItemsSource = null;
            ResultsList.Visibility = Visibility.Collapsed;
            HintText.Text = _localization.EmptyHint;
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
                Category = "calculation",
            });
        }

        // 2. URL 检测
        if (IsUrl(query))
        {
            var url = query.StartsWith("http") ? query : "https://" + query;
            results.Add(new SmartEntry
            {
                Name = query,
                Path = url,
                Icon = "🌐",
                Category = "link",
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

        _currentResults = results;
        ApplyResultsProjection();
        ResultsList.ItemsSource = _currentResults;
        ResultsList.Visibility = results.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;

        if (results.Count > 0)
            ResultsList.SelectedIndex = 0;

        HintText.Text = results.Count > 0
            ? string.Format(_localization.ResultCountFormat, results.Count)
            : _localization.NoMatches;
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

        var remaining = 8;
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
        };
        foreach (var dir in dirs)
        {
            if (remaining <= 0) yield break;
            if (!Directory.Exists(dir)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*", enumeration)
                    .Where(path => Path.GetFileName(path).Contains(
                        query, StringComparison.OrdinalIgnoreCase));
            }
            catch { continue; }

            foreach (var f in files.Take(remaining))
            {
                remaining--;
                var name = Path.GetFileName(f);
                var ext = Path.GetExtension(f).ToLower();
                var icon = ext switch
                {
                    ".pdf" => "📕", ".doc" or ".docx" => "📝", ".xls" or ".xlsx" => "📊",
                    ".png" or ".jpg" or ".jpeg" or ".gif" => "🖼",
                    ".txt" or ".md" => "📄", ".zip" or ".rar" or ".7z" => "📦",
                    _ => "📁"
                };
                yield return new SmartEntry
                {
                    Name = name,
                    Path = f,
                    Icon = icon,
                    Category = "file",
                    Subtitle = dir,
                };
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
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
                });
            }
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
                            Category = "content",
                            Subtitle = "..." + preview + "...",
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

    public void ApplyLocalization(LaunchWindowLocalization localization)
    {
        _localization = localization;
        Title = localization.Title;
        System.Windows.Automation.AutomationProperties.SetName(
            SearchBox,
            localization.SearchAutomationName);
        NavigationHintText.Text = localization.NavigationHint;
        ApplyResultsProjection();
        if (string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            HintText.Text = localization.EmptyHint;
        }
        else
        {
            HintText.Text = _currentResults.Count > 0
                ? string.Format(
                    localization.ResultCountFormat,
                    _currentResults.Count)
                : localization.NoMatches;
        }
    }

    private void ApplyResultsProjection()
    {
        var selectedIndex = ResultsList.SelectedIndex;
        foreach (var entry in _currentResults)
            entry.ApplyLocalization(_localization);
        if (ResultsList.ItemsSource is not null)
        {
            ResultsList.ItemsSource = null;
            ResultsList.ItemsSource = _currentResults;
            if (selectedIndex >= 0 && selectedIndex < _currentResults.Count)
                ResultsList.SelectedIndex = selectedIndex;
        }
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
        var duration = Application.Current.Resources["Long.Motion.Normal"] is Duration token
            ? token.TimeSpan
            : TimeSpan.FromMilliseconds(180);
        Opacity = duration == TimeSpan.Zero ? 1 : 0;
        var translate = new TranslateTransform(0, duration == TimeSpan.Zero ? 0 : -8);
        MainBorder.RenderTransform = translate;
        if (duration == TimeSpan.Zero) return;

        var fadeIn = new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fadeIn);
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void SelectEntry(SmartEntry entry)
    {
        _onSelect?.Invoke(entry);
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
    public string DisplayName { get; private set; } = string.Empty;
    public string CategoryLabel { get; private set; } = string.Empty;
    public string? DisplaySubtitle { get; private set; }

    public void ApplyLocalization(LaunchWindowLocalization localization)
    {
        DisplayName = Category == "link"
            ? string.Format(localization.OpenLinkFormat, Name)
            : Name;
        CategoryLabel = Category switch
        {
            "application" => localization.ApplicationCategory,
            "calculation" => localization.CalculationCategory,
            "link" => localization.LinkCategory,
            "file" => localization.FileCategory,
            "content" => localization.ContentCategory,
            _ => Category,
        };
        DisplaySubtitle = Category switch
        {
            "application" => localization.ApplicationSubtitle,
            "calculation" => localization.CalculationSubtitle,
            "link" => localization.LinkSubtitle,
            _ => Subtitle,
        };
    }
}
