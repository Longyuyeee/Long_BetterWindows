using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LongBetterWindows.Host.Interaction;
using System.Windows.Input;
using System.Windows.Media;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;
using Microsoft.Win32;
using Serilog;

namespace LongBetterWindows.Host.Views
{
    public partial class ToolCenterControl : UserControl
    {
        internal event EventHandler? WorkflowReviewClosed;
        internal event Action<bool>? WorkflowLayoutChanged;
        internal event Action<WorkflowExecutionResultState>? WorkflowExecutionResultChanged;
        internal event EventHandler? WorkflowTerminalOutputsCleared;
        private bool _columnEnabled;
        private bool _contextMenuRegistered;
        private bool _sparsePackageInstalled;
        private bool _sparsePackageBusy;
        private bool _startupEnabled;
        private bool? _isNarrowLayout;
        private bool _languageSelectorReady;
        private UpdateService? _updateService;
        private UpdateCheckResult? _availableUpdate;
        private string? _downloadedUpdatePath;
        private bool _automaticUpdateCheckStarted;

        public ToolCenterControl()
        {
            InitializeComponent();
            InitializeLanguageSelector();
            SizeChanged += (_, _) => ApplyResponsiveLayout(ActualWidth);
            RefreshColumnStatus();
            RefreshContextMenuStatus();
            Loaded += async (_, _) =>
            {
                await RefreshSparsePackageStatusAsync();
                await StartAutomaticUpdateCheckAsync();
            };
            Unloaded += (_, _) =>
            {
                _updateService?.Dispose();
                _updateService = null;
            };
            RefreshStartupStatus();
            RefreshMouseGestureControls();
            ShowPage("overview");

            // 同步主题按钮状态
            var currentTheme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
            _isLightMode = currentTheme == Wpf.Ui.Appearance.ApplicationTheme.Light;

            // 首次运行显示欢迎横幅
            if (App.IsFirstRun())
                WelcomeBanner.Visibility = Visibility.Visible;

            // 动态更新关于信息
            UpdateAboutInfo();

            // 插件管理控件自行刷新列表；ToolCenter 只同步全局概览数据。
            HostProvider.Instance.PluginStore.PluginsChanged += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateAboutInfo();
                });
            };
        }

        private void RefreshMouseGestureControls()
        {
            var mode = ServicesInitializer.MouseGestures.Mode;
            foreach (var button in MouseGestureButtons.Children.OfType<Button>())
            {
                var active = string.Equals(
                    button.Tag?.ToString(), mode.ToString(), StringComparison.OrdinalIgnoreCase);
                button.SetResourceReference(
                    FrameworkElement.StyleProperty,
                    active ? "LongButton.Primary" : "LongButton");
            }
            MouseGestureStatusText.Text = mode switch
            {
                MouseGestureMode.MiddleButton => I18n("settings.gesture.status.middle"),
                MouseGestureMode.LongRightPress => string.Format(
                    I18n("settings.gesture.status.longRight"),
                    LongRightPressRecognizer.HoldMilliseconds),
                _ => I18n("settings.gesture.status.disabled"),
            };
        }

        private async void MouseGestureMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string value }
                || !Enum.TryParse<MouseGestureMode>(value, out var mode)) return;
            MouseGestureButtons.IsEnabled = false;
            try
            {
                await ServicesInitializer.MouseGestures.SetModeAsync(mode);
                RefreshMouseGestureControls();
            }
            finally { MouseGestureButtons.IsEnabled = true; }
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
            SystemIntegrationGrid.Columns = isNarrow ? 1 : 2;
            DeveloperGrid.Columns = isNarrow ? 1 : 2;

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

        private void UpdateAboutInfo()
        {
            var plugins = HostProvider.Instance.PluginStore.GetAll();
            var capCount = Engine.ManifestReader.KnownCapabilities.Count;
            AboutVersion.Text = string.Format(
                I18n("developer.about.version"),
                App.ProductVersion);
            AboutStats.Text = string.Format(
                I18n("developer.about.stats"),
                capCount,
                plugins.Count,
                3);
            OverviewPluginCount.Text = plugins.Count.ToString();
            OverviewCommandCount.Text = HostProvider.Instance.PluginStore.Commands.Count.ToString();
            OverviewCapabilityCount.Text = capCount.ToString();
        }

        private async void ClearSearchPreferences_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;
            button.IsEnabled = false;
            await ServicesInitializer.SearchPreferences.ClearAsync();
            await ServicesInitializer.SuperPanelGroups.ClearAsync();
            SearchPreferenceStatusText.Text =
                I18n("settings.searchPreferences.cleared");
            button.IsEnabled = true;
        }

        private async Task StartAutomaticUpdateCheckAsync()
        {
            if (_automaticUpdateCheckStarted) return;
            _automaticUpdateCheckStarted = true;
            await CheckForUpdatesAsync(silent: true);
        }

        private async void UpdateAction_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadedUpdatePath is not null
                && File.Exists(_downloadedUpdatePath))
            {
                OpenUpdatePackage(_downloadedUpdatePath);
                return;
            }
            if (_availableUpdate?.Package is { } package)
            {
                await DownloadUpdateAsync(package);
                return;
            }
            await CheckForUpdatesAsync(silent: false);
        }

        private async Task CheckForUpdatesAsync(bool silent)
        {
            UpdateActionButton.IsEnabled = false;
            if (!silent)
                UpdateStatusText.Text = I18n("settings.update.status.checking");
            try
            {
                _updateService ??= UpdateService.CreateDefault();
                var includePrereleases = App.ProductVersion.Contains(
                    '-',
                    StringComparison.Ordinal);
                var result = await _updateService.CheckAsync(includePrereleases);
                _availableUpdate = result.State == UpdateCheckState.Available
                    ? result
                    : null;
                _downloadedUpdatePath = null;
                if (_availableUpdate is not null)
                {
                    UpdateStatusText.Text = string.Format(
                        I18n("settings.update.status.available"),
                        _availableUpdate.AvailableVersion);
                    UpdateActionButton.Content = I18n("settings.update.action.download");
                    AutomationProperties.SetName(
                        UpdateActionButton,
                        I18n("settings.update.action.download"));
                }
                else if (!silent)
                {
                    UpdateStatusText.Text = string.Format(
                        I18n("settings.update.status.current"),
                        App.ProductVersion);
                }
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Automatic update check failed");
                if (!silent)
                    UpdateStatusText.Text = I18n("settings.update.status.failed");
            }
            finally
            {
                UpdateActionButton.IsEnabled = true;
            }
        }

        private async Task DownloadUpdateAsync(UpdatePackage package)
        {
            if (_updateService is null) return;
            UpdateActionButton.IsEnabled = false;
            UpdateStatusText.Text = I18n("settings.update.status.downloading");
            try
            {
                var updateDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LongBetterWindows",
                    "Updates",
                    _availableUpdate?.AvailableVersion ?? "latest");
                var packagePath = await _updateService.DownloadAsync(
                    package,
                    updateDirectory);
                _downloadedUpdatePath = packagePath;
                UpdateStatusText.Text = I18n("settings.update.status.downloaded");
                UpdateActionButton.Content = I18n("settings.update.action.open");
                AutomationProperties.SetName(
                    UpdateActionButton,
                    I18n("settings.update.action.open"));
                _availableUpdate = null;
                OpenUpdatePackage(packagePath);
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Verified update download failed");
                UpdateStatusText.Text = I18n("settings.update.status.failed");
            }
            finally
            {
                UpdateActionButton.IsEnabled = true;
            }
        }

        private static void OpenUpdatePackage(string packagePath)
        {
            Process.Start(new ProcessStartInfo(
                    "explorer.exe",
                    $"/select,\"{packagePath}\"")
            {
                UseShellExecute = true,
            });
        }

        #region Navigation

        private string _activePage = "overview";

        internal void OpenMarketForQuality() => ShowPage("market");
        internal void OpenDiagnosticsForQuality() => ShowPage("diagnostics");
        internal void OpenPluginsForQuality() => ShowPage("plugins");
        internal void OpenSystemForQuality() => ShowPage("system");
        internal void OpenSettingsForQuality() => ShowPage("settings");
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
                ShowPage(page);
        }

        private void ShowPage(string page)
        {
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
                PageTitle.Text = title;
                PageSubtitle.Text = subtitle;
                ContentScrollViewer.ScrollToTop();
                if (key == "plugins")
                    PluginManagementHost.Refresh();
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
                else if (key == "developer")
                {
                    UpdateAboutInfo();
                    RefreshDocLinks();
                }
                Helpers.AnimationHelper.FadeInElement(panel, durationMs: 160);
                _ = Dispatcher.BeginInvoke(
                    new Action(ContentScrollViewer.ScrollToTop),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
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
                ShowPage(pages[(current + direction + pages.Length) % pages.Length]);
            }
        }

        #endregion

        #region Shared Brush helpers (from Colors.xaml)

        private Brush GreenBrush => (Brush)FindResource("SuccessGreenBrush");
        private Brush GrayBrush => (Brush)FindResource("TextSecondaryBrush");
        private Brush BlueBrush => (Brush)FindResource("AccentBlueBrush");
        private Brush RedBrush => (Brush)FindResource("DangerRedBrush");

        #endregion

        private void DevTools_Click(object sender, RoutedEventArgs e)
        {
            PluginDevTools.Open(Window.GetWindow(this)!);
        }

        private void DesignSystemPreview_Click(object sender, RoutedEventArgs e)
        {
            var preview = new DesignSystemPreview
            {
                Owner = Window.GetWindow(this),
            };
            preview.Show();
        }

        private void OpenPalette_Click(object sender, RoutedEventArgs e)
            => CommandPaletteWindow.ShowPalette();

        private static bool _isLightMode;
        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            _isLightMode = !_isLightMode;

            // 使用 WPF-UI 主题管理器切换整个 Application
            var theme = _isLightMode
                ? Wpf.Ui.Appearance.ApplicationTheme.Light
                : Wpf.Ui.Appearance.ApplicationTheme.Dark;
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(theme);
            App.SaveThemeSetting(_isLightMode);
            App.UpdateThemeResources(_isLightMode);

            if (sender is Button btn)
                btn.Content = _isLightMode
                    ? I18n("action.darkMode")
                    : I18n("action.lightMode");
        }

        private void RefreshDocLinks()
        {
            DocLinksPanel.Children.Clear();

            var docsDir = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "docs"));

            if (!Directory.Exists(docsDir))
                docsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs");

            if (!Directory.Exists(docsDir))
            {
                DocLinksPanel.Children.Add(new TextBlock
                {
                    Text = I18n("developer.docs.unavailable"),
                    FontSize = 11,
                    Foreground = GrayBrush,
                });
                return;
            }

            var docFiles = Directory.GetFiles(docsDir, "*.md")
                .OrderBy(f => f)
                .ToList();
            if (docFiles.Count == 0)
            {
                DocLinksPanel.Children.Add(new TextBlock
                {
                    Text = I18n("developer.docs.empty"),
                    FontSize = 11,
                    Foreground = GrayBrush,
                });
                return;
            }

            foreach (var file in docFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file);

                var link = new TextBlock
                {
                    Text = name,
                    FontSize = 12,
                    Foreground = BlueBrush,
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 2, 0, 2),
                    Tag = file,
                };
                link.MouseLeftButtonDown += (_, _) =>
                {
                    var path = (string)link.Tag;
                    var content = File.ReadAllText(path);
                    DocViewer.ShowDoc(
                        Window.GetWindow(this)!,
                        Path.GetFileNameWithoutExtension(path),
                        content);
                };

                DocLinksPanel.Children.Add(link);
            }
        }

        private void StartupButton_Click(object sender, RoutedEventArgs e)
        {
            _startupEnabled = !_startupEnabled;
            ServicesInitializer.Startup.SetAutoStart(_startupEnabled);
            RefreshStartupStatus();
        }

        private void RefreshStartupStatus()
        {
            _startupEnabled = ServicesInitializer.Startup.IsAutoStartEnabled;
            StartupButton.Content = _startupEnabled
                ? I18n("action.disable")
                : I18n("action.enable");

            if (_startupEnabled)
            {
                StartupStatusText.Text = I18n("status.enabled");
                StartupStatusText.Foreground = GreenBrush;
            }
            else
            {
                StartupStatusText.Text = I18n("status.disabled");
                StartupStatusText.Foreground = GrayBrush;
            }
        }

        private async void ColumnButton_Click(object sender, RoutedEventArgs e)
        {
            ColumnButton.IsEnabled = false;
            ColumnStatusText.Text = I18n("status.processing");

            try
            {
                if (_columnEnabled)
                {
                    var result = await ServicesInitializer.ColumnInjection
                        .DisableCommentColumnAsync();
                    if (result.IsSuccess)
                    {
                        _columnEnabled = false;
                        ColumnStatusText.Text = I18n("status.disabled");
                        ColumnStatusText.Foreground = GrayBrush;
                    }
                    else
                    {
                        Log.Warning(
                            "Explorer note column disable failed ({ErrorCode}): {Error}",
                            result.ErrorCode,
                            result.ErrorMessage);
                        ColumnStatusText.Text = I18n(
                            "system.column.error.disable");
                        ColumnStatusText.Foreground = RedBrush;
                    }
                }
                else
                {
                    var result = await ServicesInitializer.ColumnInjection
                        .EnableCommentColumnAsync();
                    if (result.IsSuccess)
                    {
                        _columnEnabled = true;
                        ColumnStatusText.Text = I18n("status.enabled");
                        ColumnStatusText.Foreground = GreenBrush;
                    }
                    else
                    {
                        Log.Warning(
                            "Explorer note column enable failed ({ErrorCode}): {Error}",
                            result.ErrorCode,
                            result.ErrorMessage);
                        ColumnStatusText.Text = I18n(
                            "system.column.error.enable");
                        ColumnStatusText.Foreground = RedBrush;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Explorer note column operation failed");
                ColumnStatusText.Text = I18n(
                    "system.column.error.unexpected");
                ColumnStatusText.Foreground = RedBrush;
            }

            ColumnButton.Content = _columnEnabled
                ? I18n("action.remove")
                : I18n("action.enable");
            ColumnButton.IsEnabled = true;
        }

        private async void ContextMenuButton_Click(object sender, RoutedEventArgs e)
        {
            ContextMenuButton.IsEnabled = false;
            ContextMenuStatusText.Text = I18n("status.processing");

            try
            {
                if (_contextMenuRegistered)
                {
                    var result = await ServicesInitializer.ContextMenu.UnregisterAsync();
                    if (result.IsSuccess)
                    {
                        _contextMenuRegistered = false;
                        ContextMenuStatusText.Text = I18n(
                            "status.notRegistered");
                        ContextMenuStatusText.Foreground = GrayBrush;
                    }
                    else
                    {
                        Log.Warning(
                            "Legacy context menu unregister failed ({ErrorCode}): {Error}",
                            result.ErrorCode,
                            result.ErrorMessage);
                        ContextMenuStatusText.Text = I18n(
                            "system.legacy.error.unregister");
                        ContextMenuStatusText.Foreground = RedBrush;
                    }
                }
                else
                {
                    var result = await ServicesInitializer.ContextMenu.RegisterAsync();
                    if (result.IsSuccess)
                    {
                        _contextMenuRegistered = true;
                        ContextMenuStatusText.Text = I18n(
                            "status.registered");
                        ContextMenuStatusText.Foreground = GreenBrush;
                    }
                    else
                    {
                        Log.Warning(
                            "Legacy context menu register failed ({ErrorCode}): {Error}",
                            result.ErrorCode,
                            result.ErrorMessage);
                        ContextMenuStatusText.Text = I18n(
                            "system.legacy.error.register");
                        ContextMenuStatusText.Foreground = RedBrush;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Legacy context menu operation failed");
                ContextMenuStatusText.Text = I18n(
                    "system.legacy.error.unexpected");
                ContextMenuStatusText.Foreground = RedBrush;
            }

            ContextMenuButton.Content = _contextMenuRegistered
                ? I18n("action.remove")
                : I18n("action.register");
            ContextMenuButton.IsEnabled = true;
        }

        private async void SparsePackageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sparsePackageBusy) return;

            var dialog = new OpenFileDialog
            {
                Title = _sparsePackageInstalled
                    ? I18n("system.sparse.dialog.chooseUpgrade")
                    : I18n("system.sparse.dialog.chooseRegister"),
                Filter = I18n("system.sparse.dialog.filter"),
                CheckFileExists = true,
                Multiselect = false,
            };
            if (dialog.ShowDialog() != true) return;

            var action = _sparsePackageInstalled
                ? I18n("system.sparse.action.upgrade")
                : I18n("system.sparse.action.register");
            var answer = MessageBox.Show(
                string.Format(
                    I18n("system.sparse.confirm.registerOrUpgrade.message"),
                    action),
                string.Format(
                    I18n("system.sparse.confirm.registerOrUpgrade.title"),
                    action),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes) return;

            await RunSparsePackageOperationAsync(
                () => ServicesInitializer.SparsePackage.RegisterOrUpgradeAsync(
                    dialog.FileName));
        }

        private async void SparsePackageRemoveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_sparsePackageBusy || !_sparsePackageInstalled) return;
            var answer = MessageBox.Show(
                I18n("system.sparse.confirm.uninstall.message"),
                I18n("system.sparse.confirm.uninstall.title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            await RunSparsePackageOperationAsync(
                () => ServicesInitializer.SparsePackage.UnregisterAsync());
        }

        private async Task RunSparsePackageOperationAsync(
            Func<Task<SparsePackageOperationResult>> operation)
        {
            SetSparsePackageBusy(true, I18n("status.processing"));
            var result = await operation();
            if (result.State is { } state)
                ApplySparsePackageState(state);
            else
                await RefreshSparsePackageStatusAsync();

            if (!result.IsSuccess)
            {
                Log.Warning(
                    "Sparse package operation failed ({ErrorCode}): {Error}",
                    (int)result.ErrorCode,
                    result.Message);
                SparsePackageStatusText.Text = I18n(
                    SparsePackagePresentation.GetErrorResourceKey(
                        result.ErrorCode));
                SparsePackageStatusText.Foreground = RedBrush;
            }
            SetSparsePackageBusy(false);
        }

        private async Task RefreshSparsePackageStatusAsync()
        {
            if (_sparsePackageBusy) return;
            SetSparsePackageBusy(true, I18n("status.checking"));
            var result = await ServicesInitializer.SparsePackage.GetStatusAsync();
            if (result.IsSuccess && result.State is { } state)
                ApplySparsePackageState(state);
            else
            {
                _sparsePackageInstalled = result.State?.Installed ?? false;
                Log.Warning(
                    "Sparse package status failed ({ErrorCode}): {Error}",
                    (int)result.ErrorCode,
                    result.Message);
                SparsePackageStatusText.Text = I18n(
                    SparsePackagePresentation.GetErrorResourceKey(
                        result.ErrorCode));
                SparsePackageStatusText.Foreground = RedBrush;
            }
            SetSparsePackageBusy(false);
        }

        private void ApplySparsePackageState(SparsePackageState state)
        {
            _sparsePackageInstalled = state.Installed;
            if (state.Installed)
            {
                var locationState = string.IsNullOrWhiteSpace(state.ExternalLocation)
                    ? I18n("status.locationPending")
                    : I18n("status.locationVerified");
                SparsePackageStatusText.Text = string.Format(
                    I18n("status.sparseRegistered"),
                    state.Version ?? "—",
                    state.Architecture ?? "—",
                    locationState);
                SparsePackageStatusText.Foreground = GreenBrush;
            }
            else
            {
                SparsePackageStatusText.Text = I18n("status.notRegistered");
                SparsePackageStatusText.Foreground = GrayBrush;
            }
        }

        private void InitializeLanguageSelector()
        {
            var language = ServicesInitializer.I18n.CurrentLanguage;
            LanguageSelector.SelectedItem = LanguageSelector.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(
                    item.Tag?.ToString(),
                    language,
                    StringComparison.OrdinalIgnoreCase));
            _languageSelectorReady = true;
        }

        private void LanguageSelector_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!_languageSelectorReady ||
                LanguageSelector.SelectedItem is not ComboBoxItem
                {
                    Tag: string language,
                } ||
                string.Equals(
                    language,
                    ServicesInitializer.I18n.CurrentLanguage,
                    StringComparison.OrdinalIgnoreCase))
                return;

            ServicesInitializer.I18n.SetLanguage(language);
            ServicesInitializer.I18n.ApplyTo(Application.Current.Resources);
            ShowPage(_activePage);
            RefreshColumnStatus();
            RefreshContextMenuStatus();
            RefreshStartupStatus();
            _ = RefreshSparsePackageStatusAsync();
        }

        private void SetSparsePackageBusy(bool busy, string? status = null)
        {
            _sparsePackageBusy = busy;
            SparsePackageButton.IsEnabled = !busy;
            SparsePackageRemoveButton.IsEnabled = !busy;
            SparsePackageButton.Content = _sparsePackageInstalled
                ? I18n("action.chooseUpgrade")
                : I18n("action.chooseRegister");
            SparsePackageRemoveButton.Visibility = _sparsePackageInstalled
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (!string.IsNullOrWhiteSpace(status))
            {
                SparsePackageStatusText.Text = status;
                SparsePackageStatusText.Foreground = GrayBrush;
            }
        }

        private void RefreshColumnStatus()
        {
            _columnEnabled = ServicesInitializer.ColumnInjection.IsCommentColumnEnabled;
            ColumnButton.Content = _columnEnabled
                ? I18n("action.remove")
                : I18n("action.enable");

            if (_columnEnabled)
            {
                ColumnStatusText.Text = I18n("status.enabled");
                ColumnStatusText.Foreground = GreenBrush;
            }
            else
            {
                ColumnStatusText.Text = I18n("status.disabled");
                ColumnStatusText.Foreground = GrayBrush;
            }
        }

        private void RefreshContextMenuStatus()
        {
            _contextMenuRegistered = ServicesInitializer.ContextMenu.IsRegistered;
            ContextMenuButton.Content = _contextMenuRegistered
                ? I18n("action.remove")
                : I18n("action.register");

            if (_contextMenuRegistered)
            {
                ContextMenuStatusText.Text = I18n("status.registered");
                ContextMenuStatusText.Foreground = GreenBrush;
            }
            else
            {
                ContextMenuStatusText.Text = I18n("status.notRegistered");
                ContextMenuStatusText.Foreground = GrayBrush;
            }
        }

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);

    }
}
