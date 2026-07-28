using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using LongBetterWindows.Host.Automation;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Helpers;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;
using LongBetterWindows.Host.Views;
using Serilog;

namespace LongBetterWindows.Host
{
    public partial class MainWindow : Window
    {
        private TrayService? _tray;
        private Func<Task>? _embeddedCloseRequested;
        private Action? _embeddedDetachRequested;
        private string? _activeWorkflowReviewId;
        private bool _workflowTerminalOutputApproved;
        private int _workflowTerminalOutputLength;
        private bool _workflowTerminalOutputCleared;
        private int _qualityWorkflowPluginUpgradeStatus;
        private int _qualityTerminalOutputExportStatus;
        private int _qualityWorkflowDuplicateStatus;
        private bool _workflowExecutionRejected;
        private readonly WorkspaceFocusBookmarkStore _workspaceFocusBookmarks = new();
        private ToolCenterControl ToolCenter => WorkspaceShell.ToolCenter;

        public MainWindow()
        {
            App.MarkStartupStage("main_window_constructor_begin");
            InitializeComponent();
            EventHandler? firstLayout = null;
            firstLayout = (_, _) =>
            {
                LayoutUpdated -= firstLayout;
                App.MarkStartupStage("main_window_first_layout");
            };
            LayoutUpdated += firstLayout;
            _tray = new TrayService(this);
            WorkspaceShell.Bind(
                ServicesInitializer.Workspace,
                GetWorkspaceModuleTitle,
                GetWorkspaceSearchPlaceholder,
                request => ToolCenter.ApplyWorkspaceSearchAsync(request));
            WorkspaceShell.ModuleActivationRequested +=
                WorkspaceShell_ModuleActivationRequested;
            WorkspaceShell.ModuleCloseRequested +=
                WorkspaceShell_ModuleCloseRequested;
            WorkspaceShell.ScopedSearchFailed += exception =>
                Log.Error(exception, "Workspace scoped search failed");
            ToolCenter.AddHandler(
                Keyboard.GotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(
                    ToolCenter_GotKeyboardFocus),
                handledEventsToo: true);
            ServicesInitializer.I18n.LanguageChanged += I18n_LanguageChanged;
            Closed += (_, _) =>
                ServicesInitializer.I18n.LanguageChanged -= I18n_LanguageChanged;
            ToolCenter.PageNavigationRequested +=
                ToolCenter_PageNavigationRequested;
            ToolCenter.WorkflowReviewClosed += (_, _) =>
            {
                _activeWorkflowReviewId = null;
                _workflowTerminalOutputApproved = false;
                AutomationProperties.SetItemStatus(this, string.Empty);
                SetWorkflowReviewChrome(false);
            };
            ToolCenter.WorkflowLayoutChanged += compact =>
            {
                if (_activeWorkflowReviewId is not null)
                    SetWorkflowLayoutAutomationStatus(compact, ToolCenter.WorkflowLayoutWidth);
            };
            ToolCenter.WorkflowExecutionResultChanged += result =>
            {
                _workflowTerminalOutputLength = result.TerminalOutputLength;
                _workflowTerminalOutputCleared = false;
                _workflowExecutionRejected = string.Equals(
                    result.Title,
                    I18n("workflow.execution.status.rejected"),
                    StringComparison.Ordinal);
                AutomationProperties.SetItemStatus(
                    this,
                    $"workflow-result:{result.Title};terminal-length:{result.TerminalOutputLength};bounded-scroll:{(result.TerminalOutputLength > 1024 ? "true" : "false")}");
                WorkflowTerminalClearButton.Visibility = result.HasTerminalOutputs
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            };
            ToolCenter.WorkflowTerminalOutputsCleared += (_, _) =>
            {
                _workflowTerminalOutputLength = 0;
                _workflowTerminalOutputCleared = true;
                AutomationProperties.SetItemStatus(this, "workflow-result:terminal-cleared");
                WorkflowTerminalClearButton.Visibility = Visibility.Collapsed;
            };
            App.MarkStartupStage("main_window_constructor_end");
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            App.MarkStartupStage("main_window_first_render");
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            App.MarkStartupStage("main_window_loaded");
            if ((Application.Current as App)?.ShowMarketForQualityRequested == true)
                await OpenLegacyWorkspacePageAsync("market");
            if ((Application.Current as App)?.ShowDiagnosticsForQualityRequested == true)
                await OpenLegacyWorkspacePageAsync("diagnostics");
            if ((Application.Current as App)?.ShowPluginsForQualityRequested == true)
                await OpenLegacyWorkspacePageAsync("plugins");
            if ((Application.Current as App)?.ShowSystemForQualityRequested == true)
                await OpenLegacyWorkspacePageAsync("system");
            if ((Application.Current as App)?.ShowSettingsForQualityRequested == true)
                await OpenLegacyWorkspacePageAsync("settings");
            if ((Application.Current as App)?.ShowDeveloperForQualityRequested == true)
                await OpenLegacyWorkspacePageAsync("developer");
            if ((Application.Current as App)?.ShowWelcomeForQualityRequested == true)
            {
                await OpenLegacyWorkspacePageAsync("overview");
                ToolCenter.ShowWelcomeForQuality();
            }

            if ((Application.Current as App)?.ShowDesignSystemPreviewRequested == true)
            {
                try
                {
                    var preview = new DesignSystemPreview { Owner = this };
                    preview.Show();
                    preview.Activate();
                    Log.Information("Long Design System 预览窗口已显示");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Long Design System 预览窗口显示失败");
                }
            }
        }

