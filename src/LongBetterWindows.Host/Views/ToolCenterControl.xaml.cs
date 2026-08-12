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
        internal event Action<WorkspaceManagementPage>? PageNavigationRequested;
        internal event Action<string>? PluginSettingsNavigationRequested;
        internal event Action<string>? PluginRunRequested;
        internal event Action<string>? PluginToggleRequested;
        private bool? _isNarrowLayout;
        private readonly Dictionary<string, PluginSettingsModuleControl>
            _pluginSettingsModules = new(StringComparer.OrdinalIgnoreCase);

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
                ReleasePluginSettingsModules();
                if (WidgetDashboardHost.Content is WidgetDashboardControl widgetDashboard)
                {
                    WidgetDashboardHost.Content = null;
                    widgetDashboard.Dispose();
                }
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
            ShowManagementPage(WorkspaceManagementPage.Overview);

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
            PageHeader.Margin = isNarrow
                ? new Thickness(18, 16, 18, 12)
                : new Thickness(32, 20, 32, 16);
            ContentBodyFrame.Padding = isNarrow
                ? new Thickness(18, 0, 18, 18)
                : new Thickness(32, 0, 32, 32);
            ManagementDestinationGrid.Columns = isNarrow ? 2 : 4;
            ManagementUtilityGrid.Columns = isNarrow ? 2 : 4;

            if (isNarrow)
            {
                WelcomeTextColumn.Width = new GridLength(1, GridUnitType.Star);
                WelcomeActionColumn.Width = new GridLength(0);
                Grid.SetRow(WelcomeDismissButton, 1);
                Grid.SetColumn(WelcomeDismissButton, 0);
                WelcomeDismissButton.HorizontalAlignment = HorizontalAlignment.Left;
                WelcomeDismissButton.Margin = new Thickness(0, 12, 0, 0);
            }
            else
            {
                WelcomeTextColumn.Width = new GridLength(1, GridUnitType.Star);
                WelcomeActionColumn.Width = GridLength.Auto;
                Grid.SetRow(WelcomeDismissButton, 0);
                Grid.SetColumn(WelcomeDismissButton, 1);
                WelcomeDismissButton.HorizontalAlignment = HorizontalAlignment.Stretch;
                WelcomeDismissButton.Margin = new Thickness(0);
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

        private WorkspaceManagementPage _activePage =
            WorkspaceManagementPage.Overview;
        private string? _activePluginSettingsId;
        private string _activeScrollKey =
            WorkspaceManagementPage.Overview.ToString();
        private readonly Dictionary<string, double> _pageScrollOffsets =
            new(StringComparer.Ordinal);
        private bool _pageInitialized;

        internal void OpenMarketForQuality()
            => ShowManagementPage(WorkspaceManagementPage.Market);
        internal void OpenDiagnosticsForQuality()
            => ShowManagementPage(WorkspaceManagementPage.Diagnostics);
        internal void OpenPluginsForQuality()
        {
            App.MarkPluginPageStage("plugin_page_navigation_begin");
            ShowManagementPage(WorkspaceManagementPage.Plugins);
        }
        internal PluginPageVisualMetrics GetPluginPageVisualMetricsForQuality()
            => PluginManagementHost.Content is PluginManagementControl plugins
                ? plugins.GetPerformanceMetricsForQuality()
                : default;
        internal void OpenPluginsForReleaseQuality()
            => ShowManagementPage(
                WorkspaceManagementPage.Plugins,
                forceRefresh: true);
        internal void OpenDeveloperForQuality()
            => ShowManagementPage(WorkspaceManagementPage.Developer);
        internal WeakReference ReleasePluginsForQuality()
        {
            if (PluginManagementHost.Content is not PluginManagementControl plugins)
            {
                throw new InvalidOperationException(
                    "Plugin management release probe requires an active page.");
            }
            var reference = new WeakReference(plugins);
            ShowManagementPage(WorkspaceManagementPage.Overview);
            ReleasePluginManagementPage();
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
        internal void OpenSystemForQuality()
            => ShowManagementPage(WorkspaceManagementPage.System);
        internal void OpenSettingsForQuality()
            => ShowManagementPage(WorkspaceManagementPage.Settings);
        internal bool OpenWorkspaceModule(
            WorkspaceModuleKey key,
            string? navigationTarget = null)
        {
            if (!WorkspaceManagementModuleCatalog.TryResolvePage(key, out var page))
                return false;
            if (key != new WorkspaceModuleKey("management", "root"))
                FilterManagementNavigation(string.Empty);
            ShowManagementPage(page);
            if (page == WorkspaceManagementPage.Settings
                && navigationTarget is not null
                && SettingsHost.Content is SettingsPageControl settingsPage)
            {
                settingsPage.NavigateToCategory(navigationTarget);
            }
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

        internal bool ExecuteQualityPrimaryAction()
            => MarketHost.Content is MarketplaceControl marketplace
                && marketplace.OpenSelectedDetailForQuality();

        private void FilterManagementNavigation(string query)
        {
            var normalized = query.Trim();
            (Button Destination, WorkspaceManagementPage Page)[] destinations =
            [
                (DestinationPlugins, WorkspaceManagementPage.Plugins),
                (DestinationMarket, WorkspaceManagementPage.Market),
                (DestinationWorkflows, WorkspaceManagementPage.Workflows),
                (DestinationWidgets, WorkspaceManagementPage.Widgets),
                (DestinationSystem, WorkspaceManagementPage.System),
                (DestinationDiagnostics, WorkspaceManagementPage.Diagnostics),
                (DestinationDeveloper, WorkspaceManagementPage.Developer),
                (DestinationSettings, WorkspaceManagementPage.Settings),
            ];
            var hasMatch = false;
            var hasPrimaryMatch = false;
            var hasSecondaryMatch = false;
            foreach (var (destination, page) in destinations)
            {
                var label = ManagementNavigationLabel(page);
                var isMatch = string.IsNullOrEmpty(normalized)
                    || label.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                    || page.ToString().Contains(
                        normalized,
                        StringComparison.OrdinalIgnoreCase);
                destination.Visibility = isMatch
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                hasMatch |= isMatch;
                if (page is WorkspaceManagementPage.Plugins
                    or WorkspaceManagementPage.Market
                    or WorkspaceManagementPage.Workflows
                    or WorkspaceManagementPage.Widgets)
                {
                    hasPrimaryMatch |= isMatch;
                }
                else
                {
                    hasSecondaryMatch |= isMatch;
                }
            }
            var hasQuery = !string.IsNullOrEmpty(normalized);
            OverviewSummaryCard.Visibility = hasQuery
                ? Visibility.Collapsed
                : Visibility.Visible;
            ManagementPrimaryTitle.Visibility = hasPrimaryMatch
                ? Visibility.Visible
                : Visibility.Collapsed;
            ManagementSecondaryTitle.Visibility = hasSecondaryMatch
                ? Visibility.Visible
                : Visibility.Collapsed;
            ManagementNoResultsText.Visibility = hasMatch
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private static string ManagementNavigationLabel(WorkspaceManagementPage page)
            => I18n(page switch
            {
                WorkspaceManagementPage.Workflows => "nav.workflows",
                WorkspaceManagementPage.Plugins => "nav.plugins",
                WorkspaceManagementPage.Widgets => "nav.widgets",
                WorkspaceManagementPage.Market => "nav.market",
                WorkspaceManagementPage.System => "nav.system",
                WorkspaceManagementPage.Diagnostics => "nav.diagnostics",
                WorkspaceManagementPage.Developer => "nav.developer",
                WorkspaceManagementPage.Settings => "nav.settings",
                _ => "nav.overview",
            });

        internal bool OpenPluginSettingsModule(string pluginId)
        {
            var entry = HostProvider.Instance.PluginStore.Get(pluginId);
            if (entry is null)
                return false;

            RememberCurrentScrollOffset();
            CollapseAllContentPanels();
            var module = GetOrCreatePluginSettingsModule(pluginId);
            PluginSettingsModuleHost.Content = module;
            PanelPluginSettings.Visibility = Visibility.Visible;
            _activePluginSettingsId = pluginId;
            _activeScrollKey = $"plugin-settings:{pluginId}";
            _pageInitialized = true;
            PageTitle.Text = entry.DisplayName;
            PageSubtitle.Text = string.Format(
                I18n("plugins.module.subtitle"),
                entry.Manifest.Version);
            module.Refresh();
            Helpers.AnimationHelper.FadeInElement(
                PanelPluginSettings,
                durationMs: 160);
            _ = Dispatcher.BeginInvoke(
                new Action(() =>
                    ContentScrollViewer.ScrollToVerticalOffset(
                        _pageScrollOffsets.TryGetValue(
                            _activeScrollKey,
                            out var offset)
                                ? offset
                                : 0)),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
            return true;
        }

        internal void RemovePluginSettingsModule(string pluginId)
        {
            if (!_pluginSettingsModules.Remove(pluginId, out var module))
                return;
            if (ReferenceEquals(PluginSettingsModuleHost.Content, module))
                PluginSettingsModuleHost.Content = null;
            module.Dispose();
            _pageScrollOffsets.Remove($"plugin-settings:{pluginId}");
        }

        internal void RefreshPluginSettingsModule(string pluginId)
        {
            if (_pluginSettingsModules.TryGetValue(pluginId, out var module))
                module.Refresh();
        }

        internal void ApplyLanguage()
        {
            foreach (var module in _pluginSettingsModules.Values)
                module.ApplyLanguage();
            if (_activePluginSettingsId is not null)
            {
                var pluginId = _activePluginSettingsId;
                if (HostProvider.Instance.PluginStore.Get(pluginId) is { } entry)
                {
                    PageTitle.Text = entry.DisplayName;
                    PageSubtitle.Text = string.Format(
                        I18n("plugins.module.subtitle"),
                        entry.Manifest.Version);
                }
                return;
            }
            ShowManagementPage(_activePage, forceRefresh: true);
        }

        internal void ShowWelcomeForQuality()
        {
            ShowManagementPage(WorkspaceManagementPage.Overview);
            WelcomeBanner.Visibility = Visibility.Visible;
        }

        internal void ShowMarketplaceListForQuality()
        {
            if (MarketHost.Content is MarketplaceControl marketplace)
                marketplace.ShowListForQuality();
        }

        internal bool ShowMarketplaceDetailForQuality()
            => MarketHost.Content is MarketplaceControl marketplace
                && marketplace.ShowFirstDetailForQuality();

        internal bool ShowMarketplaceUpdateReviewForQuality()
            => MarketHost.Content is MarketplaceControl marketplace
                && marketplace.ShowUpdateReviewForQuality();

        internal async Task<string?> OpenWorkflowReviewAsync(
            string workflowId,
            string? expectedStateFingerprint = null,
            CancellationToken cancellationToken = default)
        {
            ShowManagementPage(WorkspaceManagementPage.Workflows);
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
            ShowManagementPage(WorkspaceManagementPage.Workflows);
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

        private void ManagementDestination_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string pageName }
                && Enum.TryParse<WorkspaceManagementPage>(
                    pageName,
                    ignoreCase: true,
                    out var page))
            {
                RequestPageNavigation(page);
            }
        }

        private void RequestPageNavigation(WorkspaceManagementPage page)
        {
            if (_pageInitialized
                && _activePluginSettingsId is null
                && page == _activePage)
            {
                return;
            }

            if (PageNavigationRequested is not null)
                PageNavigationRequested(page);
            else
                ShowManagementPage(page);
        }

        private void ShowManagementPage(
            WorkspaceManagementPage page,
            bool forceRefresh = false)
        {
            if (_pageInitialized
                && !forceRefresh
                && _activePluginSettingsId is null
                && page == _activePage)
            {
                return;
            }
            RememberCurrentScrollOffset();
            PanelPluginSettings.Visibility = Visibility.Collapsed;
            PluginSettingsModuleHost.Content = null;
            _activePluginSettingsId = null;

            (WorkspaceManagementPage Key, FrameworkElement Panel, string Title, string Subtitle)[] pages =
            {
                (WorkspaceManagementPage.Overview, PanelOverview, I18n("page.overview.title"), I18n("page.overview.subtitle")),
                (WorkspaceManagementPage.Workflows, PanelWorkflows, I18n("page.workflows.title"), I18n("page.workflows.subtitle")),
                (WorkspaceManagementPage.Plugins, PanelPlugins, I18n("page.plugins.title"), I18n("page.plugins.subtitle")),
                (WorkspaceManagementPage.Widgets, PanelWidgets, I18n("page.widgets.title"), I18n("page.widgets.subtitle")),
                (WorkspaceManagementPage.Market, PanelMarket, I18n("page.market.title"), I18n("page.market.subtitle")),
                (WorkspaceManagementPage.System, PanelSystem, I18n("page.system.title"), I18n("page.system.subtitle")),
                (WorkspaceManagementPage.Diagnostics, PanelDiagnostics, I18n("page.diagnostics.title"), I18n("page.diagnostics.subtitle")),
                (WorkspaceManagementPage.Developer, PanelDev, I18n("page.developer.title"), I18n("page.developer.subtitle")),
                (WorkspaceManagementPage.Settings, PanelSettings, I18n("page.settings.title"), I18n("page.settings.subtitle")),
            };

            foreach (var (key, panel, title, subtitle) in pages)
            {
                var selected = key == page;
                panel.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
                if (!selected) continue;

                _activePage = key;
                _activeScrollKey = key.ToString();
                _pageInitialized = true;
                PageTitle.Text = title;
                PageSubtitle.Text = subtitle;
                if (key == WorkspaceManagementPage.Plugins)
                {
                    if (PluginManagementHost.Content is not PluginManagementControl plugins)
                    {
                        plugins = new PluginManagementControl();
                        plugins.PluginSettingsRequested +=
                            pluginId => PluginSettingsNavigationRequested?.Invoke(pluginId);
                        PluginManagementHost.Content = plugins;
                    }
                    plugins.Refresh();
                }
                else if (key == WorkspaceManagementPage.Widgets)
                {
                    WidgetDashboardHost.Content ??= new WidgetDashboardControl();
                }
                else if (key == WorkspaceManagementPage.Workflows
                    && WorkflowEditorHost.Content == null)
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
                else if (key == WorkspaceManagementPage.Market
                    && MarketHost.Content == null)
                    MarketHost.Content = new MarketplaceControl();
                else if (key == WorkspaceManagementPage.Diagnostics
                    && DiagnosticsHost.Content == null)
                    DiagnosticsHost.Content = new PerformancePanel();
                else if (key == WorkspaceManagementPage.System)
                    SystemHost.Content ??= new SystemIntegrationPageControl();
                else if (key == WorkspaceManagementPage.Developer)
                {
                    DeveloperHost.Content ??= new DeveloperPageControl();
                }
                else if (key == WorkspaceManagementPage.Settings)
                {
                    if (SettingsHost.Content is null)
                    {
                        var settingsPage = new SettingsPageControl();
                        settingsPage.LanguageApplied += SettingsPage_LanguageApplied;
                        SettingsHost.Content = settingsPage;
                    }
                }
                Helpers.AnimationHelper.FadeInElement(panel, durationMs: 160);
                var offset = _pageScrollOffsets.TryGetValue(
                    _activeScrollKey,
                    out var savedOffset)
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

        private PluginSettingsModuleControl GetOrCreatePluginSettingsModule(
            string pluginId)
        {
            if (_pluginSettingsModules.TryGetValue(pluginId, out var existing))
                return existing;
            var module = new PluginSettingsModuleControl(pluginId);
            module.PluginRunRequested +=
                id => PluginRunRequested?.Invoke(id);
            module.PluginToggleRequested +=
                id => PluginToggleRequested?.Invoke(id);
            module.PluginUnavailable += RemovePluginSettingsModule;
            module.StateChanged += state =>
            {
                if (string.Equals(
                    _activePluginSettingsId,
                    state.Id,
                    StringComparison.OrdinalIgnoreCase))
                {
                    PageTitle.Text = state.Name;
                    PageSubtitle.Text = string.Format(
                        I18n("plugins.module.subtitle"),
                        state.Version);
                }
            };
            _pluginSettingsModules[pluginId] = module;
            return module;
        }

        private void ReleasePluginSettingsModules()
        {
            PluginSettingsModuleHost.Content = null;
            foreach (var module in _pluginSettingsModules.Values)
                module.Dispose();
            _pluginSettingsModules.Clear();
        }

        private void RememberCurrentScrollOffset()
        {
            if (_pageInitialized)
                _pageScrollOffsets[_activeScrollKey] =
                    ContentScrollViewer.VerticalOffset;
        }

        private void CollapseAllContentPanels()
        {
            PanelOverview.Visibility = Visibility.Collapsed;
            PanelWorkflows.Visibility = Visibility.Collapsed;
            PanelPlugins.Visibility = Visibility.Collapsed;
            PanelWidgets.Visibility = Visibility.Collapsed;
            PanelPluginSettings.Visibility = Visibility.Collapsed;
            PanelMarket.Visibility = Visibility.Collapsed;
            PanelSystem.Visibility = Visibility.Collapsed;
            PanelDiagnostics.Visibility = Visibility.Collapsed;
            PanelDev.Visibility = Visibility.Collapsed;
            PanelSettings.Visibility = Visibility.Collapsed;
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
                var pages = Enum.GetValues<WorkspaceManagementPage>();
                var current = Array.IndexOf(pages, _activePage);
                if (_activePluginSettingsId is not null)
                    current = Array.IndexOf(
                        pages,
                        WorkspaceManagementPage.Plugins);
                var direction = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
                RequestPageNavigation(
                    pages[(current + direction + pages.Length) % pages.Length]);
            }
        }

        #endregion

        private void OpenPalette_Click(object sender, RoutedEventArgs e)
            => CommandPaletteWindow.ShowPalette();

        private void SettingsPage_LanguageApplied(object? sender, EventArgs e)
            => ShowManagementPage(_activePage, forceRefresh: true);

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);

    }
}
