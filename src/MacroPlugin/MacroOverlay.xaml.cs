using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MacroPlugin;

public partial class MacroOverlay : Window
{
    public MacroOverlay()
    {
        InitializeComponent();
    }

    public static MacroOverlay ShowOverlay()
    {
        var area = SystemParameters.WorkArea;
        var window = new MacroOverlay
        {
            Left = area.Right - 120,
            Top = area.Top + 16,
        };

        window.Show();
        return window;
    }

    public void SetRecording(int count)
    {
        Dispatcher.Invoke(() =>
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
            StatusText.Text = "REC";
            CountText.Text = count > 0 ? $"{count}" : "";

            // 录制时闪烁红点
            var anim = new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(500))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = true,
            };
            StatusDot.BeginAnimation(OpacityProperty, anim);
        });
    }

    public void SetPlaying(bool isLoop)
    {
        Dispatcher.Invoke(() =>
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF));
            StatusText.Text = isLoop ? "LOOP" : "PLAY";
            StatusDot.BeginAnimation(OpacityProperty, null);
            StatusDot.Opacity = 1;
        });
    }

    public void SetIdle()
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = "STOP";
            CountText.Text = "";
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
            StatusDot.BeginAnimation(OpacityProperty, null);
            StatusDot.Opacity = 1;

            // 2秒后自动隐藏
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2),
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                fade.Completed += (_, _) => Hide();
                BeginAnimation(OpacityProperty, fade);
            };
            timer.Start();
        });
    }
}
