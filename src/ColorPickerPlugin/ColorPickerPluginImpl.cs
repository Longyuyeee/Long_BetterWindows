using System.Windows;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using Serilog;

namespace ColorPickerPlugin;

public class ColorPickerPluginImpl : ILongPlugin, IHasSettingsUI, IHasMainUI, IPluginCommandHandler
{
    private IHostApi? _host;
    private string _configuredHotkey = "Ctrl+Shift+P";
    private string? _registeredHotkey;
    private ColorPickerWindow? _window;

    public string Id => "com.long.color-picker";
    public string Name => "颜色拾取器";
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

            _window = new ColorPickerWindow(async hex =>
            {
                var result = await _host!.Clipboard.SetTextAsync(hex);
                if (!result.IsSuccess)
                    Log.Warning("[ColorPicker] 颜色复制失败: {Error}", result.ErrorMessage);
            });
            _window.Closed += (_, _) => _window = null;
            _window.Show();
            _window.Activate();
        });
    }

    public FrameworkElement CreateSettingsUI()
    {
        return new LongBetterWindows.Host.Views.HotkeySettingsControl(
            "颜色拾取器", Id, _registeredHotkey ?? _configuredHotkey,
            hotkey => _configuredHotkey = hotkey);
    }

    public void ShowMainUI() => OnPickColor();

    public Task<PluginCommandResult> ExecuteCommandAsync(
        PluginCommandInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OnPickColor();
        return Task.FromResult(PluginCommandResult.Success("取色器已打开"));
    }
}
