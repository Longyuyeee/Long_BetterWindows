using System.Runtime.InteropServices;
using System.Windows;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Views;
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
    private readonly List<string> _registeredHotkeys = new();
    private readonly List<WeakReference<HotkeySettingsControl>> _settings = [];
    private WindowManagerGuide? _guide;
    private string _configuredTopmostHotkey = "Ctrl+Alt+T";
    private string? _registeredTopmostHotkey;
    private IReadOnlyDictionary<string, string> _strings =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string Id => "com.long.window-manager";
    public string Name => Text("plugin.name", "窗口管理");
    public string Version => "2.1.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;
        return Task.FromResult(true);
    }

    public async Task<bool> StartAsync()
    {
        var bindings = new (string Key, Action Callback)[]
        {
            (_configuredTopmostHotkey, ToggleTopmost),
            ("Ctrl+Alt+Left", () => Snap("left")),
            ("Ctrl+Alt+Right", () => Snap("right")),
            ("Ctrl+Alt+Up", () => Snap("max")),
            ("Ctrl+Alt+Down", () => Snap("bottom")),
            ("Ctrl+Alt+1", () => Snap("top-left")),
            ("Ctrl+Alt+2", () => Snap("top-right")),
            ("Ctrl+Alt+3", () => Snap("bottom-left")),
            ("Ctrl+Alt+4", () => Snap("bottom-right")),
            ("Ctrl+Alt+Shift+Left", () => Snap("third-left")),
            ("Ctrl+Alt+Shift+Right", () => Snap("third-right")),
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
        foreach (var key in _registeredHotkeys)
            await _host.HotKey.UnregisterAsync(key);
        _registeredHotkeys.Clear();
        _registeredTopmostHotkey = null;
        Application.Current.Dispatcher.Invoke(() => _guide?.Close());
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
            Text("settings.topmostTitle", "窗口置顶"),
            Id,
            _registeredTopmostHotkey
                ?? Text("settings.commandCenter", "命令中心"),
            value =>
            {
                ReplaceRegisteredHotkey(_registeredTopmostHotkey, value);
                _configuredTopmostHotkey = value;
                _registeredTopmostHotkey = value;
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
        switch (invocation.CommandId)
        {
            case "window.guide": ShowMainUI(); break;
            case "window.topmost": ToggleTopmost(); break;
            case "window.left": Snap("left"); break;
            case "window.right": Snap("right"); break;
            case "window.maximize": Snap("max"); break;
            case "window.bottom": Snap("bottom"); break;
            case "window.top-left": Snap("top-left"); break;
            case "window.top-right": Snap("top-right"); break;
            case "window.bottom-left": Snap("bottom-left"); break;
            case "window.bottom-right": Snap("bottom-right"); break;
            case "window.third-left": Snap("third-left"); break;
            case "window.third-right": Snap("third-right"); break;
            default:
                return Task.FromResult(PluginCommandResult.Failure(string.Format(
                    Text("error.unknownCommand", "未知窗口命令: {0}"),
                    invocation.CommandId)));
        }

        return Task.FromResult(PluginCommandResult.Success(
            Text("command.completed", "窗口布局已应用")));
    }

    private void ToggleTopmost()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return;
        var isTopmost = (GetWindowLong(window, GwlExstyle) & WsExTopmost) != 0;
        SetWindowPos(window, isTopmost ? HwndNotopmost : HwndTopmost, 0, 0, 0, 0,
            SwpNosize | SwpNomove | SwpShowwindow);
        FloatingHudWindow.ShowToast(isTopmost
            ? Text("toast.topmostDisabled", "已取消窗口置顶")
            : Text("toast.topmostEnabled", "窗口已置顶"));
    }

    private void Snap(string layout)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return;
            var monitor = MonitorFromWindow(window, MonitorDefaulttonearest);
            var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfoW(monitor, ref monitorInfo)) return;

            var left = monitorInfo.Work.Left;
            var top = monitorInfo.Work.Top;
            var width = monitorInfo.Work.Right - left;
            var height = monitorInfo.Work.Bottom - top;
            var target = layout switch
            {
                "left" => (left, top, width / 2, height),
                "right" => (left + width / 2, top, width / 2, height),
                "max" => (left, top, width, height),
                "bottom" => (left, top + height / 2, width, height / 2),
                "top-left" => (left, top, width / 2, height / 2),
                "top-right" => (left + width / 2, top, width / 2, height / 2),
                "bottom-left" => (left, top + height / 2, width / 2, height / 2),
                "bottom-right" => (left + width / 2, top + height / 2, width / 2, height / 2),
                "third-left" => (left, top, width / 3, height),
                "third-right" => (left + width / 3, top, width * 2 / 3, height),
                _ => (left, top, width, height),
            };

            SetWindowPos(window, IntPtr.Zero, target.Item1, target.Item2, target.Item3, target.Item4, SwpShowwindow);
            FloatingHudWindow.ShowToast(LayoutName(layout));
        });
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

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotopmost = new(-2);
    private const uint SwpNosize = 0x0001, SwpNomove = 0x0002, SwpShowwindow = 0x0040;
    private const int GwlExstyle = -20, WsExTopmost = 0x0008;
    private const uint MonitorDefaulttonearest = 2;

    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public Rect Monitor; public Rect Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
}
