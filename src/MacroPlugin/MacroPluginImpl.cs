using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.PluginSdk.Wpf;
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
    private IPluginSettingsService _pluginSettings = null!;
    private INotificationService _notification = null!;
    private MacroEngine? _engine;
    private MacroOverlay? _overlay;
    private readonly List<string> _registeredHotkeys = new();
    private string _configuredRecordHotkey = "F6";
    private string _configuredPlayHotkey = "F7";
    private string _configuredLoopHotkey = "F8";
    private int _configuredLoopIntervalMs =
        MacroLoopIntervalPolicy.DefaultMilliseconds;
    private string? _registeredRecordHotkey;
    private string? _registeredPlayHotkey;
    private string? _registeredLoopHotkey;
    private readonly List<(
        WeakReference<HotkeySettingsControl> Reference,
        string LabelKey)> _settings = [];
    private readonly List<WeakReference<MacroLoopIntervalSettingsControl>>
        _loopIntervalSettings = [];
    private readonly SemaphoreSlim _loopIntervalChangeGate = new(1, 1);
    private IReadOnlyDictionary<string, string> _strings =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string Id => "com.long.macro";
    public string Name => Text("plugin.name", "宏录制器");
    public string Version => "1.1.9";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public async Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;
        _pluginSettings = host.Settings;
        _notification = host.Notification;

        if (!host.HasCapability("system.hotkey"))
        {
            Log.Error("[Macro] 未获得热键能力授权");
            State = PluginState.Error;
            return false;
        }

        _configuredRecordHotkey = await ReadHotkeyAsync(
            "record_hotkey",
            _configuredRecordHotkey);
        _configuredPlayHotkey = await ReadHotkeyAsync(
            "play_once_hotkey",
            _configuredPlayHotkey);
        _configuredLoopHotkey = await ReadHotkeyAsync(
            "play_loop_hotkey",
            _configuredLoopHotkey);
        _configuredLoopIntervalMs = await ReadLoopIntervalAsync();
        EnsureEngine();

        Log.Information("[Macro] 初始化完成");
        return true;
    }

    public async Task<bool> StartAsync()
    {
        EnsureEngine();
        var hotKey = _host!.HotKey!;

        _registeredRecordHotkey = await RegisterWithFallbackAsync(
            hotKey,
            _configuredRecordHotkey,
            "Ctrl+Alt+F6",
            ToggleRecording);
        _registeredPlayHotkey = await RegisterWithFallbackAsync(
            hotKey,
            _configuredPlayHotkey,
            "Ctrl+Alt+F7",
            () => Observe(PlayOnce()));
        _registeredLoopHotkey = await RegisterWithFallbackAsync(
            hotKey,
            _configuredLoopHotkey,
            "Ctrl+Alt+F8",
            () => Observe(TryToggleLoopPlayAsync()));

        State = PluginState.Running;
        Log.Information("[Macro] 已启动: {Record}录制 {Play}播放 {Loop}循环",
            _registeredRecordHotkey ?? "command-center",
            _registeredPlayHotkey ?? "command-center",
            _registeredLoopHotkey ?? "command-center");
        return true;
    }

    private void EnsureEngine()
    {
        if (_engine is not null)
            return;

        _engine = new MacroEngine();
        _engine.SetLoopInterval(
            TimeSpan.FromMilliseconds(_configuredLoopIntervalMs));
        _engine.StateChanged += OnStateChanged;
        _engine.PlaybackFailed += OnPlaybackFailed;
    }

    private async Task<string?> RegisterWithFallbackAsync(
        IHotKeyService hotKey,
        string preferred,
        string fallback,
        Action callback)
    {
        var result = await hotKey.RegisterAsync(preferred, Id, callback);
        if (result.IsSuccess)
        {
            _registeredHotkeys.Add(preferred);
            return preferred;
        }

        Log.Warning("[Macro] 热键 {Hotkey} 冲突，尝试 {Fallback}", preferred, fallback);
        if (!preferred.Equals(fallback, StringComparison.OrdinalIgnoreCase))
        {
            result = await hotKey.RegisterAsync(fallback, Id, callback);
            if (result.IsSuccess)
            {
                _registeredHotkeys.Add(fallback);
                return fallback;
            }
        }

        Log.Warning("[Macro] 热键 {Preferred}/{Fallback} 均不可用，功能仍可从命令中心执行",
            preferred, fallback);
        return null;
    }

    public async Task<bool> StopAsync()
    {
        var hotKey = _host!.HotKey!;
        var unregisterFailures = new List<string>();
        foreach (var hotkey in _registeredHotkeys.ToArray())
        {
            var result = await hotKey.UnregisterAsync(hotkey);
            if (result.IsSuccess)
            {
                _registeredHotkeys.RemoveAll(existing =>
                    existing.Equals(
                        hotkey,
                        StringComparison.OrdinalIgnoreCase));
                continue;
            }

            unregisterFailures.Add(
                $"{hotkey}: {result.ErrorMessage ?? result.ErrorCode.ToString()}");
        }
        ClearReleasedHotkeyReferences();

        var engineStopped = true;
        if (_engine is not null)
        {
            engineStopped = await _engine.StopAsync();
            if (!engineStopped)
            {
                Log.Error(
                    "[Macro] 停止失败，仍有 Hook 或输入清理未完成: {Error}",
                    _engine.LastError);
            }
            else
            {
                _engine.StateChanged -= OnStateChanged;
                _engine.PlaybackFailed -= OnPlaybackFailed;
                await _engine.DisposeAsync();
                _engine = null;
            }
        }

        if (engineStopped)
        {
            var application = Application.Current;
            if (application is not null)
            {
                application.Dispatcher.Invoke(() =>
                {
                    _overlay?.Close();
                    _overlay = null;
                });
            }
        }

        if (unregisterFailures.Count > 0)
        {
            Log.Error(
                "[Macro] 停止失败，仍有热键未注销: {Failures}",
                string.Join("; ", unregisterFailures));
        }
        if (!engineStopped || unregisterFailures.Count > 0)
        {
            State = PluginState.Error;
            return false;
        }

        State = PluginState.Stopped;
        return true;
    }

    private void ClearReleasedHotkeyReferences()
    {
        if (_registeredRecordHotkey is not null
            && !_registeredHotkeys.Contains(
                _registeredRecordHotkey,
                StringComparer.OrdinalIgnoreCase))
        {
            _registeredRecordHotkey = null;
        }
        if (_registeredPlayHotkey is not null
            && !_registeredHotkeys.Contains(
                _registeredPlayHotkey,
                StringComparer.OrdinalIgnoreCase))
        {
            _registeredPlayHotkey = null;
        }
        if (_registeredLoopHotkey is not null
            && !_registeredHotkeys.Contains(
                _registeredLoopHotkey,
                StringComparer.OrdinalIgnoreCase))
        {
            _registeredLoopHotkey = null;
        }
    }

    private void ToggleRecording()
        => TryToggleRecording(discardTrailingPressedKeys: true);

    private bool TryToggleRecording(bool discardTrailingPressedKeys = false)
    {
        if (_engine == null) return false;

        EnsureOverlay();

        if (_engine.State == MacroState.Recording)
        {
            if (!_engine.StopRecording(discardTrailingPressedKeys))
            {
                Log.Error(
                    "[Macro] 录制 Hook 清理失败: {Error}",
                    _engine.LastError);
                return false;
            }
            _overlay?.SetIdle();
            Log.Information("[Macro] 录制停止，共 {Count} 个动作", _engine.ActionCount);
            return true;
        }
        else if (_engine.State == MacroState.Idle)
        {
            if (!_engine.StartRecording())
            {
                _overlay?.SetIdle();
                Log.Error(
                    "[Macro] 全局输入钩子安装失败，未进入录制状态: {Error}",
                    _engine.LastError);
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
        try
        {
            var played = await _engine.PlayOnceAsync(cancellationToken);
            if (!played && _engine.LastError is not null)
            {
                Log.Error(
                    "[Macro] 单次播放失败: {Error}",
                    _engine.LastError);
            }
            return played;
        }
        finally
        {
            _overlay?.SetIdle();
        }
    }

    private async Task<bool> TryToggleLoopPlayAsync()
    {
        if (_engine == null) return false;
        if (_engine.State == MacroState.PlayingLoop)
        {
            var stopped = await _engine.StopPlayAsync();
            _overlay?.SetIdle();
            if (stopped)
                Log.Information("[Macro] 循环播放停止");
            else
                Log.Error(
                    "[Macro] 循环播放停止失败: {Error}",
                    _engine.LastError);
            return stopped;
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
        AddSettingsControl(
            panel,
            "settings.record",
            "录制",
            "record_hotkey",
            _registeredRecordHotkey,
            ToggleRecording,
            value =>
            {
                ReplaceRegisteredHotkey(_registeredRecordHotkey, value);
                _configuredRecordHotkey = value;
                _registeredRecordHotkey = value;
            });
        AddSettingsControl(
            panel,
            "settings.playOnce",
            "播放单次",
            "play_once_hotkey",
            _registeredPlayHotkey,
            () => _ = PlayOnce(),
            value =>
            {
                ReplaceRegisteredHotkey(_registeredPlayHotkey, value);
                _configuredPlayHotkey = value;
                _registeredPlayHotkey = value;
            });
        AddSettingsControl(
            panel,
            "settings.loop",
            "循环播放",
            "play_loop_hotkey",
            _registeredLoopHotkey,
            () => Observe(TryToggleLoopPlayAsync()),
            value =>
            {
                ReplaceRegisteredHotkey(_registeredLoopHotkey, value);
                _configuredLoopHotkey = value;
                _registeredLoopHotkey = value;
            });

        var intervalControl = new MacroLoopIntervalSettingsControl(
            _configuredLoopIntervalMs,
            ApplyLoopIntervalAsync,
            CreateLoopIntervalLocalization());
        _loopIntervalSettings.RemoveAll(
            reference => !reference.TryGetTarget(out _));
        _loopIntervalSettings.Add(
            new WeakReference<MacroLoopIntervalSettingsControl>(
                intervalControl));
        panel.Children.Add(intervalControl);
        return panel;
    }

    private void AddSettingsControl(
        Panel panel,
        string labelKey,
        string fallback,
        string settingKey,
        string? registeredHotkey,
        Action hotkeyCallback,
        Action<string> commit)
    {
        var control = new HotkeySettingsControl(
            _host!.HotKey,
            Text(labelKey, fallback),
            Id,
            registeredHotkey
                ?? Text("settings.commandCenter", "命令中心"),
            async value =>
            {
                var result = await _pluginSettings.SetAsync(
                    settingKey,
                    value);
                if (result.IsSuccess)
                    commit(value);
                return result;
            },
            CreateSettingsLocalization(),
            hotkeyCallback);
        _settings.RemoveAll(item => !item.Reference.TryGetTarget(out _));
        _settings.Add((new WeakReference<HotkeySettingsControl>(control), labelKey));
        panel.Children.Add(control);
    }

    private async Task<string> ReadHotkeyAsync(
        string key,
        string fallback)
    {
        var result = await _pluginSettings.GetAsync(key);
        return result.IsSuccess && !string.IsNullOrWhiteSpace(result.Data)
            ? result.Data
            : fallback;
    }

    private async Task<int> ReadLoopIntervalAsync()
    {
        var result = await _pluginSettings.GetAsync("loop_interval");
        if (result.IsSuccess
            && MacroLoopIntervalPolicy.TryParse(
                result.Data,
                out var milliseconds))
        {
            return milliseconds;
        }

        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Data))
        {
            Log.Warning(
                "[Macro] Invalid loop interval {Interval}; using {Default} ms",
                result.Data,
                MacroLoopIntervalPolicy.DefaultMilliseconds);
        }
        return MacroLoopIntervalPolicy.DefaultMilliseconds;
    }

    internal async Task<HostApiResponse> ApplyLoopIntervalAsync(
        int milliseconds)
    {
        if (!MacroLoopIntervalPolicy.IsValid(milliseconds))
        {
            return HostApiResponse.Failure(
                ApiErrorCode.InvalidArgument,
                string.Format(
                    CultureInfo.CurrentCulture,
                    Text(
                        "settings.loopIntervalInvalid",
                        "Enter an integer from {0} to {1}."),
                    MacroLoopIntervalPolicy.MinimumMilliseconds,
                    MacroLoopIntervalPolicy.MaximumMilliseconds));
        }

        await _loopIntervalChangeGate.WaitAsync();
        try
        {
            HostApiResponse result;
            try
            {
                result = await _pluginSettings.SetAsync(
                    "loop_interval",
                    milliseconds.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception exception)
            {
                return HostApiResponse.Failure(
                    ApiErrorCode.Unknown,
                    exception.Message);
            }

            if (!result.IsSuccess)
                return result;

            _configuredLoopIntervalMs = milliseconds;
            _engine?.SetLoopInterval(
                TimeSpan.FromMilliseconds(milliseconds));
            return HostApiResponse.Success();
        }
        finally
        {
            _loopIntervalChangeGate.Release();
        }
    }

    private void ReplaceRegisteredHotkey(string? previous, string current)
    {
        if (previous is not null)
        {
            _registeredHotkeys.RemoveAll(existing =>
                existing.Equals(previous, StringComparison.OrdinalIgnoreCase));
        }
        if (!_registeredHotkeys.Contains(
                current,
                StringComparer.OrdinalIgnoreCase))
        {
            _registeredHotkeys.Add(current);
        }
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
                return await TryToggleLoopPlayAsync()
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

            _loopIntervalSettings.RemoveAll(
                reference => !reference.TryGetTarget(out _));
            foreach (var reference in _loopIntervalSettings)
            {
                if (reference.TryGetTarget(out var intervalControl))
                {
                    intervalControl.ApplyLocalization(
                        CreateLoopIntervalLocalization());
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

    private MacroLoopIntervalLocalization CreateLoopIntervalLocalization()
        => new(
            Text("settings.loopInterval", "Loop interval"),
            Text(
                "settings.loopIntervalDescription",
                "Pause between completed loop playback cycles."),
            Text("settings.milliseconds", "ms"),
            Text("settings.apply", "Apply"),
            Text("settings.unchanged", "No changes"),
            Text("settings.updated", "Updated"),
            Text(
                "settings.loopIntervalInvalid",
                "Enter an integer from {0} to {1}."),
            Text("settings.changeFailed", "Update failed: {0}"),
            Text(
                "settings.loopIntervalHint",
                "Range: 50-10000 ms; applies before the next cycle."));

    private string Text(string key, string fallback)
        => _strings.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;

    private void OnPlaybackFailed(string message)
    {
        Log.Error("[Macro] 播放失败并已执行输入释放: {Error}", message);
        var application = Application.Current;
        if (application is null)
            return;
        application.Dispatcher.BeginInvoke(() =>
        {
            _overlay?.SetIdle();
            _ = _notification.ShowAsync(Name, string.Format(
                Text("error.playFailed", "宏播放失败：{0}"),
                message));
        });
    }

    private static void Observe(Task task)
        => _ = ObserveCoreAsync(task);

    private static async Task ObserveCoreAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[Macro] 后台操作异常");
        }
    }
}