        internal PluginPageVisualMetrics GetPluginPageVisualMetricsForQuality()
            => ToolCenter.GetPluginPageVisualMetricsForQuality();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void OpenPalette_Click(object sender, RoutedEventArgs e)
            => CommandPaletteWindow.ShowPalette();

        private void OpenSuperPanel_Click(object sender, RoutedEventArgs e)
            => SuperPanelWindow.ShowPanel();

        internal async Task<string?> OpenWorkflowReviewAsync(
            string workflowId,
            string? expectedStateFingerprint = null,
            CancellationToken cancellationToken = default)
        {
            var resolution = await ResolveWorkspaceModuleAsync(
                $"workflow:{workflowId}",
                cancellationToken);
            if (!resolution.IsSuccess || resolution.Module is null)
                return I18nOrFallback(
                    "workflow.error.invalidId",
                    "工作流不存在或已失效。");
            var navigationError = await OpenWorkspaceModuleAsync(
                resolution.Module,
                cancellationToken);
            if (navigationError is not null)
                return navigationError;

            var error = await ToolCenter.OpenWorkflowReviewAsync(
                workflowId,
                expectedStateFingerprint,
                cancellationToken);
            if (error is null)
            {
                await Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Loaded,
                    cancellationToken);
                _activeWorkflowReviewId = workflowId;
                _workflowTerminalOutputApproved = false;
                _workflowTerminalOutputLength = 0;
                _workflowTerminalOutputCleared = false;
                _workflowExecutionRejected = false;
                WorkflowTerminalClearButton.Visibility = Visibility.Collapsed;
                WorkflowTerminalApprovalButton.Content =
                    I18n("workflow.review.allowTerminalOutput");
                SetWorkflowLayoutAutomationStatus(
                    ToolCenter.IsWorkflowLayoutCompact,
                    ToolCenter.WorkflowLayoutWidth);
                SetWorkflowReviewChrome(true);
                Activate();
                WorkflowReviewCancelButton.Focus();
                Keyboard.Focus(WorkflowReviewCancelButton);
            }
            return error;
        }

