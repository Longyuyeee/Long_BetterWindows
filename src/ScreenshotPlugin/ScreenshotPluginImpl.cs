using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Views;
using Serilog;

namespace ScreenshotPlugin;

public class ScreenshotPluginImpl : ILongPlugin, IHasSettingsUI, IHasMainUI, IPluginCommandHandler
{
    private IHostApi _host = null!;
    private readonly List<string> _registeredHotkeys = new();
    private string _fullHotkey = "Ctrl+Shift+S";
    private string _regionHotkey = "Ctrl+Shift+A";

    public string Id => "com.long.screenshot";
    public string Name => "截图工具";
    public string Version => "1.1.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;
        return Task.FromResult(true);
    }

    public async Task<bool> StartAsync()
    {
        _fullHotkey = await RegisterWithFallbackAsync(
            "Ctrl+Shift+S", "Ctrl+Alt+Shift+S", CaptureFullScreen);
        _regionHotkey = await RegisterWithFallbackAsync(
            "Ctrl+Shift+A", "Ctrl+Alt+Shift+A", CaptureRegion);

        State = PluginState.Running;
        Log.Information("[Screenshot] 已启动: 全屏 {Full}，区域 {Region}", _fullHotkey, _regionHotkey);
        return true;
    }

    private async Task<string> RegisterWithFallbackAsync(
        string preferred, string fallback, Action callback)
    {
        var result = await _host.HotKey.RegisterAsync(preferred, Id, callback);
        if (result.IsSuccess)
        {
            _registeredHotkeys.Add(preferred);
            return preferred;
        }

        Log.Warning("[Screenshot] 热键 {Preferred} 冲突，尝试 {Fallback}", preferred, fallback);
        result = await _host.HotKey.RegisterAsync(fallback, Id, callback);
        if (result.IsSuccess)
        {
            _registeredHotkeys.Add(fallback);
            return fallback;
        }

        Log.Warning("[Screenshot] 热键不可用，命令中心入口仍可执行: {Preferred}", preferred);
        return "命令中心";
    }

    public async Task<bool> StopAsync()
    {
        foreach (var hotkey in _registeredHotkeys)
            await _host.HotKey.UnregisterAsync(hotkey);
        _registeredHotkeys.Clear();
        State = PluginState.Stopped;
        return true;
    }

    public void ShowMainUI() => CaptureRegion();

    public Task<PluginCommandResult> ExecuteCommandAsync(
        PluginCommandInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (invocation.CommandId)
        {
            case "screenshot.full":
                CaptureFullScreen();
                return Task.FromResult(PluginCommandResult.Success("全屏截图已开始"));
            case "screenshot.region":
                CaptureRegion();
                return Task.FromResult(PluginCommandResult.Success("拖拽选择截图区域"));
            default:
                return Task.FromResult(PluginCommandResult.Failure($"未知截图命令: {invocation.CommandId}"));
        }
    }

    private void CaptureFullScreen()
    {
        Application.Current.Dispatcher.Invoke(async () =>
        {
            try
            {
                var bitmap = ScreenCapture.Capture(
                    (int)SystemParameters.VirtualScreenLeft,
                    (int)SystemParameters.VirtualScreenTop,
                    (int)SystemParameters.VirtualScreenWidth,
                    (int)SystemParameters.VirtualScreenHeight);
                var result = await _host.Clipboard.SetImageAsync(bitmap);
                if (!result.IsSuccess)
                    throw new InvalidOperationException(result.ErrorMessage ?? "剪贴板写入失败");

                FloatingHudWindow.ShowToast($"全屏截图已复制 · {bitmap.PixelWidth} × {bitmap.PixelHeight}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Screenshot] 全屏截图失败");
                FloatingHudWindow.ShowToast("截图失败，请稍后重试");
            }
        });
    }

    private void CaptureRegion()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var selector = new RegionSelectorWindow();
            selector.RegionSelected += async bitmap =>
            {
                var result = await _host.Clipboard.SetImageAsync(bitmap);
                FloatingHudWindow.ShowToast(result.IsSuccess
                    ? $"区域截图已复制 · {bitmap.PixelWidth} × {bitmap.PixelHeight}"
                    : "截图完成，但写入剪贴板失败");
            };
            selector.Show();
            selector.Activate();
        });
    }

    public FrameworkElement CreateSettingsUI()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new HotkeySettingsControl("全屏截图", Id, _fullHotkey, value => _fullHotkey = value));
        panel.Children.Add(new HotkeySettingsControl("区域截图", Id, _regionHotkey, value => _regionHotkey = value));
        return panel;
    }
}

internal static class ScreenCapture
{
    private const uint Srccopy = 0x00CC0020;

    public static BitmapSource Capture(int x, int y, int width, int height)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = CreateCompatibleBitmap(screenDc, width, height);
        var previous = SelectObject(memoryDc, bitmapHandle);

        try
        {
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, Srccopy))
                throw new InvalidOperationException("BitBlt failed.");

            var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                bitmapHandle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            SelectObject(memoryDc, previous);
            DeleteObject(bitmapHandle);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
}
