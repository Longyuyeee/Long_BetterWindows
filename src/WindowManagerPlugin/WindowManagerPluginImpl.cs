using System.Runtime.InteropServices;
using System.Windows;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Views;
using Serilog;

namespace WindowManagerPlugin;

public class WindowManagerPluginImpl : ILongPlugin, IHasSettingsUI, IHasMainUI, IPluginCommandHandler
{
    private IHostApi _host = null!;
    private readonly List<string> _registeredHotkeys = new();
    private WindowManagerGuide? _guide;

    public string Id => "com.long.window-manager";
    public string Name => "窗口管理";
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
            ("Ctrl+Alt+T", ToggleTopmost),
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
                _registeredHotkeys.Add(binding.Key);
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

            _guide = new WindowManagerGuide();
            _guide.Closed += (_, _) => _guide = null;
            _guide.Show();
        });
    }

    public FrameworkElement CreateSettingsUI()
        => new HotkeySettingsControl("窗口置顶", Id, "Ctrl+Alt+T", _ => { });

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
            default: return Task.FromResult(PluginCommandResult.Failure($"未知窗口命令: {invocation.CommandId}"));
        }

        return Task.FromResult(PluginCommandResult.Success("窗口布局已应用"));
    }

    private void ToggleTopmost()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return;
        var isTopmost = (GetWindowLong(window, GwlExstyle) & WsExTopmost) != 0;
        SetWindowPos(window, isTopmost ? HwndNotopmost : HwndTopmost, 0, 0, 0, 0,
            SwpNosize | SwpNomove | SwpShowwindow);
        FloatingHudWindow.ShowToast(isTopmost ? "已取消窗口置顶" : "窗口已置顶");
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

    private static string LayoutName(string layout) => layout switch
    {
        "left" => "左半屏", "right" => "右半屏", "max" => "最大化", "bottom" => "下半屏",
        "top-left" => "左上四分屏", "top-right" => "右上四分屏",
        "bottom-left" => "左下四分屏", "bottom-right" => "右下四分屏",
        "third-left" => "左侧三分之一", "third-right" => "右侧三分之二", _ => layout,
    };

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