        internal async Task<string?> OpenWorkflowEditorAsync(
            string workflowId,
            CancellationToken cancellationToken = default)
        {
            var resolution = await ResolveWorkspaceModuleAsync(
                $"workflow:{workflowId}",
                cancellationToken);
            return resolution.IsSuccess && resolution.Module is not null
                ? await OpenWorkspaceModuleAsync(
                    resolution.Module,
                    cancellationToken)
                : I18nOrFallback(
                    "workflow.error.invalidId",
                    "工作流不存在或已失效。");
        }

        internal async Task<string?> OpenWorkspaceModuleAsync(
            WorkspaceModuleDescriptor module,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(module);
            cancellationToken.ThrowIfCancellationRequested();
            if (EmbeddedPluginSurface.Visibility == Visibility.Visible)
                await CloseEmbeddedSurfaceAsync(notifyLifecycle: true);

            var previousActive = ServicesInitializer.Workspace.State.ActiveModuleKey;
            var navigation = ServicesInitializer.Workspace.Open(module);

            string? error;
            try
            {
                error = await ShowWorkspaceModuleViewAsync(
                    module,
                    cancellationToken);
            }
            catch
            {
                RollbackWorkspaceNavigation(navigation, module.Key, previousActive);
                throw;
            }
            if (error is null)
            {
                RestoreWorkspaceFocus(module.Key);
                return null;
            }

            RollbackWorkspaceNavigation(navigation, module.Key, previousActive);
            return error;
        }

        private async Task<string?> ShowWorkspaceModuleViewAsync(
            WorkspaceModuleDescriptor module,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (EmbeddedPluginSurface.Visibility == Visibility.Visible)
                await CloseEmbeddedSurfaceAsync(notifyLifecycle: true);
            ToolCenter.Visibility = Visibility.Visible;

            return module.Key.Kind switch
            {
                "workflow" => await ToolCenter.OpenWorkflowEditorAsync(
                    module.Key.ResourceId,
                    cancellationToken),
                "plugin-settings" => ToolCenter.OpenPluginSettings(
                    module.Key.ResourceId)
                        ? null
                        : I18nOrFallback(
                            "plugins.settingsUnavailable",
                            "插件设置当前不可用。"),
                _ => ToolCenter.OpenWorkspaceModule(module.Key)
                    ? null
                    : I18nOrFallback(
                        "search.error.workspaceResourceUnsupported",
                        "该资源不支持工作区模块。"),
            };
        }

        private async Task OpenLegacyWorkspacePageAsync(string page)
        {
            if (!WorkspaceLegacyModuleCatalog.TryCreate(
                page,
                key => ServicesInitializer.I18n.T(key),
                out var module)
                || module is null)
            {
                Log.Warning("Unknown legacy workspace page requested: {Page}", page);
                return;
            }

            var error = await OpenWorkspaceModuleAsync(
                module,
                CancellationToken.None);
            if (error is not null)
                Log.Warning("Workspace page {Page} could not open: {Error}", page, error);
        }

        private static Task<WorkspaceModuleResolution> ResolveWorkspaceModuleAsync(
            string target,
            CancellationToken cancellationToken)
        {
            if (!WorkspaceModuleAddress.TryParse(target, out var address))
            {
                return Task.FromResult(
                    new WorkspaceModuleResolution(
                        default,
                        null,
                        WorkspaceModuleResolutionError.InvalidAddress));
            }
            return ServicesInitializer.WorkspaceModules.ResolveAsync(
                address,
                cancellationToken);
        }

        private async void WorkspaceShell_ModuleActivationRequested(
            WorkspaceModuleKey key)
        {
            try
            {
                var module = ServicesInitializer.Workspace.State.Modules
                    .FirstOrDefault(candidate => candidate.Key == key);
                if (module is null)
                    return;

                var error = await OpenWorkspaceModuleAsync(
                    module,
                    CancellationToken.None);
                if (error is not null)
                {
                    Log.Warning(
                        "Workspace module {Module} could not activate: {Error}",
                        key,
                        error);
                }
            }
            catch (Exception exception)
            {
                Log.Error(
                    exception,
                    "Workspace module {Module} activation failed",
                    key);
            }
        }

