using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Core;
using Serilog;

namespace ScreenshotPlugin;

public class ScreenshotPluginImpl : ILongPlugin, IHasSettingsUI, IHasMainUI
{
    private IHostApi? _host;
    private string _fullHotkey = "Ctrl+Shift+S";
    private string _regionHotkey = "Ctrl+Shift+A";

    public string Id => "com.long.screenshot";
    public string Name => "截图工具";
    public string Version => "1.1.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    const uint SRCCOPY = 0x00CC0020;

    public Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;
        if (host.HotKey == null) { State = PluginState.Error; return Task.FromResult(false); }
        return Task.FromResult(true);
    }

    public async Task<bool> StartAsync()
    {
        var r1 = await _host!.HotKey!.RegisterAsync(_fullHotkey, Id, CaptureFullScreen);
        var r2 = await _host!.HotKey!.RegisterAsync(_regionHotkey, Id, CaptureRegion);
        if (!r1.IsSuccess || !r2.IsSuccess) { State = PluginState.Error; return false; }
        State = PluginState.Running;
        return true;
    }

    public async Task<bool> StopAsync()
    {
        await _host!.HotKey!.UnregisterAsync(_fullHotkey);
        await _host!.HotKey!.UnregisterAsync(_regionHotkey);
        State = PluginState.Disabled;
        return true;
    }

    public void ShowMainUI() => CaptureFullScreen();

    private void CaptureFullScreen()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                var screen = SystemParameters.PrimaryScreenWidth;
                var screenH = SystemParameters.PrimaryScreenHeight;
                int w = (int)screen, h = (int)screenH;

                var dc = GetDC(IntPtr.Zero);
                var memDc = CreateCompatibleDC(dc);
                var bmp = CreateCompatibleBitmap(dc, w, h);
                SelectObject(memDc, bmp);
                BitBlt(memDc, 0, 0, w, h, dc, 0, 0, SRCCOPY);

                var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                Clipboard.SetImage(bitmap);

                DeleteObject(bmp);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, dc);

                LongBetterWindows.Host.Views.FloatingHudWindow.ShowToast($"截图已复制到剪贴板 ({w}x{h})");
                Log.Information("[Screenshot] 截图完成 {W}x{H}", w, h);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Screenshot] 截图失败");
            }
        });
    }

    private void CaptureRegion()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var selector = new RegionSelector();
            selector.RegionSelected += bitmap =>
            {
                try { Clipboard.SetImage(bitmap); } catch { }
                LongBetterWindows.Host.Views.FloatingHudWindow.ShowToast(
                    $"区域截图已复制 ({bitmap.PixelWidth}x{bitmap.PixelHeight})");
            };
            selector.ShowDialog();
        });
    }

    public FrameworkElement CreateSettingsUI()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new LongBetterWindows.Host.Views.HotkeySettingsControl(
            "全屏截图", Id, _fullHotkey, hk => _fullHotkey = hk));
        panel.Children.Add(new LongBetterWindows.Host.Views.HotkeySettingsControl(
            "区域截图", Id, _regionHotkey, hk => _regionHotkey = hk));
        return panel;
    }
}

/// <summary>
/// 区域选择覆盖窗口——拖拽选择截图区域
/// </summary>
public class RegionSelector : Window
{
    private Point _start, _end;
    private bool _drawing;
    private System.Windows.Shapes.Rectangle _rect = null!;

    public event Action<BitmapSource>? RegionSelected;

    public RegionSelector()
    {
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00));
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;

        _rect = new System.Windows.Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0x7A, 0xFF)),
            Visibility = Visibility.Collapsed,
        };

        var canvas = new Canvas();
        canvas.Children.Add(_rect);
        Content = canvas;

        canvas.MouseLeftButtonDown += (_, e) =>
        {
            _start = e.GetPosition(canvas);
            _drawing = true;
            _rect.Visibility = Visibility.Visible;
            Canvas.SetLeft(_rect, _start.X);
            Canvas.SetTop(_rect, _start.Y);
            _rect.Width = 0; _rect.Height = 0;
            CaptureMouse();
        };

        canvas.MouseMove += (_, e) =>
        {
            if (!_drawing) return;
            _end = e.GetPosition(canvas);
            var x = Math.Min(_start.X, _end.X);
            var y = Math.Min(_start.Y, _end.Y);
            var w = Math.Abs(_end.X - _start.X);
            var h = Math.Abs(_end.Y - _start.Y);
            Canvas.SetLeft(_rect, x); Canvas.SetTop(_rect, y);
            _rect.Width = w; _rect.Height = h;
        };

        canvas.MouseLeftButtonUp += (_, _) =>
        {
            _drawing = false;
            ReleaseMouseCapture();
            if (_rect.Width > 5 && _rect.Height > 5)
                CaptureAndClose();
            else
                Close();
        };

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private void CaptureAndClose()
    {
        try
        {
            var x = (int)Canvas.GetLeft(_rect);
            var y = (int)Canvas.GetTop(_rect);
            var w = (int)_rect.Width;
            var h = (int)_rect.Height;

            var dc = GetDC(IntPtr.Zero);
            var memDc = CreateCompatibleDC(dc);
            var bmp = CreateCompatibleBitmap(dc, w, h);
            SelectObject(memDc, bmp);
            BitBlt(memDc, 0, 0, w, h, dc, x, y, SRCCOPY);

            var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            DeleteObject(bmp); DeleteDC(memDc); ReleaseDC(IntPtr.Zero, dc);
            RegionSelected?.Invoke(bitmap);
        }
        catch { }
        Close();
    }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    const uint SRCCOPY = 0x00CC0020;
}
