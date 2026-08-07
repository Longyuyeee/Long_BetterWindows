using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using LongBetterWindows.Host.Automation;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using Serilog;

namespace LongBetterWindows.Host.Views
{
    public partial class PluginWindowHost : Window
    {
        private Window? _returnTarget;
        private readonly Func<Task>? _endRequested;
        private readonly QualityWindowAutomation? _qualityAutomation;
        private PluginWindowMode _windowMode = PluginWindowMode.Standard;
        private bool _initialPlacementFinalized;
        private bool _placementRefreshPending;
        private HwndSource? _windowSource;

        private const int WmSettingChange = 0x001A;
        private const int WmDisplayChange = 0x007E;
        private const int SpiSetWorkArea = 0x002F;

        public PluginWindowHost(
            string pluginId,
            string title,
            FrameworkElement content,
            PluginWindowPreference? preference = null,
            string? sessionId = null,
            Func<Task>? endRequested = null)
        {
            InitializeComponent();
            Title = title;
            PluginTitle.Text = title;
            PluginContent.Content = content;
            _endRequested = endRequested;
            EndRunButton.Visibility = endRequested is null
                ? Visibility.Collapsed
                : Visibility.Visible;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                AutomationProperties.SetItemStatus(
                    this,
                    $"plugin-session:{sessionId};placement:detached");
            }
            Icon = PluginTaskbarIdentity.CreateIcon(pluginId, title);
            SourceInitialized += (_, _) =>
            {
                _windowSource = HwndSource.FromHwnd(
                    new WindowInteropHelper(this).Handle);
                _windowSource?.AddHook(WindowMessageHook);
                if (!PluginTaskbarIdentity.Apply(this, pluginId))
                    Log.Warning(
                        "Could not apply detached taskbar identity for plugin {PluginId}",
                        pluginId);
                ApplyInitialPlacement(pluginId);
            };
            ContentRendered += (_, _) =>
            {
                if (_initialPlacementFinalized)
                    return;
                _initialPlacementFinalized = true;
                ApplyInitialPlacement(pluginId);
            };
            _qualityAutomation = QualityWindowAutomation.Attach(
                this,
                ExecuteQualityWindowAction);
            Closed += (_, _) =>
            {
                _windowSource?.RemoveHook(WindowMessageHook);
                _windowSource = null;
                _qualityAutomation?.Dispose();
            };
            ApplyWindowPreference(preference);
        }

        internal void SetReturnTarget(Window? target) => _returnTarget = target;
        internal bool ReturnRequested { get; private set; }

        internal void UpdateTitle(string title)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateTitle(title));
                return;
            }
            Title = title;
            PluginTitle.Text = title;
        }

        private void ApplyInitialPlacement(string pluginId)
        {
            if (!PluginWindowPlacement.TryApply(
                    this,
                    _returnTarget ?? Owner ?? Application.Current.MainWindow,
                    _windowMode == PluginWindowMode.FullScreen))
            {
                Log.Debug(
                    "Could not apply source-monitor placement for plugin {PluginId}",
                    pluginId);
            }
        }

        private IntPtr WindowMessageHook(
            IntPtr window,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message == WmDisplayChange
                || (message == WmSettingChange
                    && wParam.ToInt64() == SpiSetWorkArea))
            {
                SchedulePlacementRefresh();
            }
            return IntPtr.Zero;
        }

        private void SchedulePlacementRefresh()
        {
            if (_placementRefreshPending)
                return;
            _placementRefreshPending = true;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () =>
                {
                    _placementRefreshPending = false;
                    if (IsLoaded)
                        PluginWindowPlacement.TryConstrainToNearestWorkArea(this);
                });
        }

        private void ApplyWindowPreference(PluginWindowPreference? preference)
        {
            if (preference == null) return;

            _windowMode = preference.Mode;

            (Width, Height) = preference.Mode switch
            {
                PluginWindowMode.Compact => (560, 400),
                PluginWindowMode.Wide => (1040, 680),
                PluginWindowMode.Document => (980, 760),
                PluginWindowMode.Overlay => (720, 520),
                PluginWindowMode.FullScreen => (720, 560),
                _ => (720, 560),
            };

            if (preference.PreferredWidth is > 0) Width = preference.PreferredWidth.Value;
            if (preference.PreferredHeight is > 0) Height = preference.PreferredHeight.Value;
            if (preference.MinWidth is > 0) MinWidth = preference.MinWidth.Value;
            if (preference.MinHeight is > 0) MinHeight = preference.MinHeight.Value;

            if (preference.Mode == PluginWindowMode.FullScreen)
                WindowStartupLocation = WindowStartupLocation.Manual;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && ResizeMode != ResizeMode.NoResize)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Back_Click(object sender, RoutedEventArgs e) => ReturnToOwner();

        private async void EndRun_Click(object sender, RoutedEventArgs e)
        {
            if (_endRequested is null)
                return;
            EndRunButton.IsEnabled = false;
            try
            {
                await _endRequested();
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Could not end detached plugin run");
                EndRunButton.IsEnabled = true;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            if (WebPluginUiModalState.IsOpen(
                    PluginContent.Content as DependencyObject))
            {
                return;
            }
            ReturnToOwner();
            e.Handled = true;
        }

        private bool ExecuteQualityWindowAction(QualityWindowAction action)
        {
            if (action != QualityWindowAction.Dismiss)
                return false;
            ReturnToOwner();
            return true;
        }

        private void ReturnToOwner()
        {
            ReturnRequested = true;
            var owner = _returnTarget ?? Owner;
            Close();
            if (owner is null) return;
            if (!owner.IsVisible) owner.Show();
            if (owner.WindowState == WindowState.Minimized)
                owner.WindowState = WindowState.Normal;
            owner.Activate();
        }

        public void DetachContent() => PluginContent.Content = null;

        internal void ReturnToOwnerForQuality() => ReturnToOwner();
    }
}
