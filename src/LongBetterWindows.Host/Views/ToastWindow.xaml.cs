using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LongBetterWindows.Host.Views
{
    public partial class ToastWindow : Window
    {
        public ToastWindow()
        {
            InitializeComponent();
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
                var workArea = Services.MonitorHelper.GetCursorWorkArea();

                var window = new ToastWindow
                {
                    Opacity = 0,
                    Left = workArea.Right - 360,
                    Top = workArea.Bottom - 80,
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

                window.Loaded += (_, _) =>
                {
                    window.Left = workArea.Right - window.ActualWidth - 20;
                    window.Top = workArea.Bottom - window.ActualHeight - 20;
                };

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

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    var fadeOut = new DoubleAnimation(1, 0, normalDuration);
                    fadeOut.Completed += (_, _) => window.Close();
                    window.BeginAnimation(OpacityProperty, fadeOut);
                };
                timer.Start();
            });
        }
    }
}
