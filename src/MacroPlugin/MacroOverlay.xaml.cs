using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Threading;

namespace MacroPlugin;

public sealed record MacroOverlayLocalization(
    string Recording,
    string Playing,
    string Looping,
    string Stopped);

public partial class MacroOverlay : Window
{
    private DispatcherTimer? _hideTimer;
    private MacroOverlayLocalization _localization =
        new("录制", "播放", "循环", "停止");
    private MacroOverlayState _state = MacroOverlayState.Recording;
    private int _actionCount;

    public MacroOverlay()
    {
        InitializeComponent();
    }

    public static MacroOverlay ShowOverlay(MacroOverlayLocalization localization)
    {
        var area = LongBetterWindows.Host.Services.MonitorHelper.GetCursorWorkArea();
        var window = new MacroOverlay
        {
            Left = area.Right - 120,
            Top = area.Top + 16,
        };
        window.ApplyLocalization(localization);

        window.Show();
        return window;
    }

    public void SetRecording(int count)
    {
        Dispatcher.Invoke(() =>
        {
            _state = MacroOverlayState.Recording;
            _actionCount = count;
            CancelPendingHide();
            StatusBorder.Background = (Brush)FindResource("Long.Brush.State.Danger");
            SetForeground("Long.Brush.Text.OnAccent", "Long.Brush.Text.OnAccentMuted");
            StatusText.Text = _localization.Recording;
            CountText.Text = count > 0 ? $"{count}" : "";

            // 录制时闪烁红点
            var duration = Application.Current.Resources["Long.Motion.Slow"] is Duration token
                ? token.TimeSpan
                : TimeSpan.FromMilliseconds(280);
            if (duration == TimeSpan.Zero)
            {
                StatusDot.Opacity = 1;
                return;
            }
            var anim = new DoubleAnimation(0.35, 1, duration)
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
            _state = isLoop
                ? MacroOverlayState.Looping
                : MacroOverlayState.Playing;
            CancelPendingHide();
            StatusBorder.Background = (Brush)FindResource("Long.Brush.Accent.Primary");
            SetForeground("Long.Brush.Text.OnAccent", "Long.Brush.Text.OnAccentMuted");
            StatusText.Text = isLoop
                ? _localization.Looping
                : _localization.Playing;
            StatusDot.BeginAnimation(OpacityProperty, null);
            StatusDot.Opacity = 1;
        });
    }

    public void SetIdle()
    {
        Dispatcher.Invoke(() =>
        {
            _state = MacroOverlayState.Stopped;
            _actionCount = 0;
            StatusText.Text = _localization.Stopped;
            CountText.Text = "";
            StatusBorder.Background = (Brush)FindResource("Long.Brush.Surface.Card");
            SetForeground("Long.Brush.Text.Primary", "Long.Brush.Text.Muted");
            StatusDot.BeginAnimation(OpacityProperty, null);
            StatusDot.Opacity = 1;

            // 2秒后自动隐藏
            CancelPendingHide();
            _hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2),
            };
            _hideTimer.Tick += (_, _) =>
            {
                CancelPendingHide();
                var duration = Application.Current.Resources["Long.Motion.Normal"] is Duration token
                    ? token.TimeSpan
                    : TimeSpan.FromMilliseconds(180);
                if (duration == TimeSpan.Zero)
                {
                    Hide();
                    return;
                }
                var fade = new DoubleAnimation(1, 0, duration);
                fade.Completed += (_, _) => Hide();
                BeginAnimation(OpacityProperty, fade);
            };
            _hideTimer.Start();
        });
    }

    public void ApplyLocalization(MacroOverlayLocalization localization)
    {
        _localization = localization;
        StatusText.Text = _state switch
        {
            MacroOverlayState.Recording => localization.Recording,
            MacroOverlayState.Playing => localization.Playing,
            MacroOverlayState.Looping => localization.Looping,
            _ => localization.Stopped,
        };
        CountText.Text = _actionCount > 0 ? _actionCount.ToString() : string.Empty;
    }

    private void SetForeground(string primaryKey, string secondaryKey)
    {
        StatusDot.Fill = (Brush)FindResource(primaryKey);
        StatusText.Foreground = (Brush)FindResource(primaryKey);
        CountText.Foreground = (Brush)FindResource(secondaryKey);
    }

    private void CancelPendingHide()
    {
        _hideTimer?.Stop();
        _hideTimer = null;
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
    }

    private enum MacroOverlayState
    {
        Recording,
        Playing,
        Looping,
        Stopped,
    }
}
