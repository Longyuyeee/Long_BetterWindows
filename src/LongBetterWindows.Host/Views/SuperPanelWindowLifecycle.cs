using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views
{
    internal sealed class SuperPanelWindowLifecycle : IDisposable
    {
        private const int WmMouseWheel = 0x020A;
        private readonly Window _window;
        private readonly FrameworkElement _panelChrome;
        private readonly Action<int> _cycleGroup;
        private IntPtr _foregroundWindow;
        private HwndSource? _windowSource;

        internal SuperPanelWindowLifecycle(
            Window window,
            FrameworkElement panelChrome,
            Action<int> cycleGroup)
        {
            _window = window;
            _panelChrome = panelChrome;
            _cycleGroup = cycleGroup;
        }

        internal void CaptureForegroundWindow(IntPtr foregroundWindow) =>
            _foregroundWindow = foregroundWindow;

        internal void Present(bool animate)
        {
            if (!_window.IsVisible)
                _window.Show();
            PositionNearCursor();
            _window.Activate();
            if (animate)
                AnimateIn();
        }

        internal void Dismiss(bool restoreFocus)
        {
            _window.Hide();
            if (restoreFocus && _foregroundWindow != IntPtr.Zero)
                Shell32.SetForegroundWindow(_foregroundWindow);
        }

        internal void AttachWindowMessageHook()
        {
            _windowSource = HwndSource.FromHwnd(
                new WindowInteropHelper(_window).Handle);
            _windowSource?.AddHook(WindowMessageHook);
        }

        internal void HandleDeactivated(bool keepVisible)
        {
            if (!keepVisible)
                _window.Hide();
        }

        internal static Point CalculatePosition(
            Point cursor,
            Rect workArea,
            Size windowSize,
            double gap = 16,
            double margin = 10)
        {
            var left = cursor.X + gap;
            var top = cursor.Y + gap;
            if (left + windowSize.Width > workArea.Right - margin)
                left = cursor.X - windowSize.Width - gap;
            if (top + windowSize.Height > workArea.Bottom - margin)
                top = cursor.Y - windowSize.Height - gap;

            return new Point(
                Math.Clamp(
                    left,
                    workArea.Left + margin,
                    Math.Max(workArea.Left + margin,
                        workArea.Right - windowSize.Width - margin)),
                Math.Clamp(
                    top,
                    workArea.Top + margin,
                    Math.Max(workArea.Top + margin,
                        workArea.Bottom - windowSize.Height - margin)));
        }

        private void PositionNearCursor()
        {
            var placement = MonitorHelper.GetCursorPlacement(_window);
            var size = new Size(
                _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width,
                _window.ActualHeight > 0 ? _window.ActualHeight : _window.Height);
            var position = CalculatePosition(placement.Cursor, placement.WorkArea, size);
            _window.Left = position.X;
            _window.Top = position.Y;
        }

        private void AnimateIn()
        {
            var duration = Application.Current.Resources["Long.Motion.Normal"] is Duration token
                ? token.TimeSpan
                : TimeSpan.FromMilliseconds(180);
            _window.Opacity = duration == TimeSpan.Zero ? 1 : 0;
            var translate = new TranslateTransform(0, duration == TimeSpan.Zero ? 0 : 8);
            _panelChrome.RenderTransform = translate;
            if (duration == TimeSpan.Zero) return;
            _window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }

        private IntPtr WindowMessageHook(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message != WmMouseWheel)
                return IntPtr.Zero;

            var delta = unchecked((short)((wParam.ToInt64() >> 16) & 0xffff));
            _cycleGroup(delta);
            handled = true;
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_windowSource is null) return;
            _windowSource.RemoveHook(WindowMessageHook);
            _windowSource = null;
        }
    }
}
