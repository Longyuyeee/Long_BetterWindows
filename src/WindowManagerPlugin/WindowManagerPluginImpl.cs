using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using LongBetterWindows.Host.Core;
using Serilog;

namespace WindowManagerPlugin;

public class WindowManagerPluginImpl : ILongPlugin, IHasSettingsUI, IHasMainUI
{
    private IHostApi? _host;

    public string Id => "com.long.window-manager";
    public string Name => "窗口管理";
    public string Version => "1.0.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    // Win32
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int newStyle);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    static readonly IntPtr HWND_TOPMOST = new(-1);
    static readonly IntPtr HWND_NOTOPMOST = new(-2);
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_SHOWWINDOW = 0x0040;
    const int GWL_EXSTYLE = -20, WS_EX_TOPMOST = 0x0008;

    public Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;
        if (host.HotKey == null) { State = PluginState.Error; return Task.FromResult(false); }
        return Task.FromResult(true);
    }

    public async Task<bool> StartAsync()
    {
        var hk = _host!.HotKey!;
        var r1 = await hk.RegisterAsync("Ctrl+Alt+T", Id, ToggleTopmost);
        var r2 = await hk.RegisterAsync("Ctrl+Alt+Left", Id, () => SnapWindow("left"));
        var r3 = await hk.RegisterAsync("Ctrl+Alt+Right", Id, () => SnapWindow("right"));
        var r4 = await hk.RegisterAsync("Ctrl+Alt+Up", Id, () => SnapWindow("max"));

        if (!r1.IsSuccess || !r2.IsSuccess || !r3.IsSuccess || !r4.IsSuccess)
        { State = PluginState.Error; return false; }

        State = PluginState.Running;
        return true;
    }

    public async Task<bool> StopAsync()
    {
        var hk = _host!.HotKey!;
        await hk.UnregisterAsync("Ctrl+Alt+T");
        await hk.UnregisterAsync("Ctrl+Alt+Left");
        await hk.UnregisterAsync("Ctrl+Alt+Right");
        await hk.UnregisterAsync("Ctrl+Alt+Up");
        State = PluginState.Disabled;
        return true;
    }

    public void ShowMainUI()
    {
        LongBetterWindows.Host.Views.FloatingHudWindow.ShowToast(
            "Ctrl+Alt+T 置顶  Ctrl+Alt+←/→ 分屏  Ctrl+Alt+↑ 最大化");
    }

    public FrameworkElement CreateSettingsUI()
    {
        return new LongBetterWindows.Host.Views.HotkeySettingsControl(
            "窗口管理", Id, "Ctrl+Alt+T", _ => { });
    }

    private void ToggleTopmost()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        bool isTopmost = (exStyle & WS_EX_TOPMOST) != 0;

        if (isTopmost)
        {
            SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW);
            LongBetterWindows.Host.Views.FloatingHudWindow.ShowToast("已取消置顶");
        }
        else
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW);
            LongBetterWindows.Host.Views.FloatingHudWindow.ShowToast("窗口已置顶");
        }
    }

    private void SnapWindow(string direction)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            var area = SystemParameters.WorkArea;
            int x, y, w, h;

            if (direction == "left") { x = (int)area.Left; y = (int)area.Top; w = (int)(area.Width / 2); h = (int)area.Height; }
            else if (direction == "right") { x = (int)(area.Left + area.Width / 2); y = (int)area.Top; w = (int)(area.Width / 2); h = (int)area.Height; }
            else { x = (int)area.Left; y = (int)area.Top; w = (int)area.Width; h = (int)area.Height; } // maximize

            SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, SWP_SHOWWINDOW);
        });
    }
}