        private async void WorkspaceShell_ModuleCloseRequested(
            WorkspaceModuleKey key)
        {
            try
            {
                await CloseWorkspaceModuleAsync(key);
            }
            catch (Exception exception)
            {
                Log.Error(
                    exception,
                    "Workspace module {Module} close failed",
                    key);
            }
        }

        private async Task CloseWorkspaceModuleAsync(WorkspaceModuleKey key)
        {
            var before = ServicesInitializer.Workspace.State;
            var wasActive = before.ActiveModuleKey == key;
            var result = ServicesInitializer.Workspace.Close(key);
            if (!result.Changed)
                return;

            WorkspaceShell.RemoveModuleSearch(key);
            _workspaceFocusBookmarks.Remove(key);
            if (wasActive)
            {
                var error = await ShowWorkspaceModuleViewAsync(
                    result.State.ActiveModule,
                    CancellationToken.None);
                if (error is not null)
                {
                    Log.Warning(
                        "Workspace fallback module {Module} could not display: {Error}",
                        result.State.ActiveModuleKey,
                        error);
                }
                RestoreWorkspaceFocus(result.State.ActiveModuleKey);
            }
        }

        private async void ToolCenter_PageNavigationRequested(string page)
        {
            try
            {
                await OpenLegacyWorkspacePageAsync(page);
            }
            catch (Exception exception)
            {
                Log.Error(
                    exception,
                    "Legacy workspace page {Page} navigation failed",
                    page);
            }
        }

        private static void RollbackWorkspaceNavigation(
            WorkspaceNavigationResult navigation,
            WorkspaceModuleKey moduleKey,
            WorkspaceModuleKey previousActive)
        {
            if (navigation.Kind == WorkspaceNavigationChangeKind.Opened)
                ServicesInitializer.Workspace.Close(moduleKey);
            else if (navigation.Kind == WorkspaceNavigationChangeKind.Activated)
                ServicesInitializer.Workspace.Activate(previousActive);
        }

        private void I18n_LanguageChanged(string language)
            => WorkspaceShell.Refresh();

        private void ToolCenter_GotKeyboardFocus(
            object sender,
            KeyboardFocusChangedEventArgs e)
        {
            if (e.NewFocus is not IInputElement element)
                return;
            _workspaceFocusBookmarks.Remember(
                ServicesInitializer.Workspace.State.ActiveModuleKey,
                element);
        }

        private void RestoreWorkspaceFocus(WorkspaceModuleKey key)
        {
            _ = Dispatcher.BeginInvoke(
                () =>
                {
                    if (!_workspaceFocusBookmarks.Restore(key))
                        WorkspaceShell.FocusActiveModule();
                },
                DispatcherPriority.Input);
        }

        private static string GetWorkspaceModuleTitle(
            WorkspaceModuleDescriptor module)
        {
            var resourceKey = (module.Key.Kind, module.Key.ResourceId) switch
            {
                ("management", "root") => "page.overview.title",
                ("management-page", "workflows") => "page.workflows.title",
                ("management-page", "plugins") => "page.plugins.title",
                ("management-page", "system") => "page.system.title",
                ("marketplace", "catalog") => "page.market.title",
                ("settings", "root") => "page.settings.title",
                ("diagnostics", "root") => "page.diagnostics.title",
                ("developer", "root") => "page.developer.title",
                _ => null,
            };
            if (resourceKey is not null)
                return I18n(resourceKey);
            if (module.Key.Kind == "plugin-settings")
            {
                var plugin = HostProvider.Instance.PluginStore.Get(
                    module.Key.ResourceId);
                if (plugin is not null)
                    return $"{plugin.DisplayName} - {I18n("plugins.settings")}";
            }
            return module.Title;
        }

