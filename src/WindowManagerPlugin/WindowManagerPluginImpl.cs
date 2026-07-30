using System.Windows;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.PluginSdk.Wpf;
using Serilog;

namespace WindowManagerPlugin;

public class WindowManagerPluginImpl :
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
    private readonly List<WeakReference<HotkeySettingsControl>> _settings = [];
    private WindowManagerGuide? _guide;
    private string _configuredTopmostHotkey = "Ctrl+Alt+T";
    private string? _registeredTopmostHotkey;
    private IReadOnlyDictionary<string, string> _strings =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string Id => "com.long.window-manager";
    public string Name => Text("plugin.name", "窗口管理");
    public string Version => "2.1.4";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public async Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;
        _pluginSettings = host.Settings;
        _notification = host.Notification;
        var configured = await _pluginSettings.GetAsync("topmost_hotkey");
        if (configured.IsSuccess && !string.IsNullOrWhiteSpace(configured.Data))
            _configuredTopmostHotkey = configured.Data;
        return true;
    }

    public async Task<bool> StartAsync()
    {
        var bindings = new (string Key, Action Callback)[]
        {
            (_configuredTopmostHotkey, ToggleTopmost),
            ("Ctrl+Alt+Left", () => ApplyLayout(WindowLayout.Left, "left")),
            ("Ctrl+Alt+Right", () => ApplyLayout(WindowLayout.Right, "right")),
            ("Ctrl+Alt+Up", () => ApplyLayout(WindowLayout.Maximize, "max")),
            ("Ctrl+Alt+Down", () => ApplyLayout(WindowLayout.Bottom, "bottom")),
            ("Ctrl+Alt+1", () => ApplyLayout(WindowLayout.TopLeft, "top-left")),
            ("Ctrl+Alt+2", () => ApplyLayout(WindowLayout.TopRight, "top-right")),
            ("Ctrl+Alt+3", () => ApplyLayout(WindowLayout.BottomLeft, "bottom-left")),
            ("Ctrl+Alt+4", () => ApplyLayout(WindowLayout.BottomRight, "bottom-right")),
            ("Ctrl+Alt+Shift+Left", () => ApplyLayout(WindowLayout.ThirdLeft, "third-left")),
            ("Ctrl+Alt+Shift+Right", () => ApplyLayout(WindowLayout.ThirdRight, "third-right")),
        };

        foreach (var binding in bindings)
        {
            var result = await _host.HotKey.RegisterAsync(binding.Key, Id, binding.Callback);
            if (result.IsSuccess)
            {
                _registeredHotkeys.Add(binding.Key);
                if (binding.Key.Equals(
                        _configuredTopmostHotkey,
                        StringComparison.OrdinalIgnoreCase))
                    _registeredTopmostHotkey = binding.Key;
            }
            else
                Log.Warning("[WindowManager] 热键冲突，命令入口仍可用: {Hotkey}", binding.Key);
        }

        State = PluginState.Running;
        Log.Information("[WindowManager] 已启动，注册 {Count}/{Total} 个热键", _registeredHotkeys.Count, bindings.Length);
        return true;
    }

    public async Task<bool> StopAsync()
    {
        var failures = new List<string>();
        foreach (var key in _registeredHotkeys.ToArray())
        {
            var result = await _host.HotKey.UnregisterAsync(key);
            if (result.IsSuccess)
            {
                _registeredHotkeys.RemoveAll(existing =>
                    existing.Equals(key, StringComparison.OrdinalIgnoreCase));
                continue;
            }

            failures.Add($"{key}: {result.ErrorMessage ?? result.ErrorCode.ToString()}");
        }

        if (_registeredTopmostHotkey is not null
            && !_registeredHotkeys.Contains(
                _registeredTopmostHotkey,
                StringComparer.OrdinalIgnoreCase))
        {
            _registeredTopmostHotkey = null;
        }

        var application = Application.Current;
        if (application is not null)
            application.Dispatcher.Invoke(() => _guide?.Close());

        if (failures.Count > 0)
        {
            State = PluginState.Error;
            Log.Error(
                "[WindowManager] 停止失败，仍有热键未注销: {Failures}",
                string.Join("; ", failures));
            return false;
        }

        State = PluginState.Stopped;
        return true;
    }

    public void ShowMainUI()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_guide is { IsVisible: true })
            {
                _guide.Activate();
                return;
            }

            _guide = new WindowManagerGuide(CreateGuideLocalization());
            _guide.Closed += (_, _) => _guide = null;
            _guide.Show();
        });
    }

    public FrameworkElement CreateSettingsUI()
    {
        var control = new HotkeySettingsControl(
            _host.HotKey,
            Text("settings.topmostTitle", "窗口置顶"),
            Id,
            _registeredTopmostHotkey
                ?? Text("settings.commandCenter", "命令中心"),
            async value =>
            {
                var result = await _pluginSettings.SetAsync(
                    "topmost_hotkey",
                    value);
                if (!result.IsSuccess)
                    return result;
                ReplaceRegisteredHotkey(_registeredTopmostHotkey, value);
                _configuredTopmostHotkey = value;
                _registeredTopmostHotkey = value;
                return HostApiResponse.Success();
            },
            CreateSettingsLocalization(),
            ToggleTopmost);
        _settings.RemoveAll(reference => !reference.TryGetTarget(out _));
        _settings.Add(new WeakReference<HotkeySettingsControl>(control));
        return control;
    }

    public Task<PluginCommandResult> ExecuteCommandAsync(
        PluginCommandInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (invocation.CommandId == "window.guide")
        {
            ShowMainUI();
            return Task.FromResult(PluginCommandResult.Success(
                Text("command.completed", "窗口布局已应用")));
        }

        HostApiResponse<WindowOperationOutcome>? operation = invocation.CommandId switch
        {
            "window.topmost" => _host.WindowInfo.ToggleForegroundTopmost(),
            "window.left" => _host.WindowInfo.ApplyForegroundLayout(WindowLayout.Left),
            "window.right" => _host.WindowInfo.ApplyForegroundLayout(WindowLayout.Right),
            "window.maximize" => _host.WindowInfo.ApplyForegroundLayout(WindowLayout.Maximize),
            "window.bottom" => _host.WindowInfo.ApplyForegroundLayout(WindowLayout.Bottom),
            "window.top-left" => _host.WindowInfo.ApplyForegroundLayout(WindowLayout.TopLeft),
            "window.top-right" => _host.WindowInfo.ApplyForegroundLayout(WindowLayout.TopRight),
            "window.bottom-left" => _host.WindowInfo.ApplyForegroundLayout(WindowLayout.BottomLeft),
            "window.bottom-right" => _host.WindowInfo.ApplyForegroundLayout(WindowLayout.BottomRight),
            "window.third-left" => _host.WindowInfo.ApplyForegroundLayout(WindowLayout.ThirdLeft),
            "window.third-right" => _host.WindowInfo.ApplyForegroundLayout(WindowLayout.ThirdRight),
            _ => null,
        };

        if (operation is null)
        {
            return Task.FromResult(PluginCommandResult.Failure(string.Format(
                Text("error.unknownCommand", "未知窗口命令: {0}"),
                invocation.CommandId)));
        }
        if (!operation.IsSuccess)
            return Task.FromResult(PluginCommandResult.Failure(
                FormatOperationFailure(operation)));

        return Task.FromResult(PluginCommandResult.Success(
            Text("command.completed", "窗口布局已应用")));
    }

    private void ToggleTopmost()
    {
        var result = _host.WindowInfo.ToggleForegroundTopmost();
        if (!result.IsSuccess)
        {
            _ = _notification.ShowAsync(Name, FormatOperationFailure(result));
            return;
        }

        _ = _notification.ShowAsync(Name, result.Data?.After?.IsTopmost == true
            ? Text("toast.topmostEnabled", "窗口已置顶")
            : Text("toast.topmostDisabled", "已取消窗口置顶"));
    }

    private void ApplyLayout(WindowLayout layout, string localizationKey)
    {
        var result = _host.WindowInfo.ApplyForegroundLayout(layout);
        _ = _notification.ShowAsync(Name, result.IsSuccess
            ? LayoutName(localizationKey)
            : FormatOperationFailure(result));
    }

    private string FormatOperationFailure(
        HostApiResponse<WindowOperationOutcome> result)
    {
        var detail = result.ErrorMessage ?? result.ErrorCode.ToString();
        var message = string.Format(
            Text("error.operationFailed", "窗口操作失败：{0}"),
            detail);
        if (result.Data is
            {
                RecoveryAttempted: true,
                RecoverySucceeded: true,
            })
        {
            return message + Text(
                "error.recovered",
                "；已恢复原窗口状态");
        }
        if (result.Data is
            {
                RecoveryAttempted: true,
                RecoverySucceeded: false,
            } outcome)
        {
            return message + string.Format(
                Text("error.recoveryFailed", "；恢复原状态失败：{0}"),
                outcome.RecoveryErrorMessage
                    ?? outcome.RecoveryErrorCode.ToString());
        }
        return message;
    }

    public Task OnLanguageChangedAsync(
        PluginLanguageContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _strings = context.Resources;
        var application = Application.Current;
        if (application is not null)
        {
            application.Dispatcher.Invoke(() =>
            {
                _guide?.ApplyLocalization(CreateGuideLocalization());
                _settings.RemoveAll(reference => !reference.TryGetTarget(out _));
                foreach (var reference in _settings)
                {
                    if (reference.TryGetTarget(out var control))
                    {
                        control.ApplyLocalization(
                            Text("settings.topmostTitle", "窗口置顶"),
                            CreateSettingsLocalization());
                    }
                }
            });
        }
        return Task.CompletedTask;
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

    private WindowManagerGuideLocalization CreateGuideLocalization()
        => new(
            Text("guide.title", "窗口管理"),
            Text("guide.description", "用统一指令或快捷键整理当前窗口"),
            Text("guide.closeAutomationName", "关闭窗口管理指南"),
            Text("guide.common", "常用操作"),
            Text("guide.topmost", "切换置顶"),
            Text("guide.maximize", "最大化"),
            Text("guide.layouts", "半屏与分区"),
            Text("guide.left", "←  左半屏"),
            Text("guide.right", "→  右半屏"),
            Text("guide.bottom", "↓  下半屏"),
            Text("guide.topLeft", "1  左上四分屏"),
            Text("guide.topRight", "2  右上四分屏"),
            Text("guide.bottomLeft", "3  左下四分屏"),
            Text("guide.bottomRight", "4  右下四分屏"),
            Text("guide.thirdLeft", "Shift + ←  左侧 ⅓"),
            Text("guide.thirdRight", "Shift + →  右侧 ⅔"),
            Text(
                "guide.hint",
                "提示：所有操作也可以直接在命令中心搜索“窗口”。"));

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

    private string LayoutName(string layout)
        => Text("layout." + layout, layout);

    private string Text(string key, string fallback)
        => _strings.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;

}
