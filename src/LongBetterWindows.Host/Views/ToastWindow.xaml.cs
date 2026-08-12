using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LongBetterWindows.PluginSdk.Wpf;

namespace LongBetterWindows.Host.Views
{
    public partial class ToastWindow : Window
    {
        private const double EdgeInset = 20;
        private const double StackGap = 8;
        private static readonly List<ToastWindow> ActiveWindows = [];

        private DispatcherTimer? _closeTimer;
        private Rect _workArea;

        public ToastWindow()
        {
            InitializeComponent();
            TransientWindowBehavior.MakeNonActivating(this, clickThrough: true);
            Closed += Window_Closed;
        }

        public static void Show(string message)
            => ShowInternal(message, null);

        public static void ShowSuccess(string message)
            => ShowInternal(message, "Long.Brush.State.Success");

        public static void ShowError(string message)
            => ShowInternal(message, "Long.Brush.State.Danger");

        private static void ShowInternal(string message, string? accentKey)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var window = new ToastWindow
                {
                    Opacity = 0,
                };

                if (accentKey != null)
                {
                    var brush = Application.Current.TryFindResource(accentKey) as System.Windows.Media.Brush;
                    if (brush != null)
                    {
                        window.AccentBar.Background = brush;
                        window.AccentBar.Visibility = Visibility.Visible;
                    }
                }

                window.MessageText.Text = message;
                window.Show();
                window.UpdateLayout();
                window._workArea = MonitorHelper.GetCursorPlacement(window).WorkArea;
                ActiveWindows.Add(window);
                Reposition(window._workArea);

                var normalDuration = Application.Current.Resources["Long.Motion.Normal"] is Duration token
                    ? token.TimeSpan
                    : TimeSpan.FromMilliseconds(180);
                if (normalDuration == TimeSpan.Zero)
                    window.Opacity = 1;

                var fadeIn = new DoubleAnimation(0, 1, normalDuration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                window.BeginAnimation(OpacityProperty, fadeIn);

                window._closeTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2),
                };
                window._closeTimer.Tick += (_, _) =>
                {
                    window._closeTimer?.Stop();
                    window._closeTimer = null;
                    if (normalDuration == TimeSpan.Zero)
                    {
                        window.Close();
                        return;
                    }

                    var fadeOut = new DoubleAnimation(1, 0, normalDuration);
                    fadeOut.Completed += (_, _) => window.Close();
                    window.BeginAnimation(OpacityProperty, fadeOut);
                };
                window._closeTimer.Start();
            });
        }

        private static void Reposition(Rect workArea)
        {
            var bottom = workArea.Bottom - EdgeInset;
            foreach (var window in ActiveWindows
                         .Where(window => window._workArea == workArea)
                         .Reverse())
            {
                window.Left = Math.Max(
                    workArea.Left + EdgeInset,
                    workArea.Right - window.ActualWidth - EdgeInset);
                window.Top = Math.Max(
                    workArea.Top + EdgeInset,
                    bottom - window.ActualHeight);
                bottom = window.Top - StackGap;
            }
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            _closeTimer?.Stop();
            _closeTimer = null;
            ActiveWindows.Remove(this);
            Reposition(_workArea);
        }
    }
}