        private static string GetWorkspaceSearchPlaceholder(
            WorkspaceModuleKey key)
        {
            var scope = WorkspaceSearchScopeCatalog.Resolve(key);
            return scope is null
                ? string.Empty
                : I18n(scope.PlaceholderResourceKey);
        }

        private void SetWorkflowLayoutAutomationStatus(
            bool compact,
            double width,
            bool? terminalApproved = null)
        {
            if (_activeWorkflowReviewId is null) return;
            if (terminalApproved.HasValue)
                _workflowTerminalOutputApproved = terminalApproved.Value;
            AutomationProperties.SetItemStatus(
                this,
                $"workflow-review:{_activeWorkflowReviewId};layout:{(compact ? "compact" : "wide")};width:{Math.Round(width)}"
                    + $";terminal-approved:{_workflowTerminalOutputApproved.ToString().ToLowerInvariant()}");
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private async void Close_Click(object sender, RoutedEventArgs e)
        {
            if (EmbeddedPluginSurface.Visibility == Visibility.Visible)
                await CloseEmbeddedSurfaceAsync(notifyLifecycle: true);
            Close();
        }

        internal bool IsHostingEmbedded(FrameworkElement content)
            => EmbeddedPluginSurface.Visibility == Visibility.Visible
               && ReferenceEquals(EmbeddedPluginContent.Content, content);

        internal void ShowMarketplaceListForQuality()
            => ToolCenter.ShowMarketplaceListForQuality();

        internal WeakReference ReleasePluginManagementForQuality()
            => ToolCenter.ReleasePluginsForQuality();

        internal void ShowEmbeddedPlugin(
            string title,
            FrameworkElement content,
            Func<Task> closeRequested,
            Action detachRequested)
        {
            if (EmbeddedPluginContent.Content is FrameworkElement existing
                && !ReferenceEquals(existing, content))
            {
                EmbeddedPluginContent.Content = null;
                _ = _embeddedCloseRequested?.Invoke();
            }
            EmbeddedPluginTitle.Text = title;
            EmbeddedPluginContent.Content = content;
            _embeddedCloseRequested = closeRequested;
            _embeddedDetachRequested = detachRequested;
            ToolCenter.Visibility = Visibility.Collapsed;
            EmbeddedPluginSurface.Visibility = Visibility.Visible;
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        internal void CloseEmbeddedPlugin(FrameworkElement content)
        {
            if (!ReferenceEquals(EmbeddedPluginContent.Content, content)) return;
            EmbeddedPluginContent.Content = null;
            EmbeddedPluginSurface.Visibility = Visibility.Collapsed;
            ToolCenter.Visibility = Visibility.Visible;
            _embeddedCloseRequested = null;
            _embeddedDetachRequested = null;
        }

        private async Task CloseEmbeddedSurfaceAsync(bool notifyLifecycle)
        {
            var callback = _embeddedCloseRequested;
            EmbeddedPluginContent.Content = null;
            EmbeddedPluginSurface.Visibility = Visibility.Collapsed;
            ToolCenter.Visibility = Visibility.Visible;
            _embeddedCloseRequested = null;
            _embeddedDetachRequested = null;
            if (notifyLifecycle && callback is not null)
                await callback();
        }

        private async void EmbeddedBack_Click(object sender, RoutedEventArgs e)
            => await CloseEmbeddedSurfaceAsync(notifyLifecycle: true);

        private async void EmbeddedClose_Click(object sender, RoutedEventArgs e)
            => await CloseEmbeddedSurfaceAsync(notifyLifecycle: true);

        private void DetachEmbedded_Click(object sender, RoutedEventArgs e)
        {
            var callback = _embeddedDetachRequested;
            EmbeddedPluginContent.Content = null;
            EmbeddedPluginSurface.Visibility = Visibility.Collapsed;
            ToolCenter.Visibility = Visibility.Visible;
            _embeddedCloseRequested = null;
            _embeddedDetachRequested = null;
            callback?.Invoke();
        }

        private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var modifiers = Keyboard.Modifiers;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.K && modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = WorkspaceShell.FocusScopedSearch();
                return;
            }
            if (_activeWorkflowReviewId is not null
                && key == Key.T
                && modifiers.HasFlag(ModifierKeys.Control)
                && modifiers.HasFlag(ModifierKeys.Shift))
            {
                ToggleWorkflowTerminalOutputApproval();
                e.Handled = true;
                return;
            }
            if (_activeWorkflowReviewId is not null
                && key == Key.Enter
                && modifiers.HasFlag(ModifierKeys.Control)
                && modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = await ToolCenter.ConfirmWorkflowReviewAsync();
                return;
            }
            if (key == Key.Delete
                && modifiers.HasFlag(ModifierKeys.Control)
                && modifiers.HasFlag(ModifierKeys.Shift)
                && WorkflowTerminalClearButton.Visibility == Visibility.Visible)
            {
                e.Handled = ToolCenter.ClearWorkflowTerminalOutputs();
                return;
            }
            if (key != Key.Escape)
                return;

            var state = ServicesInitializer.Workspace.State;
            var workspaceIsVisible =
                EmbeddedPluginSurface.Visibility != Visibility.Visible;
            var action = WorkspaceEscapeRouter.Route(
                new WorkspaceEscapeContext(
                    HasTransientLayer:
                        workspaceIsVisible
                        && (_activeWorkflowReviewId is not null
                            || ToolCenter.HasDismissibleTransientLayer),
                    HasScopedSearchQuery:
                        workspaceIsVisible
                        && WorkspaceShell.HasScopedSearchQuery,
                    CanNavigateBackInModule:
                        workspaceIsVisible
                        && ToolCenter.CanNavigateBackInModule,
                    CanNavigateBackInWorkspace:
                        EmbeddedPluginSurface.Visibility == Visibility.Visible,
                    CanCloseActiveModule: state.ActiveModule.CanClose));
            switch (action)
            {
                case WorkspaceEscapeAction.DismissTransientLayer:
                    e.Handled = ToolCenter.CancelWorkflowReview()
                        || ToolCenter.DismissTransientLayer();
                    break;
                case WorkspaceEscapeAction.ClearScopedSearch:
                    e.Handled = WorkspaceShell.ClearScopedSearch();
                    break;
                case WorkspaceEscapeAction.NavigateBackInModule:
                    e.Handled = ToolCenter.NavigateBackInModule();
                    break;
                case WorkspaceEscapeAction.NavigateBackInWorkspace:
                    await CloseEmbeddedSurfaceAsync(notifyLifecycle: true);
                    e.Handled = true;
                    break;
                case WorkspaceEscapeAction.CloseActiveModule:
                    await CloseWorkspaceModuleAsync(state.ActiveModuleKey);
                    e.Handled = true;
                    break;
            }
        }

