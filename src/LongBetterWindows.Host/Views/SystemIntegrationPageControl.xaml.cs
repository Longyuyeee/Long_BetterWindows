using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LongBetterWindows.Host.Helpers;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;
using Microsoft.Win32;
using Serilog;

namespace LongBetterWindows.Host.Views
{
    public partial class SystemIntegrationPageControl : UserControl, IDisposable
    {
        private bool _columnEnabled;
        private bool _contextMenuRegistered;
        private bool _sparsePackageInstalled;
        private bool _sparsePackageBusy;
        private bool _startupEnabled;
        private bool _disposed;

        public SystemIntegrationPageControl()
        {
            InitializeComponent();
            SizeChanged += OnSizeChanged;
            Loaded += OnLoaded;
            ServicesInitializer.I18n.LanguageChanged += OnLanguageChanged;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SizeChanged -= OnSizeChanged;
            Loaded -= OnLoaded;
            ServicesInitializer.I18n.LanguageChanged -= OnLanguageChanged;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            RefreshSynchronousStatus();
            await RefreshSparsePackageStatusAsync();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
            => ApplyResponsiveLayout(e.NewSize.Width);

        private void ApplyResponsiveLayout(double width)
        {
            var compact = width < 860;
            SystemGapColumn.Width = new GridLength(compact ? 0 : 12);
            SystemSecondaryColumn.Width = compact
                ? new GridLength(0)
                : new GridLength(1, GridUnitType.Star);

            Position(SparseCard, compact ? 1 : 0, compact ? 0 : 2);
            Position(ContextMenuCard, compact ? 2 : 1, 0);
            Position(StartupCard, compact ? 3 : 1, compact ? 0 : 2);
            Position(HotkeyCard, compact ? 4 : 2, 0);
        }

        private static void Position(FrameworkElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
        }

        private void OnLanguageChanged(string language)
        {
            if (_disposed) return;
            _ = Dispatcher.BeginInvoke(() =>
            {
                RefreshSynchronousStatus();
                _ = RefreshSparsePackageStatusAsync();
            });
        }

        private void RefreshSynchronousStatus()
        {
            if (_disposed) return;
            RefreshColumnStatus();
            RefreshContextMenuStatus();
            RefreshStartupStatus();
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
            SetStatus(
                StartupStatusText,
                _startupEnabled ? "status.enabled" : "status.disabled",
                _startupEnabled ? GreenBrush : GrayBrush);
        }

        private async void ColumnButton_Click(object sender, RoutedEventArgs e)
        {
            ColumnButton.IsEnabled = false;
            ColumnStatusText.Text = I18n("status.processing");
            try
            {
                var result = _columnEnabled
                    ? await ServicesInitializer.ColumnInjection.DisableCommentColumnAsync()
                    : await ServicesInitializer.ColumnInjection.EnableCommentColumnAsync();
                if (_disposed) return;
                if (result.IsSuccess)
                {
                    _columnEnabled = !_columnEnabled;
                    SetStatus(
                        ColumnStatusText,
                        _columnEnabled ? "status.enabled" : "status.disabled",
                        _columnEnabled ? GreenBrush : GrayBrush);
                }
                else
                {
                    Log.Warning(
                        "Explorer note column operation failed ({ErrorCode}): {Error}",
                        result.ErrorCode,
                        result.ErrorMessage);
                    SetStatus(
                        ColumnStatusText,
                        _columnEnabled
                            ? "system.column.error.disable"
                            : "system.column.error.enable",
                        RedBrush);
                }
            }
            catch (Exception ex)
            {
                if (_disposed) return;
                Log.Error(ex, "Explorer note column operation failed");
                SetStatus(
                    ColumnStatusText,
                    "system.column.error.unexpected",
                    RedBrush);
            }
            finally
            {
                if (!_disposed)
                {
                    ColumnButton.Content = _columnEnabled
                        ? I18n("action.remove")
                        : I18n("action.enable");
                    ColumnButton.IsEnabled = true;
                }
            }
        }

        private async void ContextMenuButton_Click(object sender, RoutedEventArgs e)
        {
            ContextMenuButton.IsEnabled = false;
            ContextMenuStatusText.Text = I18n("status.processing");
            try
            {
                var result = _contextMenuRegistered
                    ? await ServicesInitializer.ContextMenu.UnregisterAsync()
                    : await ServicesInitializer.ContextMenu.RegisterAsync();
                if (_disposed) return;
                if (result.IsSuccess)
                {
                    _contextMenuRegistered = !_contextMenuRegistered;
                    SetStatus(
                        ContextMenuStatusText,
                        _contextMenuRegistered
                            ? "status.registered"
                            : "status.notRegistered",
                        _contextMenuRegistered ? GreenBrush : GrayBrush);
                }
                else
                {
                    Log.Warning(
                        "Legacy context menu operation failed ({ErrorCode}): {Error}",
                        result.ErrorCode,
                        result.ErrorMessage);
                    SetStatus(
                        ContextMenuStatusText,
                        _contextMenuRegistered
                            ? "system.legacy.error.unregister"
                            : "system.legacy.error.register",
                        RedBrush);
                }
            }
            catch (Exception ex)
            {
                if (_disposed) return;
                Log.Error(ex, "Legacy context menu operation failed");
                SetStatus(
                    ContextMenuStatusText,
                    "system.legacy.error.unexpected",
                    RedBrush);
            }
            finally
            {
                if (!_disposed)
                {
                    ContextMenuButton.Content = _contextMenuRegistered
                        ? I18n("action.remove")
                        : I18n("action.register");
                    ContextMenuButton.IsEnabled = true;
                }
            }
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
            if (dialog.ShowDialog(DialogOwnerResolver.Resolve(this)) != true) return;

            var action = _sparsePackageInstalled
                ? I18n("system.sparse.action.upgrade")
                : I18n("system.sparse.action.register");
            var approved = ThemedMessageDialog.ShowConfirmation(
                Window.GetWindow(this),
                string.Format(
                    I18n("system.sparse.confirm.registerOrUpgrade.message"),
                    action),
                string.Format(
                    I18n("system.sparse.confirm.registerOrUpgrade.title"),
                    action),
                ThemedMessageDialogTone.Info);
            if (!approved) return;

            await RunSparsePackageOperationAsync(
                () => ServicesInitializer.SparsePackage.RegisterOrUpgradeAsync(
                    dialog.FileName));
        }

        private async void SparsePackageRemoveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_sparsePackageBusy || !_sparsePackageInstalled) return;
            var approved = ThemedMessageDialog.ShowConfirmation(
                Window.GetWindow(this),
                I18n("system.sparse.confirm.uninstall.message"),
                I18n("system.sparse.confirm.uninstall.title"),
                ThemedMessageDialogTone.Danger);
            if (!approved) return;

