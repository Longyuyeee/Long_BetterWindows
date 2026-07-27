using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Views;
using Serilog;

namespace ScreenshotPlugin;

public class ScreenshotPluginImpl :
    ILongPlugin,
    IHasSettingsUI,
    IHasMainUI,
    IPluginCommandHandler,
    IPluginLanguageLifecycle
{
    private IHostApi _host = null!;
    private readonly List<string> _registeredHotkeys = new();
    private readonly List<WeakReference<HotkeySettingsControl>> _fullSettings = [];
    private readonly List<WeakReference<HotkeySettingsControl>> _regionSettings = [];
    private string _configuredFullHotkey = "Ctrl+Shift+S";
    private string _configuredRegionHotkey = "Ctrl+Shift+A";
    private string? _registeredFullHotkey;
    private string? _registeredRegionHotkey;
    private RegionSelectorWindow? _selector;
    private IReadOnlyDictionary<string, string> _strings =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string Id => "com.long.screenshot";
    public string Name => Text("plugin.name", "截图工具");
    public string Version => "1.1.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;
        return Task.FromResult(true);
    }

    public async Task<bool> StartAsync()
    {
        _registeredFullHotkey = await RegisterWithFallbackAsync(
            _configuredFullHotkey,
            "Ctrl+Alt+Shift+S",
            CaptureFullScreen);
        _registeredRegionHotkey = await RegisterWithFallbackAsync(
            _configuredRegionHotkey,
            "Ctrl+Alt+Shift+A",
            CaptureRegion);

        State = PluginState.Running;
        Log.Information(
            "[Screenshot] 已启动: 全屏 {Full}，区域 {Region}",
            _registeredFullHotkey ?? "command-center",
            _registeredRegionHotkey ?? "command-center");
        return true;
    }

    private async Task<string?> RegisterWithFallbackAsync(
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
        return null;
    }

    public async Task<bool> StopAsync()
    {
        foreach (var hotkey in _registeredHotkeys)
            await _host.HotKey.UnregisterAsync(hotkey);
        _registeredHotkeys.Clear();
        _registeredFullHotkey = null;
        _registeredRegionHotkey = null;
        Application.Current.Dispatcher.Invoke(() => _selector?.Close());
        State = PluginState.Stopped;
        return true;
    }

    public void ShowMainUI() => CaptureRegion();

    public async Task<PluginCommandResult> ExecuteCommandAsync(
        PluginCommandInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (invocation.CommandId)
        {
            case "screenshot.full":
                return await CaptureFullScreenAsync();
            case "screenshot.region":
                CaptureRegion();
                return PluginCommandResult.Success(
                    Text("command.regionStarted", "拖拽选择截图区域"));
            default:
                return PluginCommandResult.Failure(string.Format(
                    Text("error.unknownCommand", "未知截图命令: {0}"),
                    invocation.CommandId));
        }
    }

    private void CaptureFullScreen() => _ = CaptureFullScreenAsync();

    private async Task<PluginCommandResult> CaptureFullScreenAsync()
    {
        try
        {
            var bitmap = await Application.Current.Dispatcher.InvokeAsync(() =>
                ScreenCapture.Capture(
                    (int)SystemParameters.VirtualScreenLeft,
                    (int)SystemParameters.VirtualScreenTop,
                    (int)SystemParameters.VirtualScreenWidth,
                    (int)SystemParameters.VirtualScreenHeight));
            var result = await _host.Clipboard.SetImageAsync(bitmap);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    result.ErrorMessage ?? Text(
                        "toast.clipboardFailed",
                        "截图完成，但写入剪贴板失败"));
            }

            var message = string.Format(
                Text("toast.fullCopied", "全屏截图已复制 · {0} × {1}"),
                bitmap.PixelWidth,
                bitmap.PixelHeight);
            ShowToast(message);
            return PluginCommandResult.Success(message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Screenshot] 全屏截图失败");
            var message = Text(
                "toast.captureFailed",
                "截图失败，请稍后重试");
            ShowToast(message);
            return PluginCommandResult.Failure(message);
        }
    }

    private static void ShowToast(string message)
        => Application.Current.Dispatcher.Invoke(
            () => FloatingHudWindow.ShowToast(message));

    private void CaptureRegion()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_selector?.IsVisible == true)
            {
                _selector.Activate();
                return;
            }

            var selector = new RegionSelectorWindow(CreateSelectorLocalization());
            _selector = selector;
            selector.Closed += (_, _) =>
            {
                if (ReferenceEquals(_selector, selector))
                    _selector = null;
            };
            selector.RegionSelected += async bitmap =>
            {
                try
                {
                    var result = await _host.Clipboard.SetImageAsync(bitmap);
                    FloatingHudWindow.ShowToast(result.IsSuccess
                        ? string.Format(
                            Text("toast.regionCopied", "区域截图已复制 · {0} × {1}"),
                            bitmap.PixelWidth,
                            bitmap.PixelHeight)
                        : Text(
                            "toast.clipboardFailed",
                            "截图完成，但写入剪贴板失败"));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Screenshot] 区域截图写入剪贴板失败");
                    FloatingHudWindow.ShowToast(Text(
                        "toast.clipboardFailed",
                        "截图完成，但写入剪贴板失败"));
                }
            };
            selector.CaptureFailed += ex =>
            {
                Log.Error(ex, "[Screenshot] 区域截图失败");
                FloatingHudWindow.ShowToast(Text(
                    "toast.captureFailed",
                    "截图失败，请稍后重试"));
            };
            selector.Show();
            selector.Activate();
        });
    }

    public FrameworkElement CreateSettingsUI()
    {
        var panel = new StackPanel();
        var full = new HotkeySettingsControl(
            Text("settings.fullTitle", "全屏截图"),
            Id,
            _registeredFullHotkey
                ?? Text("settings.commandCenter", "命令中心"),
            value =>
            {
                ReplaceRegisteredHotkey(_registeredFullHotkey, value);
                _configuredFullHotkey = value;
                _registeredFullHotkey = value;
            },
            CreateSettingsLocalization(),
            CaptureFullScreen);
        var region = new HotkeySettingsControl(
            Text("settings.regionTitle", "区域截图"),
            Id,
            _registeredRegionHotkey
                ?? Text("settings.commandCenter", "命令中心"),
            value =>
            {
                ReplaceRegisteredHotkey(_registeredRegionHotkey, value);
                _configuredRegionHotkey = value;
                _registeredRegionHotkey = value;
            },
            CreateSettingsLocalization(),
            CaptureRegion);
        _fullSettings.Add(new WeakReference<HotkeySettingsControl>(full));
        _regionSettings.Add(new WeakReference<HotkeySettingsControl>(region));
        panel.Children.Add(full);
        panel.Children.Add(region);
        return panel;
    }

    public Task OnLanguageChangedAsync(
        PluginLanguageContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _strings = context.Resources;
        var application = Application.Current;
        if (application is null)
            return Task.CompletedTask;

        application.Dispatcher.Invoke(() =>
        {
            _selector?.ApplyLocalization(CreateSelectorLocalization());
            ApplySettingsLocalization(
                _fullSettings,
                Text("settings.fullTitle", "全屏截图"));
            ApplySettingsLocalization(
                _regionSettings,
                Text("settings.regionTitle", "区域截图"));
        });
        return Task.CompletedTask;
    }

    private void ApplySettingsLocalization(
        List<WeakReference<HotkeySettingsControl>> references,
        string title)
    {
        references.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in references)
        {
            if (reference.TryGetTarget(out var control))
                control.ApplyLocalization(title, CreateSettingsLocalization());
        }
    }

    private void ReplaceRegisteredHotkey(string? previous, string current)
    {
        if (previous is not null)
        {
            _registeredHotkeys.RemoveAll(existing =>
                existing.Equals(previous, StringComparison.OrdinalIgnoreCase));
        }
        if (!_registeredHotkeys.Contains(current, StringComparer.OrdinalIgnoreCase))
            _registeredHotkeys.Add(current);
    }

    private RegionSelectorLocalization CreateSelectorLocalization()
        => new(
            Text("overlay.automationName", "截图区域选择器"),
            Text("overlay.instruction", "拖拽选择截图区域"),
            Text("overlay.cancel", "· ESC 取消"));

    private HotkeySettingsLocalization CreateSettingsLocalization()
        => new(
            Text("settings.currentHotkey", "当前快捷键"),
            Text("settings.apply", "应用"),
            Text("settings.unchanged", "未修改"),
            Text("settings.conflict", "冲突: 已被「{0}」占用"),
            Text("settings.updated", "已更新"),
            Text("settings.changeFailed", "修改失败: {0}"),
            Text(
                "settings.formatHint",
                "格式: Ctrl+K  Alt+M  Win+N  Ctrl+Shift+Space  F6"));

    private string Text(string key, string fallback)
        => _strings.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;
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