        private void CancelWorkflowReview_Click(object sender, RoutedEventArgs e)
            => CancelWorkflowReview();

        private void CancelWorkflowReview()
        {
            AutomationProperties.SetItemStatus(this, string.Empty);
            SetWorkflowReviewChrome(false);
            ToolCenter.CancelWorkflowReview();
        }

        private async void WorkflowTerminalApprovalButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeWorkflowReviewId is null) return;
            await Task.Delay(100);
            ToggleWorkflowTerminalOutputApproval();
        }

        private void ToggleWorkflowTerminalOutputApproval()
        {
            var approved = ToolCenter.ToggleWorkflowTerminalOutputApproval();
            _workflowTerminalOutputApproved = approved;
            WorkflowTerminalApprovalButton.Content = approved
                ? I18n("workflow.review.terminalOutputAllowed")
                : I18n("workflow.review.allowTerminalOutput");
            SetWorkflowLayoutAutomationStatus(
                ToolCenter.IsWorkflowLayoutCompact,
                ToolCenter.WorkflowLayoutWidth,
                approved);
        }

        private async void ConfirmWorkflowReview_Click(object sender, RoutedEventArgs e)
            => await ConfirmWorkflowReviewAsync();

        private async Task ConfirmWorkflowReviewAsync()
        {
            await Task.Delay(100);
            await ToolCenter.ConfirmWorkflowReviewAsync();
        }

        private void SetWorkflowReviewChrome(bool visible)
        {
            var reviewVisibility = visible ? Visibility.Visible : Visibility.Collapsed;
            WorkflowReviewCancelButton.Visibility = reviewVisibility;
            WorkflowTerminalApprovalButton.Visibility = reviewVisibility;
            WorkflowReviewConfirmButton.Visibility = reviewVisibility;
            OpenSuperPanelButton.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            OpenPaletteButton.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            if (!visible)
                WorkflowTerminalApprovalButton.Content =
                    I18n("workflow.review.allowTerminalOutput");
        }

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);

        private static string I18nOrFallback(string key, string fallback)
        {
            var value = I18n(key);
            return string.IsNullOrWhiteSpace(value) || value == key
                ? fallback
                : value;
        }

        private void ClearWorkflowTerminalOutputs_Click(object sender, RoutedEventArgs e)
            => ToolCenter.ClearWorkflowTerminalOutputs();

        private void QueueWorkflowAutomationAction(QualityWorkflowAction action)
            => Dispatcher.BeginInvoke(new Action(async () =>
            {
                switch (action)
                {
                    case QualityWorkflowAction.CancelReview
                        when WorkflowReviewCancelButton.IsVisible:
                        CancelWorkflowReview();
                        break;
                    case QualityWorkflowAction.ApproveTerminalOutput
                        when WorkflowTerminalApprovalButton.IsVisible:
                        ToggleWorkflowTerminalOutputApproval();
                        break;
                    case QualityWorkflowAction.ConfirmReview
                        when WorkflowReviewConfirmButton.IsVisible:
                        await ConfirmWorkflowReviewAsync();
                        break;
                    case QualityWorkflowAction.ClearTerminalOutput
                        when WorkflowTerminalClearButton.IsVisible:
                        ToolCenter.ClearWorkflowTerminalOutputs();
                        break;
                    case QualityWorkflowAction.UpgradePluginPackage:
                        await UpgradeQualityWorkflowPluginAsync();
                        break;
                    case QualityWorkflowAction.RunTerminalOutputExportMatrix:
                        await RunQualityTerminalOutputExportMatrixAsync();
                        break;
                    case QualityWorkflowAction.DuplicateDraft:
                        if (WorkflowReviewCancelButton.IsVisible)
                            CancelWorkflowReview();
                        _qualityWorkflowDuplicateStatus =
                            ToolCenter.DuplicateCurrentWorkflow() ? 1 : -1;
                        break;
                }
            }));

        private async Task UpgradeQualityWorkflowPluginAsync()
        {
            if (_qualityWorkflowPluginUpgradeStatus != 0) return;
            var app = Application.Current as App;
            var packagePath = app?.QualityWorkflowUpgradePackagePath;
            var installer = App.PackageInstaller;
            if (string.IsNullOrWhiteSpace(packagePath) || installer is null)
            {
                _qualityWorkflowPluginUpgradeStatus = -1;
                return;
            }

            try
            {
                var result = await installer.InstallAsync(packagePath);
                _qualityWorkflowPluginUpgradeStatus = result.IsSuccess ? 1 : -1;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Quality workflow plugin upgrade failed");
                _qualityWorkflowPluginUpgradeStatus = -1;
            }
        }

        private async Task RunQualityTerminalOutputExportMatrixAsync()
        {
            if (_qualityTerminalOutputExportStatus != 0) return;
            var app = Application.Current as App;
            var output = ToolCenter.GetWorkflowTerminalOutputForQuality();
            if (output is null
                || string.IsNullOrWhiteSpace(app?.QualityTerminalExportDirectory)
                || string.IsNullOrWhiteSpace(app.QualityWorkflowsDirectory))
            {
                _qualityTerminalOutputExportStatus = -1;
                return;
            }

            _qualityTerminalOutputExportStatus = 2;
            try
            {
                var passed = await QualityTerminalOutputExportMatrix.RunAsync(
                    output,
                    app.QualityTerminalExportDirectory,
                    Path.Combine(app.QualityWorkflowsDirectory, ".reports"));
                _qualityTerminalOutputExportStatus = passed ? 1 : -1;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Quality terminal output export matrix failed");
                _qualityTerminalOutputExportStatus = -1;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            App.MarkStartupStage("main_window_source_initialized");

            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WorkflowAutomationWndProc);
            ServicesInitializer.HotKey.Initialize(hwnd);
            ServicesInitializer.MouseGestures.Start();
            _ = RegisterCommandPaletteHotkeyAsync();
            (Application.Current as App)?.StartPluginRuntime();
        }

        private IntPtr WorkflowAutomationWndProc(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message != QualityWorkflowAutomation.MessageId
                || (Application.Current as App)?.QualityWorkflowAutomationEnabled != true
                || !Enum.IsDefined(typeof(QualityWorkflowAction), wParam.ToInt32()))
            {
                return IntPtr.Zero;
            }

            handled = true;
            var action = (QualityWorkflowAction)wParam.ToInt32();
            if (action == QualityWorkflowAction.QueryReviewReady)
                return _activeWorkflowReviewId is null ? IntPtr.Zero : new IntPtr(1);
            if (action == QualityWorkflowAction.QueryTerminalOutputLength)
                return new IntPtr(_workflowTerminalOutputLength);
            if (action == QualityWorkflowAction.QueryTerminalOutputCleared)
                return _workflowTerminalOutputCleared ? new IntPtr(1) : IntPtr.Zero;
            if (action == QualityWorkflowAction.QueryPluginUpgradeStatus)
                return new IntPtr(_qualityWorkflowPluginUpgradeStatus);
            if (action == QualityWorkflowAction.QueryExecutionRejected)
                return _workflowExecutionRejected ? new IntPtr(1) : IntPtr.Zero;
            if (action == QualityWorkflowAction.QueryTerminalOutputExportStatus)
                return new IntPtr(_qualityTerminalOutputExportStatus);
            if (action == QualityWorkflowAction.QueryDuplicateStatus)
                return new IntPtr(_qualityWorkflowDuplicateStatus);

            QueueWorkflowAutomationAction(action);
            return new IntPtr(1);
        }

        private async Task RegisterCommandPaletteHotkeyAsync()
        {
            const string preferredHotkey = "Alt+Space";
            const string fallbackHotkey = "Ctrl+Alt+Space";

            var result = await ServicesInitializer.HotKey.RegisterAsync(
                preferredHotkey,
                "builtin.command-palette",
                CommandPaletteWindow.ShowPalette);

            if (result.IsSuccess)
            {
                Log.Information("Command Palette 全局热键已注册: {Hotkey}", preferredHotkey);
                return;
            }

            Log.Warning(
                "Command Palette 首选热键 {Hotkey} 注册失败: {Error}，尝试备用热键 {Fallback}",
                preferredHotkey,
                result.ErrorMessage,
                fallbackHotkey);

            var fallback = await ServicesInitializer.HotKey.RegisterAsync(
                fallbackHotkey,
                "builtin.command-palette",
                CommandPaletteWindow.ShowPalette);

            if (fallback.IsSuccess)
                Log.Information("Command Palette 备用全局热键已注册: {Hotkey}", fallbackHotkey);
            else
                Log.Error("Command Palette 全局热键均注册失败: {Error}", fallback.ErrorMessage);
        }
    }
}
