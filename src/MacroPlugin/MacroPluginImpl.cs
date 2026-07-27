using System.Windows;
using System.Windows.Controls;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Views;
using Serilog;

namespace MacroPlugin;

public class MacroPluginImpl :
    ILongPlugin,
    IHasSettingsUI,
    IHasMainUI,
    IPluginCommandHandler,
    IPluginLanguageLifecycle
{
    private IHostApi? _host;
    private MacroEngine? _engine;
    private MacroOverlay? _overlay;
    private readonly List<string> _registeredHotkeys = new();
    private string _recordHotkey = "F6";
    private string _playHotkey = "F7";
    private string _loopHotkey = "F8";
    private readonly List<(
        WeakReference<HotkeySettingsControl> Reference,
        string LabelKey)> _settings = [];
    private IReadOnlyDictionary<string, string> _strings =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string Id => "com.long.macro";
    public string Name => Text("plugin.name", "宏录制器");
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
        return Text("settings.commandCenter", "命令中心");
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

    private void ToggleRecording() => TryToggleRecording();

    private bool TryToggleRecording()
    {
        if (_engine == null) return false;

        EnsureOverlay();

        if (_engine.State == MacroState.Recording)
        {
            _engine.StopRecording();
            _overlay?.SetIdle();
            Log.Information("[Macro] 录制停止，共 {Count} 个动作", _engine.ActionCount);
            return true;
        }
        else if (_engine.State == MacroState.Idle)
        {
            if (!_engine.StartRecording())
            {
                _overlay?.SetIdle();
                Log.Error("[Macro] 全局输入钩子安装失败，未进入录制状态");
                return false;
            }
            _overlay?.SetRecording(_engine.ActionCount);
            Log.Information("[Macro] 开始录制...");
            return true;
        }
        return false;
    }

    private async Task<bool> PlayOnce(
        CancellationToken cancellationToken = default)
    {
        if (_engine == null) return false;
        if (_engine.State != MacroState.Idle) return false;
        if (_engine.ActionCount == 0)
        {
            Log.Debug("[Macro] 无录制动作");
            return false;
        }

        EnsureOverlay();
        _overlay?.SetPlaying(false);
        var played = await _engine.PlayOnceAsync(cancellationToken);
        _overlay?.SetIdle();
        return played;
    }

    private void ToggleLoopPlay() => TryToggleLoopPlay();

    private bool TryToggleLoopPlay()
    {
        if (_engine == null) return false;
        if (_engine.State == MacroState.PlayingLoop)
        {
            _engine.StopPlay();
            _overlay?.SetIdle();
            Log.Information("[Macro] 循环播放停止");
            return true;
        }
        if (_engine.State != MacroState.Idle) return false;
        if (_engine.ActionCount == 0) return false;

        EnsureOverlay();
        _overlay?.SetPlaying(true);
        if (!_engine.PlayLoop())
        {
            _overlay?.SetIdle();
            return false;
        }
        Log.Information("[Macro] 循环播放开始...");
        return true;
    }

    private void EnsureOverlay()
    {
        if (_overlay == null || !_overlay.IsVisible)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _overlay?.Close();
                _overlay = MacroOverlay.ShowOverlay(CreateOverlayLocalization());
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
        var panel = new StackPanel();
        AddSettingsControl(panel, "settings.record", "录制", _recordHotkey);
        AddSettingsControl(panel, "settings.playOnce", "播放单次", _playHotkey);
        AddSettingsControl(panel, "settings.loop", "循环播放", _loopHotkey);
        return panel;
    }

    private void AddSettingsControl(
        Panel panel,
        string labelKey,
        string fallback,
        string hotkey)
    {
        var control = new HotkeySettingsControl(
            Text(labelKey, fallback),
            Id,
            hotkey,
            _ => { },
            CreateSettingsLocalization());
        _settings.RemoveAll(item => !item.Reference.TryGetTarget(out _));
        _settings.Add((new WeakReference<HotkeySettingsControl>(control), labelKey));
        panel.Children.Add(control);
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
                return TryToggleRecording()
                    ? PluginCommandResult.Success(
                        Text("result.recordToggled", "宏录制状态已切换"))
                    : PluginCommandResult.Failure(
                        Text("error.recordUnavailable", "无法切换宏录制状态"));
            case "macro.play-once":
                return await PlayOnce(cancellationToken)
                    ? PluginCommandResult.Success(
                        Text("result.playedOnce", "宏播放已执行"))
                    : PluginCommandResult.Failure(
                        Text("error.playUnavailable", "没有可播放的宏，或宏当前正忙"));
            case "macro.loop-toggle":
                return TryToggleLoopPlay()
                    ? PluginCommandResult.Success(
                        Text("result.loopToggled", "循环播放状态已切换"))
                    : PluginCommandResult.Failure(
                        Text("error.playUnavailable", "没有可播放的宏，或宏当前正忙"));
            default:
                return PluginCommandResult.Failure(
                    Text("error.unknownCommand", "未知宏命令"));
        }
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
            _overlay?.ApplyLocalization(CreateOverlayLocalization());
            _settings.RemoveAll(item => !item.Reference.TryGetTarget(out _));
            foreach (var item in _settings)
            {
                if (item.Reference.TryGetTarget(out var control))
                {
                    control.ApplyLocalization(
                        Text(item.LabelKey, item.LabelKey),
                        CreateSettingsLocalization());
                }
            }
        });
        return Task.CompletedTask;
    }

    private MacroOverlayLocalization CreateOverlayLocalization()
        => new(
            Text("overlay.recording", "录制"),
            Text("overlay.playing", "播放"),
            Text("overlay.looping", "循环"),
            Text("overlay.stopped", "停止"));

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
