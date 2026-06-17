using System.Windows;
using System.Windows.Controls;
using LongBetterWindows.Host.Core;
using Serilog;

namespace MacroPlugin;

public class MacroPluginImpl : ILongPlugin, IHasSettingsUI, IHasMainUI
{
    private IHostApi? _host;
    private MacroEngine? _engine;
    private MacroOverlay? _overlay;

    public string Id => "com.long.macro";
    public string Name => "宏录制器";
    public string Version => "1.0.0";
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

        var r1 = await hotKey.RegisterAsync("F6", () => ToggleRecording());
        var r2 = await hotKey.RegisterAsync("F7", async () => await PlayOnce());
        var r3 = await hotKey.RegisterAsync("F8", () => ToggleLoopPlay());

        if (!r1.IsSuccess || !r2.IsSuccess || !r3.IsSuccess)
        {
            Log.Error("[Macro] 热键注册失败");
            State = PluginState.Error;
            return false;
        }

        State = PluginState.Running;
        Log.Information("[Macro] 已启动: F6录制 F7播放 F8循环");
        return true;
    }

    public async Task<bool> StopAsync()
    {
        var hotKey = _host!.HotKey!;
        await hotKey.UnregisterAsync("F6");
        await hotKey.UnregisterAsync("F7");
        await hotKey.UnregisterAsync("F8");

        _engine?.StopPlay();
        _engine?.StopRecording();
        _engine?.Dispose();

        Application.Current.Dispatcher.Invoke(() => _overlay?.Close());
        State = PluginState.Disabled;
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
        panel.Children.Add(new LongBetterWindows.Host.Views.HotkeySettingsControl("录制", Id, "F6", _ => { }));
        panel.Children.Add(new LongBetterWindows.Host.Views.HotkeySettingsControl("播放单次", Id, "F7", _ => { }));
        panel.Children.Add(new LongBetterWindows.Host.Views.HotkeySettingsControl("循环播放", Id, "F8", _ => { }));
        return panel;
    }

    public void ShowMainUI()
    {
        EnsureOverlay();
        if (_overlay != null) _overlay.SetIdle();
    }
}
