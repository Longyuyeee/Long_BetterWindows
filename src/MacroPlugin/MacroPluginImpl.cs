using System.Windows;
using System.Windows.Controls;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using Serilog;

namespace MacroPlugin;

public class MacroPluginImpl : ILongPlugin, IHasSettingsUI, IHasMainUI, IPluginCommandHandler
{
    private IHostApi? _host;
    private MacroEngine? _engine;
    private MacroOverlay? _overlay;
    private readonly List<string> _registeredHotkeys = new();
    private string _recordHotkey = "F6";
    private string _playHotkey = "F7";
    private string _loopHotkey = "F8";

    public string Id => "com.long.macro";
    public string Name => "宏录制器";
    public string Version => "1.1.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;

        if (!host.HasCapability("system.hotkey"))
        {
            Log.Error("[Macro] 未获得热键能力授权");
            State = PluginState.Error;
            return Task.FromResult(false);
        }

        _engine = new MacroEngine();
        _engine.StateChanged += OnStateChanged;

        Log.Information("[Macro] 初始化完成");
        return Task.FromResult(true);
    }

    public async Task<bool> StartAsync()
    {
        var hotKey = _host!.HotKey!;

        _recordHotkey = await RegisterWithFallbackAsync(hotKey, "F6", "Ctrl+Alt+F6", ToggleRecording);
        _playHotkey = await RegisterWithFallbackAsync(hotKey, "F7", "Ctrl+Alt+F7", () => _ = PlayOnce());
        _loopHotkey = await RegisterWithFallbackAsync(hotKey, "F8", "Ctrl+Alt+F8", ToggleLoopPlay);

        State = PluginState.Running;
        Log.Information("[Macro] 已启动: {Record}录制 {Play}播放 {Loop}循环",
            _recordHotkey, _playHotkey, _loopHotkey);
        return true;
    }

    private async Task<string> RegisterWithFallbackAsync(
        IHotKeyService hotKey,
        string preferred,
        string fallback,
        Action callback)
    {
        var result = await hotKey.RegisterAsync(preferred, callback);
        if (result.IsSuccess)
        {
            _registeredHotkeys.Add(preferred);
            return preferred;
        }

        Log.Warning("[Macro] 热键 {Hotkey} 冲突，尝试 {Fallback}", preferred, fallback);
        result = await hotKey.RegisterAsync(fallback, callback);
        if (result.IsSuccess)
        {
            _registeredHotkeys.Add(fallback);
            return fallback;
        }

        Log.Warning("[Macro] 热键 {Preferred}/{Fallback} 均不可用，功能仍可从命令中心执行",
            preferred, fallback);
        return "命令中心";
    }

    public async Task<bool> StopAsync()
    {
        var hotKey = _host!.HotKey!;
        foreach (var hotkey in _registeredHotkeys)
            await hotKey.UnregisterAsync(hotkey);
        _registeredHotkeys.Clear();

        _engine?.StopPlay();
        _engine?.StopRecording();
        _engine?.Dispose();

        Application.Current.Dispatcher.Invoke(() => _overlay?.Close());
        State = PluginState.Stopped;
        return true;
    }

    private void ToggleRecording()
    {
        if (_engine == null) return;

        EnsureOverlay();

        if (_engine.State == MacroState.Recording)
        {
            _engine.StopRecording();
            _overlay?.SetIdle();
            Log.Information("[Macro] 录制停止，共 {Count} 个动作", _engine.ActionCount);
        }
        else if (_engine.State == MacroState.Idle)
        {
            _engine.StartRecording();
            _overlay?.SetRecording(_engine.ActionCount);
            Log.Information("[Macro] 开始录制...");
        }
    }

    private async Task PlayOnce()
    {
        if (_engine == null) return;
        if (_engine.State != MacroState.Idle) return;
        if (_engine.ActionCount == 0)
        {
            Log.Debug("[Macro] 无录制动作");
            return;
        }

        EnsureOverlay();
        _overlay?.SetPlaying(false);
        await _engine.PlayOnceAsync();
        _overlay?.SetIdle();
    }

    private void ToggleLoopPlay()
    {
        if (_engine == null) return;
        if (_engine.State == MacroState.PlayingLoop)
        {
            _engine.StopPlay();
            _overlay?.SetIdle();
            Log.Information("[Macro] 循环播放停止");
            return;
        }
        if (_engine.State != MacroState.Idle) return;
        if (_engine.ActionCount == 0) return;

        EnsureOverlay();
        _overlay?.SetPlaying(true);
        _engine.PlayLoop();
        Log.Information("[Macro] 循环播放开始...");
    }

    private void EnsureOverlay()
    {
        if (_overlay == null || !_overlay.IsVisible)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _overlay?.Close();
                _overlay = MacroOverlay.ShowOverlay();
            });
        }
    }

    private void OnStateChanged(MacroState state)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_overlay == null || !_overlay.IsVisible) return;

            switch (state)
            {
                case MacroState.Recording:
                    _overlay.SetRecording(_engine?.ActionCount ?? 0);
                    break;
                case MacroState.Playing:
                case MacroState.PlayingLoop:
                    _overlay.SetPlaying(state == MacroState.PlayingLoop);
                    break;
                case MacroState.Idle:
                    _overlay.SetIdle();
                    break;
            }
        });
    }

    public FrameworkElement CreateSettingsUI()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new LongBetterWindows.Host.Views.HotkeySettingsControl("录制", Id, _recordHotkey, _ => { }));
        panel.Children.Add(new LongBetterWindows.Host.Views.HotkeySettingsControl("播放单次", Id, _playHotkey, _ => { }));
        panel.Children.Add(new LongBetterWindows.Host.Views.HotkeySettingsControl("循环播放", Id, _loopHotkey, _ => { }));
        return panel;
    }

    public void ShowMainUI()
    {
        EnsureOverlay();
        if (_overlay != null) _overlay.SetIdle();
    }

    public async Task<PluginCommandResult> ExecuteCommandAsync(
        PluginCommandInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (invocation.CommandId)
        {
            case "macro.record-toggle":
                ToggleRecording();
                return PluginCommandResult.Success("宏录制状态已切换");
            case "macro.play-once":
                await PlayOnce();
                return PluginCommandResult.Success("宏播放已执行");
            case "macro.loop-toggle":
                ToggleLoopPlay();
                return PluginCommandResult.Success("循环播放状态已切换");
            default:
                return PluginCommandResult.Failure("未知宏命令");
        }
    }
}
