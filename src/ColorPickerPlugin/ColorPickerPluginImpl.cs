using System.Windows;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.PluginSdk.Wpf;
using Serilog;

namespace ColorPickerPlugin;

public class ColorPickerPluginImpl :
    ILongPlugin,
    IHasSettingsUI,
    IHasMainUI,
    IPluginCommandHandler,
    IPluginLanguageLifecycle
{
    private IHostApi? _host;
    private IPluginSettingsService _pluginSettings = null!;
    private INotificationService _notification = null!;
    private IScreenColorSampler _screenColorSampler = null!;
    private string _configuredHotkey = "Ctrl+Shift+P";
    private string? _registeredHotkey;
    private ColorPickerWindow? _window;
    private CancellationTokenSource _operationLifetime = new();
    private readonly List<WeakReference<HotkeySettingsControl>> _settings = [];
    private IReadOnlyDictionary<string, string> _strings =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string Id => "com.long.color-picker";
    public string Name => Text("plugin.name", "颜色拾取器");
    public string Version => "1.2.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public async Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;
        _pluginSettings = host.Settings;
        _notification = host.Notification;
        _screenColorSampler = host.ScreenColorSampler;
        var configured = await _pluginSettings.GetAsync("hotkey");
        if (configured.IsSuccess && !string.IsNullOrWhiteSpace(configured.Data))
            _configuredHotkey = configured.Data;
        return true;
    }

    public async Task<bool> StartAsync()
    {
        if (_operationLifetime.IsCancellationRequested)
        {
            _operationLifetime.Dispose();
            _operationLifetime = new CancellationTokenSource();
        }
        _registeredHotkey = await TryRegisterAsync(_configuredHotkey);
        if (_registeredHotkey == null
            && !_configuredHotkey.Equals(
                "Ctrl+Alt+P",
                StringComparison.OrdinalIgnoreCase))
            _registeredHotkey = await TryRegisterAsync("Ctrl+Alt+P");

        if (_registeredHotkey == null)
            Log.Warning("[ColorPicker] 热键均冲突，功能仍可从命令中心执行");

        State = PluginState.Running;
        return true;
    }

    private async Task<string?> TryRegisterAsync(string hotkey)
    {
        var result = await _host!.HotKey.RegisterAsync(hotkey, Id, OnPickColor);
        if (!result.IsSuccess)
        {
            Log.Warning("[ColorPicker] 热键 {Hotkey} 注册失败", hotkey);
            return null;
        }

        Log.Information("[ColorPicker] 热键已注册: {Hotkey}", hotkey);
        return hotkey;
    }

    public async Task<bool> StopAsync()
    {
        _operationLifetime.Cancel();
        if (_registeredHotkey != null)
            await _host!.HotKey.UnregisterAsync(_registeredHotkey);
        _registeredHotkey = null;
        var application = Application.Current;
        if (application is not null)
            application.Dispatcher.Invoke(() => _window?.Close());
        _window = null;
        State = PluginState.Stopped;
        return true;
    }

    private void OnPickColor()
    {
        if (_operationLifetime.IsCancellationRequested)
            return;
        var application = Application.Current;
        if (application is null)
            return;

        application.Dispatcher.Invoke(() =>
        {
            if (_window is not null)
            {
                if (_window.IsVisible)
                    _window.Activate();
                return;
            }

            var operationToken = _operationLifetime.Token;
            var delivery = new ColorPickerDeliveryCoordinator();
            var clipboardWriter = new ColorPickerClipboardWriter();
            ColorPickerWindow? window = null;
            window = new ColorPickerWindow(
                _screenColorSampler,
                async hex =>
                {
                    try
                    {
                        var delivered = await delivery.TryDeliverAsync(
                            hex,
                            value => clipboardWriter.WriteAsync(
                                value,
                                _host!.Clipboard.SetTextAsync,
                                operationToken),
                            operationToken);
                        if (!delivered)
                            return;
                        operationToken.ThrowIfCancellationRequested();
                        _ = _notification.ShowAsync(Name, string.Format(
                            Text("toast.copied", "已复制颜色 {0}"),
                            hex));
                    }
                    catch (OperationCanceledException)
                        when (operationToken.IsCancellationRequested)
                    {
                        Log.Information(
                            "[ColorPicker] Cancelled pending clipboard delivery");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[ColorPicker] 颜色复制失败");
                        _ = _notification.ShowAsync(Name, Text(
                            "toast.copyFailed",
                            "拾取成功，但写入剪贴板失败"));
                    }
                    finally
                    {
                        application.Dispatcher.Invoke(() =>
                        {
                            if (ReferenceEquals(_window, window))
                                _window = null;
                        });
                    }
                },
                CreateWindowLocalization());
            _window = window;
            window.CaptureFailed += error =>
            {
                Log.Warning("[ColorPicker] 取色会话失败: {Error}", error);
                if (!operationToken.IsCancellationRequested)
                {
                    _ = _notification.ShowAsync(Name, Text(
                        "toast.captureFailed",
                        "无法读取当前桌面颜色"));
                }
            };
            window.Closed += (_, _) =>
            {
                if (!window.HasCommittedSelection)
                    delivery.Cancel();
                if (!window.HasCommittedSelection
                    && ReferenceEquals(_window, window))
                    _window = null;
            };
            window.Show();
            window.Activate();
        });
    }

    public FrameworkElement CreateSettingsUI()
    {
        var control = new HotkeySettingsControl(
            _host!.HotKey,
            Name,
            Id,
            _registeredHotkey
                ?? Text("settings.commandCenter", "命令中心"),
            async hotkey =>
            {
                var result = await _pluginSettings.SetAsync("hotkey", hotkey);
                if (!result.IsSuccess)
                    return result;
                _configuredHotkey = hotkey;
                _registeredHotkey = hotkey;
                return HostApiResponse.Success();
            },
            CreateSettingsLocalization(),
            OnPickColor);
        _settings.RemoveAll(reference => !reference.TryGetTarget(out _));
        _settings.Add(new WeakReference<HotkeySettingsControl>(control));
        return control;
    }

    public void ShowMainUI() => OnPickColor();

    public Task<PluginCommandResult> ExecuteCommandAsync(
        PluginCommandInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _operationLifetime.Token.ThrowIfCancellationRequested();
        if (invocation.CommandId != "color.pick")
        {
            return Task.FromResult(PluginCommandResult.Failure(string.Format(
                Text("error.unknownCommand", "未知取色命令: {0}"),
                invocation.CommandId)));
        }
        OnPickColor();
        return Task.FromResult(PluginCommandResult.Success(
            Text("command.opened", "取色器已打开")));
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
            _window?.ApplyLocalization(CreateWindowLocalization());
            _settings.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (var reference in _settings)
            {
                if (reference.TryGetTarget(out var control))
                    control.ApplyLocalization(Name, CreateSettingsLocalization());
            }
        });
        return Task.CompletedTask;
    }

    private ColorPickerWindowLocalization CreateWindowLocalization()
        => new(
            Text("window.title", "屏幕取色"),
            Text("window.automationName", "屏幕颜色拾取器"),
            Text("window.instruction", "单击屏幕复制色值 · Esc 取消"));

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
