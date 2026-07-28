using System.Windows;
using System.Windows.Controls;
using LongBetterWindows.Host.Interaction;
using System.Windows.Input;
using System.Windows.Media.Effects;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views
{
    public partial class ToolCenterControl : UserControl
    {
        internal event EventHandler? WorkflowReviewClosed;
        internal event Action<bool>? WorkflowLayoutChanged;
        internal event Action<WorkflowExecutionResultState>? WorkflowExecutionResultChanged;
        internal event EventHandler? WorkflowTerminalOutputsCleared;
        internal event Action<string>? PageNavigationRequested;
        private bool? _isNarrowLayout;

        public ToolCenterControl()
        {
            InitializeComponent();
            if (App.ShowManagementCardShadowsForQuality)
            {
                var count = ApplyManagementCardShadowsForQuality();
                App.RecordManagementCardShadowCount(count);
                if (count == 0)
                    throw new InvalidOperationException(
                        "Quality management-card shadow baseline did not find any cards.");
            }
            SizeChanged += (_, _) => ApplyResponsiveLayout(ActualWidth);
            Unloaded += (_, _) =>
            {
                ReleasePluginManagementPage();
                if (DeveloperHost.Content is DeveloperPageControl developerPage)
                {
                    DeveloperHost.Content = null;
                    developerPage.Dispose();
                }
                if (SystemHost.Content is SystemIntegrationPageControl systemPage)
                {
                    SystemHost.Content = null;
                    systemPage.Dispose();
                }
                if (SettingsHost.Content is SettingsPageControl settingsPage)
                {
                    SettingsHost.Content = null;
                    settingsPage.LanguageApplied -= SettingsPage_LanguageApplied;
                    settingsPage.Dispose();
                }
            };
            ShowPage("overview");

            // 首次运行显示欢迎横幅
            if (App.IsFirstRun())
                WelcomeBanner.Visibility = Visibility.Visible;

            // 动态更新关于信息
            RefreshOverviewMetrics();

            // 插件管理控件自行刷新列表；ToolCenter 只同步全局概览数据。
            HostProvider.Instance.PluginStore.PluginsChanged += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    RefreshOverviewMetrics();
                });
            };
        }

        private void ApplyResponsiveLayout(double width)
        {
            var isNarrow = width < 860;
            if (_isNarrowLayout == isNarrow) return;
            _isNarrowLayout = isNarrow;
            NavigationColumn.Width = new GridLength(isNarrow ? 160 : 220);
            PageHeader.Margin = isNarrow
                ? new Thickness(18, 16, 18, 12)
                : new Thickness(32, 20, 32, 16);
            ContentBodyFrame.Padding = isNarrow
                ? new Thickness(18, 0, 18, 18)
                : new Thickness(32, 0, 32, 32);

            if (isNarrow)
            {
                OverviewPrimaryColumn.Width = new GridLength(1, GridUnitType.Star);
                OverviewGapColumn.Width = new GridLength(0);
                OverviewStatusColumn.Width = new GridLength(0);
                Grid.SetRow(OverviewStatusCard, 1);
                Grid.SetColumn(OverviewStatusCard, 0);
                OverviewStatusCard.Margin = new Thickness(0, 12, 0, 0);
                WelcomeTextColumn.Width = new GridLength(1, GridUnitType.Star);
                WelcomeActionColumn.Width = new GridLength(0);
                Grid.SetRow(WelcomeDismissButton, 1);
                Grid.SetColumn(WelcomeDismissButton, 0);
                WelcomeDismissButton.HorizontalAlignment = HorizontalAlignment.Left;
                WelcomeDismissButton.Margin = new Thickness(0, 12, 0, 0);
                OverviewLaunchTextColumn.Width = new GridLength(1, GridUnitType.Star);
                OverviewLaunchActionColumn.Width = new GridLength(0);
                Grid.SetRow(OverviewLaunchButton, 1);
                Grid.SetColumn(OverviewLaunchButton, 0);
                OverviewLaunchButton.HorizontalAlignment = HorizontalAlignment.Left;
                OverviewLaunchButton.Margin = new Thickness(0, 14, 0, 0);
            }
            else
            {
                OverviewPrimaryColumn.Width = new GridLength(1.3, GridUnitType.Star);
                OverviewGapColumn.Width = new GridLength(16);
                OverviewStatusColumn.Width = new GridLength(1, GridUnitType.Star);
                Grid.SetRow(OverviewStatusCard, 0);
                Grid.SetColumn(OverviewStatusCard, 2);
                OverviewStatusCard.Margin = new Thickness(0);
                WelcomeTextColumn.Width = new GridLength(1, GridUnitType.Star);
                WelcomeActionColumn.Width = GridLength.Auto;
                Grid.SetRow(WelcomeDismissButton, 0);
                Grid.SetColumn(WelcomeDismissButton, 1);
                WelcomeDismissButton.HorizontalAlignment = HorizontalAlignment.Stretch;
                WelcomeDismissButton.Margin = new Thickness(0);
                OverviewLaunchTextColumn.Width = new GridLength(1, GridUnitType.Star);
                OverviewLaunchActionColumn.Width = GridLength.Auto;
                Grid.SetRow(OverviewLaunchButton, 0);
                Grid.SetColumn(OverviewLaunchButton, 1);
                OverviewLaunchButton.HorizontalAlignment = HorizontalAlignment.Stretch;
                OverviewLaunchButton.Margin = new Thickness(0);
            }
        }

        private void RefreshOverviewMetrics()
        {
            var plugins = HostProvider.Instance.PluginStore.GetAll();
            var capCount = Engine.ManifestReader.KnownCapabilities.Count;
            OverviewPluginCount.Text = plugins.Count.ToString();
            OverviewCommandCount.Text = HostProvider.Instance.PluginStore.Commands.Count.ToString();
            OverviewCapabilityCount.Text = capCount.ToString();
        }

        #region Navigation

        private string _activePage = "overview";
        private readonly Dictionary<string, double> _pageScrollOffsets =
            new(StringComparer.Ordinal);
        private bool _pageInitialized;

        internal void OpenMarketForQuality() => ShowPage("market");
        internal void OpenDiagnosticsForQuality() => ShowPage("diagnostics");
        internal void OpenPluginsForQuality()
        {
            App.MarkPluginPageStage("plugin_page_navigation_begin");
            ShowPage("plugins");
        }
        internal PluginPageVisualMetrics GetPluginPageVisualMetricsForQuality()
            => PluginManagementHost.Content is PluginManagementControl plugins
                ? plugins.GetPerformanceMetricsForQuality()
                : default;
        internal void OpenDeveloperForQuality() => ShowPage("developer");
        internal WeakReference ReleasePluginsForQuality()
        {
            ShowPage("plugins");
            var reference = new WeakReference(PluginManagementHost.Content);
            ShowPage("overview");
            return reference;
        }

        private int ApplyManagementCardShadowsForQuality()
        {
            if (FindResource("Long.Shadow.Level1") is not Effect shadow
                || Resources["ManagementCard"] is not Style managementStyle)
            {
                return 0;
            }

            var count = 0;
            foreach (var border in EnumerateLogicalDescendants(this).OfType<Border>())
            {
                if (IsBasedOn(border.Style, managementStyle))
                {
                    border.Effect = shadow.CloneCurrentValue();
                    count++;
                }
            }
            return count;
        }

        private static bool IsBasedOn(Style? style, Style expected)
        {
            for (var current = style; current is not null; current = current.BasedOn)
            {
                if (ReferenceEquals(current, expected))
                    return true;
            }
            return false;
        }

        private static IEnumerable<DependencyObject> EnumerateLogicalDescendants(
            DependencyObject parent)
        {
            foreach (var item in LogicalTreeHelper.GetChildren(parent))
            {
                if (item is not DependencyObject child)
                    continue;
                yield return child;
                foreach (var descendant in EnumerateLogicalDescendants(child))
                    yield return descendant;
            }
        }
        internal void OpenSystemForQuality() => ShowPage("system");
        internal void OpenSettingsForQuality() => ShowPage("settings");
        internal bool OpenWorkspaceModule(WorkspaceModuleKey key)
        {
            var page = (key.Kind, key.ResourceId) switch
            {
                ("management", "root") => "overview",
                ("marketplace", "catalog") => "market",
                ("settings", "root") => "settings",
                ("diagnostics", "root") => "diagnostics",
                ("developer", "root") => "developer",
                ("management-page", "workflows") => "workflows",
                ("management-page", "plugins") => "plugins",
                ("management-page", "system") => "system",
                _ => null,
            };
            if (page is null)
                return false;
            if (key != new WorkspaceModuleKey("management", "root"))
                FilterManagementNavigation(string.Empty);
            ShowPage(page);
            return true;
        }

        internal Task ApplyWorkspaceSearchAsync(
            WorkspaceSearchRequest request)
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            switch (request.ModuleKey.Kind, request.ModuleKey.ResourceId)
            {
                case ("management", "root"):
                    FilterManagementNavigation(request.Query);
                    break;
                case ("marketplace", "catalog")
                    when MarketHost.Content is MarketplaceControl marketplace:
                    return marketplace.ApplyWorkspaceSearchAsync(
                        request.Query,
                        request.CancellationToken);
                case ("management-page", "plugins")
                    when PluginManagementHost.Content is PluginManagementControl plugins:
                    plugins.ApplyWorkspaceSearch(request.Query);
                    break;
            }
            return Task.CompletedTask;
        }

        internal bool HasDismissibleTransientLayer
            => MarketHost.Content is MarketplaceControl marketplace
                && marketplace.HasDismissibleTransientLayer;

        internal bool CanNavigateBackInModule
            => MarketHost.Content is MarketplaceControl marketplace
                && marketplace.CanNavigateBackInModule;

        internal bool DismissTransientLayer()
            => MarketHost.Content is MarketplaceControl marketplace
                && marketplace.DismissTransientLayer();

        internal bool NavigateBackInModule()
            => MarketHost.Content is MarketplaceControl marketplace
                && marketplace.NavigateBackInModule();

        private void FilterManagementNavigation(string query)
        {
            var normalized = query.Trim();
            RadioButton[] destinations =
            [
                NavOverview,
                NavWorkflows,
                NavPlugins,
                NavMarket,
                NavSystem,
                NavDiagnostics,
                NavDeveloper,
                NavSettings,
            ];
            foreach (var destination in destinations)
            {
                var label = destination.Content?.ToString() ?? string.Empty;
                var target = destination.Tag?.ToString() ?? string.Empty;
                destination.Visibility = string.IsNullOrEmpty(normalized)
                    || label.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                    || target.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
        }

        internal bool OpenPluginSettings(string pluginId)
        {
            ShowPage("plugins");
            return PluginManagementHost.Content is PluginManagementControl plugins
                && plugins.OpenPluginSettings(pluginId);
        }

        internal void ShowWelcomeForQuality()
        {
            ShowPage("overview");
            WelcomeBanner.Visibility = Visibility.Visible;
        }

        internal void ShowMarketplaceListForQuality()
        {
            if (MarketHost.Content is MarketplaceControl marketplace)
                marketplace.ShowListForQuality();
        }

        internal async Task<string?> OpenWorkflowReviewAsync(
            string workflowId,
            string? expectedStateFingerprint = null,
            CancellationToken cancellationToken = default)
        {
            ShowPage("workflows");
            if (WorkflowEditorHost.Content is not WorkflowEditorControl editor)
                return I18n("workflow.error.editorUnavailable");
            return await editor.OpenExecutionReviewAsync(
                workflowId,
                expectedStateFingerprint,
                cancellationToken);
        }

        internal async Task<string?> OpenWorkflowEditorAsync(
            string workflowId,
            CancellationToken cancellationToken = default)
        {
            ShowPage("workflows");
            if (WorkflowEditorHost.Content is not WorkflowEditorControl editor)
                return I18n("workflow.error.editorUnavailable");
            return await editor.OpenEditorAsync(workflowId, cancellationToken);
        }

        internal bool CancelWorkflowReview()
            => WorkflowEditorHost.Content is WorkflowEditorControl editor
                && editor.CancelExecutionReview();

        internal bool DuplicateCurrentWorkflow()
            => WorkflowEditorHost.Content is WorkflowEditorControl editor
                && editor.DuplicateCurrentWorkflow();

        internal bool IsWorkflowLayoutCompact
            => WorkflowEditorHost.Content is WorkflowEditorControl editor
                && editor.IsCompactLayout;

        internal double WorkflowLayoutWidth
            => WorkflowEditorHost.ActualWidth;

        internal bool ClearWorkflowTerminalOutputs()
            => WorkflowEditorHost.Content is WorkflowEditorControl editor
                && editor.ClearTerminalOutputs();

        internal WorkflowTerminalOutput? GetWorkflowTerminalOutputForQuality()
            => WorkflowEditorHost.Content is WorkflowEditorControl editor
                ? editor.GetTerminalOutputForQuality()
                : null;

        internal bool ToggleWorkflowTerminalOutputApproval()
            => WorkflowEditorHost.Content is WorkflowEditorControl editor
                && editor.ToggleTerminalOutputApproval();

        internal Task<bool> ConfirmWorkflowReviewAsync()
            => WorkflowEditorHost.Content is WorkflowEditorControl editor
                ? editor.ConfirmExecutionReviewAsync()
                : Task.FromResult(false);

        private void Navigation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton { Tag: string page })
                RequestPageNavigation(page);
        }

        private void RequestPageNavigation(string page)
        {
            if (_pageInitialized
                && string.Equals(page, _activePage, StringComparison.Ordinal))
            {
                return;
            }

            if (PageNavigationRequested is not null)
                PageNavigationRequested(page);
            else
                ShowPage(page);
        }

        private void ShowPage(string page, bool forceRefresh = false)
        {
            if (_pageInitialized
                && !forceRefresh
                && string.Equals(page, _activePage, StringComparison.Ordinal))
            {
                return;
            }
            if (_pageInitialized)
                _pageScrollOffsets[_activePage] = ContentScrollViewer.VerticalOffset;

            if (!string.Equals(page, "plugins", StringComparison.Ordinal))
                ReleasePluginManagementPage();

            (string Key, FrameworkElement Panel, RadioButton Navigation, string Title, string Subtitle)[] pages =
            {
                ("overview", PanelOverview, NavOverview, I18n("page.overview.title"), I18n("page.overview.subtitle")),
                ("workflows", PanelWorkflows, NavWorkflows, I18n("page.workflows.title"), I18n("page.workflows.subtitle")),
                ("plugins", PanelPlugins, NavPlugins, I18n("page.plugins.title"), I18n("page.plugins.subtitle")),
                ("market", PanelMarket, NavMarket, I18n("page.market.title"), I18n("page.market.subtitle")),
                ("system", PanelSystem, NavSystem, I18n("page.system.title"), I18n("page.system.subtitle")),
                ("diagnostics", PanelDiagnostics, NavDiagnostics, I18n("page.diagnostics.title"), I18n("page.diagnostics.subtitle")),
                ("developer", PanelDev, NavDeveloper, I18n("page.developer.title"), I18n("page.developer.subtitle")),
                ("settings", PanelSettings, NavSettings, I18n("page.settings.title"), I18n("page.settings.subtitle")),
            };

            foreach (var (key, panel, navigation, title, subtitle) in pages)
            {
                var selected = key == page;
                panel.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
                navigation.IsChecked = selected;
                if (!selected) continue;

                _activePage = key;
                _pageInitialized = true;
                PageTitle.Text = title;
                PageSubtitle.Text = subtitle;
                if (key == "plugins")
                {
                    PluginManagementHost.Content ??= new PluginManagementControl();
                    ((PluginManagementControl)PluginManagementHost.Content).Refresh();
                }
                else if (key == "workflows" && WorkflowEditorHost.Content == null)
                {
                    var editor = new WorkflowEditorControl();
                    editor.ExecutionReviewClosed += (_, _) =>
                        WorkflowReviewClosed?.Invoke(this, EventArgs.Empty);
                    editor.ResponsiveLayoutChanged += compact =>
                        WorkflowLayoutChanged?.Invoke(compact);
                    editor.ExecutionResultChanged += result =>
                        WorkflowExecutionResultChanged?.Invoke(result);
                    editor.TerminalOutputsCleared += (_, _) =>
                        WorkflowTerminalOutputsCleared?.Invoke(this, EventArgs.Empty);
                    WorkflowEditorHost.Content = editor;
                }
                else if (key == "market" && MarketHost.Content == null)
                    MarketHost.Content = new MarketplaceControl();
                else if (key == "diagnostics" && DiagnosticsHost.Content == null)
                    DiagnosticsHost.Content = new PerformancePanel();
                else if (key == "system")
                    SystemHost.Content ??= new SystemIntegrationPageControl();
                else if (key == "developer")
                {
                    DeveloperHost.Content ??= new DeveloperPageControl();
                }
                else if (key == "settings")
                {
                    if (SettingsHost.Content is null)
                    {
                        var settingsPage = new SettingsPageControl();
                        settingsPage.LanguageApplied += SettingsPage_LanguageApplied;
                        SettingsHost.Content = settingsPage;
                    }
                }
                Helpers.AnimationHelper.FadeInElement(panel, durationMs: 160);
                var offset = _pageScrollOffsets.TryGetValue(key, out var savedOffset)
                    ? savedOffset
                    : 0;
                _ = Dispatcher.BeginInvoke(
                    new Action(() => ContentScrollViewer.ScrollToVerticalOffset(offset)),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        }

        private void ReleasePluginManagementPage()
        {
            if (PluginManagementHost.Content is not PluginManagementControl plugins)
                return;

            PluginManagementHost.Content = null;
            plugins.Dispose();
        }

        private void WelcomeDismiss_Click(object sender, RoutedEventArgs e)
        {
            WelcomeBanner.Visibility = Visibility.Collapsed;
            App.MarkOnboarded();
        }

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                var pages = new[] { "overview", "workflows", "plugins", "market", "system", "diagnostics", "developer", "settings" };
                var current = Array.IndexOf(pages, _activePage);
                var direction = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
                RequestPageNavigation(
                    pages[(current + direction + pages.Length) % pages.Length]);
            }
        }

        #endregion

        private void OpenPalette_Click(object sender, RoutedEventArgs e)
            => CommandPaletteWindow.ShowPalette();

        private void SettingsPage_LanguageApplied(object? sender, EventArgs e)
            => ShowPage(_activePage, forceRefresh: true);

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);

    }
}
