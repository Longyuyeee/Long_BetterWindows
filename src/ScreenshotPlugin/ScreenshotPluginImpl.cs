using System.Windows;
using System.Windows.Controls;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.PluginSdk.Wpf;
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
    private IPluginSettingsService _pluginSettings = null!;
    private INotificationService _notification = null!;
    private readonly List<string> _registeredHotkeys = new();
    private readonly List<WeakReference<HotkeySettingsControl>> _fullSettings = [];
    private readonly List<WeakReference<HotkeySettingsControl>> _regionSettings = [];
    private string _configuredFullHotkey = "Ctrl+Shift+S";
    private string _configuredRegionHotkey = "Ctrl+Shift+A";
    private string? _registeredFullHotkey;
    private string? _registeredRegionHotkey;
    private RegionSelectorWindow? _selector;
    private CancellationTokenSource _operationLifetime = new();
    private readonly ScreenshotOperationCoordinator _operations = new();
    private readonly ScreenshotClipboardWriter _clipboardWriter = new();
    private IReadOnlyDictionary<string, string> _strings =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string Id => "com.long.screenshot";
    public string Name => Text("plugin.name", "截图工具");
    public string Version => "1.2.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public async Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;
        _pluginSettings = host.Settings;
        _notification = host.Notification;
        var full = await _pluginSettings.GetAsync("full_hotkey");
        if (full.IsSuccess && !string.IsNullOrWhiteSpace(full.Data))
            _configuredFullHotkey = full.Data;
        var region = await _pluginSettings.GetAsync("region_hotkey");
        if (region.IsSuccess && !string.IsNullOrWhiteSpace(region.Data))
            _configuredRegionHotkey = region.Data;
        return true;
    }

    public async Task<bool> StartAsync()
    {
        if (_operationLifetime.IsCancellationRequested)
        {
            _operationLifetime.Dispose();
            _operationLifetime = new CancellationTokenSource();
        }
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
        if (!preferred.Equals(fallback, StringComparison.OrdinalIgnoreCase))
        {
            result = await _host.HotKey.RegisterAsync(
                fallback,
                Id,
                callback);
            if (result.IsSuccess)
            {
                _registeredHotkeys.Add(fallback);
                return fallback;
            }
        }

        Log.Warning("[Screenshot] 热键不可用，命令中心入口仍可执行: {Preferred}", preferred);
        return null;
    }

    public async Task<bool> StopAsync()
    {
        _operationLifetime.Cancel();
        foreach (var hotkey in _registeredHotkeys)
            await _host.HotKey.UnregisterAsync(hotkey);
        _registeredHotkeys.Clear();
        _registeredFullHotkey = null;
        _registeredRegionHotkey = null;
        var application = Application.Current;
        if (application is not null)
        {
            application.Dispatcher.Invoke(() =>
            {
                if (_selector?.IsVisible == true)
                    _selector.Close();
            });
        }
        _selector = null;
        State = PluginState.Stopped;
        return true;
    }

    public void ShowMainUI() => CaptureRegion();

    public async Task<PluginCommandResult> ExecuteCommandAsync(
        PluginCommandInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _operationLifetime.Token.ThrowIfCancellationRequested();
        switch (invocation.CommandId)
        {
            case "screenshot.full":
                return await CaptureFullScreenAsync(cancellationToken);
            case "screenshot.region":
                return StartRegionCapture();
            default:
                return PluginCommandResult.Failure(string.Format(
                    Text("error.unknownCommand", "未知截图命令: {0}"),
                    invocation.CommandId));
        }
    }

    private void CaptureFullScreen() => _ = CaptureFullScreenAsync();

    private async Task<PluginCommandResult> CaptureFullScreenAsync(
        CancellationToken cancellationToken = default)
    {
        var operation = _operations.TryBegin();
        if (operation is null)
            return BusyResult();
        var operationToken = _operationLifetime.Token;
        using var operationLease = operation;
        using var linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                operationToken);
        try
        {
            var bitmap = await CaptureAndCopyAsync(
                () => _host.ScreenCapture.CaptureToBitmapAsync(),
                linkedCancellation.Token);

            var message = string.Format(
                Text("toast.fullCopied", "全屏截图已复制 · {0} × {1}"),
                bitmap.PixelWidth,
                bitmap.PixelHeight);
            ShowToast(message);
            return PluginCommandResult.Success(message);
        }
        catch (OperationCanceledException)
            when (operationToken.IsCancellationRequested)
        {
            Log.Information(
                "[Screenshot] Cancelled pending full-screen clipboard delivery");
            return PluginCommandResult.Failure(
                "Screenshot delivery cancelled.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ScreenshotClipboardDeliveryException ex)
        {
            Log.Error(ex, "[Screenshot] 全屏截图写入剪贴板失败");
            var message = Text(
                "toast.clipboardFailed",
                "截图完成，但写入剪贴板失败");
            ShowToast(message);
            return PluginCommandResult.Failure(message);
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

    private void ShowToast(string message)
        => _ = _notification.ShowAsync(Name, message);

    private void CaptureRegion() => _ = StartRegionCapture();

    private PluginCommandResult StartRegionCapture()
    {
        if (_operationLifetime.IsCancellationRequested)
            return PluginCommandResult.Failure("Screenshot delivery cancelled.");
        var application = Application.Current;
        if (application is null)
        {
            return PluginCommandResult.Failure(Text(
                "toast.captureFailed",
                "截图失败，请稍后重试"));
        }

        return application.Dispatcher.Invoke(() =>
        {
            if (_selector is not null)
            {
                if (_selector.IsVisible)
                {
                    _selector.Activate();
                    return PluginCommandResult.Success(Text(
                        "command.regionStarted",
                        "拖拽选择截图区域"));
                }
                return BusyResult();
            }

            var operation = _operations.TryBegin();
            if (operation is null)
                return BusyResult();
            var operationToken = _operationLifetime.Token;
            RegionSelectorWindow selector;
            try
            {
                selector = new RegionSelectorWindow(
                    CreateSelectorLocalization());
            }
            catch (Exception ex)
            {
                operation.Dispose();
                Log.Error(ex, "[Screenshot] 无法创建区域选择器");
                var message = Text(
                    "toast.captureFailed",
                    "截图失败，请稍后重试");
                ShowToast(message);
                return PluginCommandResult.Failure(message);
            }
            _selector = selector;
            selector.Closed += (_, _) =>
            {
                if (!selector.HasCommittedSelection)
                    operation.Dispose();
                if (!selector.HasCommittedSelection
                    && ReferenceEquals(_selector, selector))
                    _selector = null;
            };
            selector.RegionSelected += async bounds =>
            {
                try
                {
                    var bitmap = await CaptureAndCopyAsync(
                        () => _host.ScreenCapture.CaptureRegionAsync(
                            bounds.X,
                            bounds.Y,
                            bounds.Width,
                            bounds.Height),
                        operationToken);
                    ShowToast(string.Format(
                        Text(
                            "toast.regionCopied",
                            "区域截图已复制 · {0} × {1}"),
                        bitmap.PixelWidth,
                        bitmap.PixelHeight));
                }
                catch (OperationCanceledException)
                    when (operationToken.IsCancellationRequested)
                {
                    Log.Information(
                        "[Screenshot] Cancelled pending region clipboard delivery");
                }
                catch (ScreenshotClipboardDeliveryException ex)
                {
                    Log.Error(ex, "[Screenshot] 区域截图写入剪贴板失败");
                    ShowToast(Text(
                        "toast.clipboardFailed",
                        "截图完成，但写入剪贴板失败"));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Screenshot] 区域截图失败");
                    ShowToast(Text(
                        "toast.captureFailed",
                        "截图失败，请稍后重试"));
                }
                finally
                {
                    operation.Dispose();
                    application.Dispatcher.Invoke(() =>
                    {
                        if (ReferenceEquals(_selector, selector))
                            _selector = null;
                    });
                }
            };
            selector.CaptureFailed += ex =>
            {
                operation.Dispose();
                if (ReferenceEquals(_selector, selector)
                    && !selector.IsVisible)
                {
                    _selector = null;
                }
                Log.Error(ex, "[Screenshot] 区域截图失败");
                ShowToast(Text(
                    "toast.captureFailed",
                    "截图失败，请稍后重试"));
            };
            try
            {
                selector.Show();
                selector.Activate();
                return PluginCommandResult.Success(Text(
                    "command.regionStarted",
                    "拖拽选择截图区域"));
            }
            catch (Exception ex)
            {
                operation.Dispose();
                if (ReferenceEquals(_selector, selector))
                    _selector = null;
                Log.Error(ex, "[Screenshot] 无法打开区域选择器");
                var message = Text(
                    "toast.captureFailed",
                    "截图失败，请稍后重试");
                ShowToast(message);
                return PluginCommandResult.Failure(message);
            }
        });
    }

    private PluginCommandResult BusyResult()
    {
        var message = Text(
            "toast.busy",
            "另一项截图操作仍在进行");
        ShowToast(message);
        return PluginCommandResult.Failure(message);
    }

    private async Task<System.Windows.Media.Imaging.BitmapSource>
        CaptureAndCopyAsync(
            Func<Task<HostApiResponse<System.Windows.Media.Imaging.BitmapSource>>>
                captureAsync,
            CancellationToken cancellationToken)
    {
        return await AsyncDeliveryBoundary.RunAsync(
            async () =>
            {
                var capture = await captureAsync();
                if (!capture.IsSuccess || capture.Data is null)
                {
                    throw new ScreenshotCaptureException(
                        capture.ErrorMessage ?? Text(
                            "toast.captureFailed",
                            "截图失败，请稍后重试"));
                }
                return capture.Data;
            },
            bitmap => _clipboardWriter.WriteAsync(
                bitmap,
                _host.Clipboard.SetImageAsync,
                cancellationToken),
            cancellationToken);
    }

    public FrameworkElement CreateSettingsUI()
    {
        var panel = new StackPanel();
        var full = new HotkeySettingsControl(
            _host.HotKey,
            Text("settings.fullTitle", "全屏截图"),
            Id,
            _registeredFullHotkey
                ?? Text("settings.commandCenter", "命令中心"),
            async value =>
            {
                var result = await _pluginSettings.SetAsync(
                    "full_hotkey",
                    value);
                if (!result.IsSuccess)
                    return result;
                ReplaceRegisteredHotkey(_registeredFullHotkey, value);
                _configuredFullHotkey = value;
                _registeredFullHotkey = value;
                return HostApiResponse.Success();
            },
            CreateSettingsLocalization(),
            CaptureFullScreen);
        var region = new HotkeySettingsControl(
            _host.HotKey,
            Text("settings.regionTitle", "区域截图"),
            Id,
            _registeredRegionHotkey
                ?? Text("settings.commandCenter", "命令中心"),
            async value =>
            {
                var result = await _pluginSettings.SetAsync(
                    "region_hotkey",
                    value);
                if (!result.IsSuccess)
                    return result;
                ReplaceRegisteredHotkey(_registeredRegionHotkey, value);
                _configuredRegionHotkey = value;
                _registeredRegionHotkey = value;
                return HostApiResponse.Success();
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