            await RunSparsePackageOperationAsync(
                () => ServicesInitializer.SparsePackage.UnregisterAsync());
        }

        private async Task RunSparsePackageOperationAsync(
            Func<Task<SparsePackageOperationResult>> operation)
        {
            SetSparsePackageBusy(true, I18n("status.processing"));
            var result = await operation();
            if (_disposed) return;
            if (result.State is { } state)
                ApplySparsePackageState(state);
            else
            {
                SetSparsePackageBusy(false);
                await RefreshSparsePackageStatusAsync();
            }

            if (!result.IsSuccess && !_disposed)
            {
                Log.Warning(
                    "Sparse package operation failed ({ErrorCode}): {Error}",
                    (int)result.ErrorCode,
                    result.Message);
                SetStatus(
                    SparsePackageStatusText,
                    SparsePackagePresentation.GetErrorResourceKey(
                        result.ErrorCode),
                    RedBrush);
            }
            if (!_disposed)
                SetSparsePackageBusy(false);
        }

        private async Task RefreshSparsePackageStatusAsync()
        {
            if (_disposed || _sparsePackageBusy) return;
            SetSparsePackageBusy(true, I18n("status.checking"));
            var result = await ServicesInitializer.SparsePackage.GetStatusAsync();
            if (_disposed) return;
            if (result.IsSuccess && result.State is { } state)
                ApplySparsePackageState(state);
            else
            {
                _sparsePackageInstalled = result.State?.Installed ?? false;
                Log.Warning(
                    "Sparse package status failed ({ErrorCode}): {Error}",
                    (int)result.ErrorCode,
                    result.Message);
                SetStatus(
                    SparsePackageStatusText,
                    SparsePackagePresentation.GetErrorResourceKey(
                        result.ErrorCode),
                    RedBrush);
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
                    state.Version ?? "-",
                    state.Architecture ?? "-",
                    locationState);
                SparsePackageStatusText.Foreground = GreenBrush;
            }
            else
            {
                SetStatus(
                    SparsePackageStatusText,
                    "status.notRegistered",
                    GrayBrush);
            }
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
            _columnEnabled =
                ServicesInitializer.ColumnInjection.IsCommentColumnEnabled;
            ColumnButton.Content = _columnEnabled
                ? I18n("action.remove")
                : I18n("action.enable");
            SetStatus(
                ColumnStatusText,
                _columnEnabled ? "status.enabled" : "status.disabled",
                _columnEnabled ? GreenBrush : GrayBrush);
        }

        private void RefreshContextMenuStatus()
        {
            _contextMenuRegistered =
                ServicesInitializer.ContextMenu.IsRegistered;
            ContextMenuButton.Content = _contextMenuRegistered
                ? I18n("action.remove")
                : I18n("action.register");
            SetStatus(
                ContextMenuStatusText,
                _contextMenuRegistered
                    ? "status.registered"
                    : "status.notRegistered",
                _contextMenuRegistered ? GreenBrush : GrayBrush);
        }

        private static void SetStatus(
            TextBlock target,
            string resourceKey,
            Brush foreground)
        {
            target.Text = I18n(resourceKey);
            target.Foreground = foreground;
        }

        private Brush GreenBrush => (Brush)FindResource("SuccessGreenBrush");
        private Brush GrayBrush => (Brush)FindResource("TextSecondaryBrush");
        private Brush RedBrush => (Brush)FindResource("DangerRedBrush");

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);
    }
}
