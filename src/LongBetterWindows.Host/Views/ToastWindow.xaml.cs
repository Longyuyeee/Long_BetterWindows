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
                window.MessageText.Text = message;
                window.Show();

                // 自适应位置：根据实际宽度调整到右下角
                window.Loaded += (_, _) =>
                {
                    window.Left = workArea.Right - window.ActualWidth - 20;
                    window.Top = workArea.Bottom - window.ActualHeight - 20;
                };

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                window.BeginAnimation(OpacityProperty, fadeIn);

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                    fadeOut.Completed += (_, _) => window.Close();
                    window.BeginAnimation(OpacityProperty, fadeOut);
                };
                timer.Start();
            });
        }
    }
}
