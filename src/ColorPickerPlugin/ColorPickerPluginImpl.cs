using System.Windows;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Views;
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
    private string _configuredHotkey = "Ctrl+Shift+P";
    private string? _registeredHotkey;
    private ColorPickerWindow? _window;
    private readonly List<WeakReference<HotkeySettingsControl>> _settings = [];
    private IReadOnlyDictionary<string, string> _strings =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string Id => "com.long.color-picker";
    public string Name => Text("plugin.name", "颜色拾取器");
    public string Version => "1.1.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;
        return Task.FromResult(true);
    }

    public async Task<bool> StartAsync()
    {
        _registeredHotkey = await TryRegisterAsync(_configuredHotkey);
        if (_registeredHotkey == null)
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
        if (_registeredHotkey != null)
            await _host!.HotKey.UnregisterAsync(_registeredHotkey);
        _registeredHotkey = null;
        Application.Current.Dispatcher.Invoke(() => _window?.Close());
        State = PluginState.Stopped;
        return true;
    }

    private void OnPickColor()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_window?.IsVisible == true)
            {
                _window.Activate();
                return;
            }

            _window = new ColorPickerWindow(
                async hex =>
                {
                    var result = await _host!.Clipboard.SetTextAsync(hex);
                    if (result.IsSuccess)
                    {
                        FloatingHudWindow.ShowToast(string.Format(
                            Text("toast.copied", "已复制颜色 {0}"),
                            hex));
                    }
                    else
                    {
                        Log.Warning("[ColorPicker] 颜色复制失败: {Error}", result.ErrorMessage);
                        FloatingHudWindow.ShowToast(Text(
                            "toast.copyFailed",
                            "拾取成功，但写入剪贴板失败"));
                    }
                },
                CreateWindowLocalization());
            _window.Closed += (_, _) => _window = null;
            _window.Show();
            _window.Activate();
        });
    }

    public FrameworkElement CreateSettingsUI()
    {
        var control = new HotkeySettingsControl(
            Name,
            Id,
            _registeredHotkey
                ?? Text("settings.commandCenter", "命令中心"),
            hotkey =>
            {
                _configuredHotkey = hotkey;
                _registeredHotkey = hotkey;
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
