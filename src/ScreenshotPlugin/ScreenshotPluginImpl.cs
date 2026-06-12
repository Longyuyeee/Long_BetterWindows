using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Core;
using Serilog;

namespace ScreenshotPlugin;

public class ScreenshotPluginImpl : ILongPlugin, IHasSettingsUI, IHasMainUI
{
    private IHostApi? _host;
    private string _hotkey = "Ctrl+Shift+S";

    public string Id => "com.long.screenshot";
    public string Name => "截图工具";
    public string Version => "1.0.0";
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
        var r = await _host!.HotKey!.RegisterAsync(_hotkey, Id, CaptureScreen);
        if (!r.IsSuccess) { State = PluginState.Error; return false; }
        State = PluginState.Running;
        return true;
    }

    public async Task<bool> StopAsync()
    {
        await _host!.HotKey!.UnregisterAsync(_hotkey);
        State = PluginState.Disabled;
        return true;
    }

    public void ShowMainUI() => CaptureScreen();

    private void CaptureScreen()
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

    public FrameworkElement CreateSettingsUI()
    {
        return new LongBetterWindows.Host.Views.HotkeySettingsControl(
            "截图工具", Id, _hotkey, hk => _hotkey = hk);
    }
}
