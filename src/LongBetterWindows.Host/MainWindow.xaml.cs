using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using LongBetterWindows.Host.Automation;
using LongBetterWindows.Host.Helpers;
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
        private bool _workflowExecutionRejected;

        public MainWindow()
        {
            InitializeComponent();
            _tray = new TrayService(this);
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
                    "执行已拒绝",
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
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if ((Application.Current as App)?.ShowMarketForQualityRequested == true)
                ToolCenter.OpenMarketForQuality();
            if ((Application.Current as App)?.ShowDiagnosticsForQualityRequested == true)
                ToolCenter.OpenDiagnosticsForQuality();
            if ((Application.Current as App)?.ShowPluginsForQualityRequested == true)
                ToolCenter.OpenPluginsForQuality();

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
            if (EmbeddedPluginSurface.Visibility == Visibility.Visible)
                await CloseEmbeddedSurfaceAsync(notifyLifecycle: true);
            ToolCenter.Visibility = Visibility.Visible;
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
                WorkflowTerminalApprovalButton.Content = "允许终端输出";
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
            if (e.Key == Key.Escape && ToolCenter.CancelWorkflowReview())
            {
                e.Handled = true;
                return;
            }
            if (e.Key != Key.Escape
                || EmbeddedPluginSurface.Visibility != Visibility.Visible) return;
            await CloseEmbeddedSurfaceAsync(notifyLifecycle: true);
            e.Handled = true;
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
                ? "已允许终端输出 ✓"
                : "允许终端输出";
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
                WorkflowTerminalApprovalButton.Content = "允许终端输出";
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
