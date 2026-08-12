using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LongBetterWindows.PluginSdk.Wpf;

namespace QuickLaunchPlugin;

public sealed record LaunchWindowLocalization(
    string Title,
    string SearchAutomationName,
    string EmptyHint,
    string ResultCountFormat,
    string LimitedResultCountFormat,
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
    private CancellationTokenSource? _searchCancellation;
    private readonly QuickLaunchDiskSearchEngine _diskSearch;
    private readonly QuickLaunchQueryGeneration _queryGeneration = new();
    private readonly QuickLaunchApplicationMatcher _applicationMatcher;
    private bool _candidateLimitReached;

    public LaunchWindow(
        LaunchWindowLocalization localization,
        QuickLaunchDiskSearchEngine? diskSearch = null)
        : this(
            localization,
            diskSearch,
            new QuickLaunchApplicationMatcher(null))
    {
    }

    internal LaunchWindow(
        LaunchWindowLocalization localization,
        QuickLaunchDiskSearchEngine? diskSearch,
        QuickLaunchApplicationMatcher applicationMatcher)
    {
        _localization = localization;
        _diskSearch = diskSearch ?? new QuickLaunchDiskSearchEngine();
        _applicationMatcher = applicationMatcher;
        InitializeComponent();
        Loaded += LaunchWindow_Loaded;
        Closed += (_, _) =>
        {
            _searchCancellation?.Cancel();
            _searchCancellation?.Dispose();
            _searchCancellation = null;
            _queryGeneration.Invalidate();
        };
        ApplyLocalization(localization);
    }

    internal static LaunchWindow Show(
        Action<SmartEntry?> onSelect,
        LaunchWindowLocalization localization,
        QuickLaunchApplicationMatcher applicationMatcher,
        string? initialQuery = null)
    {
        var window = new LaunchWindow(localization, null, applicationMatcher)
        {
            _onSelect = onSelect,
        };
        window.Show();
        window.ActivateSearch(initialQuery);
        return window;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var (_, workArea) = MonitorHelper.GetCursorPlacement(this);
        var placement = QuickLaunchWindowPlacement.Calculate(
            workArea,
            new Size(Width, Height));
        Left = placement.X;
        Top = placement.Y;
    }

    public void ActivateSearch(string? initialQuery = null)
    {
        if (!string.IsNullOrWhiteSpace(initialQuery))
            SearchBox.Text = initialQuery;
        if (!IsVisible)
            Show();
        Activate();
        SearchBox.Focus();
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    private static List<SmartEntry>? _cachedApps;
    private static readonly object AppCacheLock = new();

    private async void LaunchWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var applications = await Task.Run(GetApplications);
            _apps.AddRange(applications);
            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                await UpdateSearchAsync(SearchBox.Text.Trim());
        }
        catch (Exception)
        {
            // URL, calculation, and file search remain available if app discovery fails.
        }
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

    private async void SearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
        => await UpdateSearchAsync(SearchBox.Text.Trim());

    private async Task UpdateSearchAsync(string query)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var cancellationToken = _searchCancellation.Token;
        var generation = _queryGeneration.Begin();

        if (string.IsNullOrEmpty(query))
        {
            _currentResults = [];
            ResultsList.ItemsSource = null;
            ResultsList.Visibility = Visibility.Collapsed;
            _candidateLimitReached = false;
            HintText.Text = _localization.EmptyHint;
            return;
        }

        var immediateResults = new List<SmartEntry>();

        // 1. 数学表达式
        if (TryEvaluateMath(query, out var mathResult))
        {
            immediateResults.Add(new SmartEntry
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
            immediateResults.Add(new SmartEntry
            {
                Name = query,
                Path = url,
                Icon = "🌐",
                Category = "link",
            });
        }

        // 3. 应用搜索
        var appScores = await _applicationMatcher.ScoreAsync(
            _apps.Select(app => app.Name),
            query,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var appResults = _apps
            .Where(app => appScores.ContainsKey(app.Name))
            .OrderByDescending(app => appScores[app.Name])
            .ThenBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        immediateResults.AddRange(appResults);

        ApplySearchResults(immediateResults, candidateLimitReached: false);

        try
        {
            await Task.Delay(180, cancellationToken);
            var diskResult = await Task.Run(
                () => query.StartsWith(">") && query.Length > 2
                    ? _diskSearch.SearchContent(
                        query[1..].Trim(),
                        4,
                        cancellationToken)
                    : query.Length >= 2
                        ? _diskSearch.SearchFiles(
                            query,
                            3,
                            cancellationToken)
                        : new QuickLaunchDiskSearchResult([], 0, false),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_queryGeneration.IsCurrent(generation)
                || !string.Equals(
                    SearchBox.Text.Trim(),
                    query,
                    StringComparison.Ordinal))
                return;

            var results = immediateResults.Concat(diskResult.Entries).ToList();
            ApplySearchResults(results, diskResult.CandidateLimitReached);
        }
        catch (OperationCanceledException)
        {
            // A newer query superseded this disk search.
        }
    }

    private void ApplySearchResults(
        List<SmartEntry> results,
        bool candidateLimitReached)
    {
        _currentResults = results;
        _candidateLimitReached = candidateLimitReached;
        ApplyResultsProjection();
        ResultsList.ItemsSource = _currentResults;
        ResultsList.Visibility = results.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;

        if (results.Count > 0)
            ResultsList.SelectedIndex = 0;

        UpdateHint();
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

    public void ApplyLocalization(LaunchWindowLocalization localization)
    {
        _localization = localization;
        Title = localization.Title;
        System.Windows.Automation.AutomationProperties.SetName(
            this,
            localization.Title);
        System.Windows.Automation.AutomationProperties.SetName(
            SearchBox,
            localization.SearchAutomationName);
        NavigationHintText.Text = localization.NavigationHint;
        ApplyResultsProjection();
        UpdateHint();
    }

    private void UpdateHint()
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            HintText.Text = _localization.EmptyHint;
            return;
        }

        if (_candidateLimitReached)
        {
            HintText.Text = string.Format(
                _localization.LimitedResultCountFormat,
                _currentResults.Count);
            return;
        }

        HintText.Text = _currentResults.Count > 0
            ? string.Format(
                _localization.ResultCountFormat,
                _currentResults.Count)
            : _localization.NoMatches;
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
    }

    private void ResultsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (ResultsList.SelectedItem is SmartEntry entry)
                SelectEntry(entry);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && ResultsList.SelectedIndex <= 0)
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
            e.Handled = true;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is SmartEntry entry)
            SelectEntry(entry);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        CompleteSelection(null);
        Close();
        e.Handled = true;
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        _searchCancellation?.Cancel();
        CompleteSelection(null);
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
        CompleteSelection(entry);
        Close();
    }

    private void CompleteSelection(SmartEntry? entry)
    {
        var callback = Interlocked.Exchange(ref _onSelect, null);
        callback?.Invoke(entry);
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
