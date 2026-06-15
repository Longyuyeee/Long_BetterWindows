using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LongBetterWindows.Host.Core;
using Serilog;

namespace WindowManagerPlugin;

public class WindowManagerPluginImpl : ILongPlugin, IHasSettingsUI, IHasMainUI
{
    private IHostApi? _host;

    public string Id => "com.long.window-manager";
    public string Name => "窗口管理";
    public string Version => "2.0.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    // P/Invoke
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int newStyle);
    static readonly IntPtr HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2);
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
        // 置顶
        var r1 = await hk.RegisterAsync("Ctrl+Alt+T", Id, ToggleTopmost);
        // 半屏
        var r2 = await hk.RegisterAsync("Ctrl+Alt+Left", Id, () => Snap("left"));
        var r3 = await hk.RegisterAsync("Ctrl+Alt+Right", Id, () => Snap("right"));
        var r4 = await hk.RegisterAsync("Ctrl+Alt+Up", Id, () => Snap("max"));
        var r5 = await hk.RegisterAsync("Ctrl+Alt+Down", Id, () => Snap("bottom"));
        // 四分屏
        var r6 = await hk.RegisterAsync("Ctrl+Alt+1", Id, () => Snap("top-left"));
        var r7 = await hk.RegisterAsync("Ctrl+Alt+2", Id, () => Snap("top-right"));
        var r8 = await hk.RegisterAsync("Ctrl+Alt+3", Id, () => Snap("bottom-left"));
        var r9 = await hk.RegisterAsync("Ctrl+Alt+4", Id, () => Snap("bottom-right"));
        // 三分屏
        var r10 = await hk.RegisterAsync("Ctrl+Alt+Shift+Left", Id, () => Snap("third-left"));
        var r11 = await hk.RegisterAsync("Ctrl+Alt+Shift+Right", Id, () => Snap("third-right"));

        if (!r1.IsSuccess || !r2.IsSuccess) { State = PluginState.Error; return false; }
        State = PluginState.Running;
        return true;
    }

    public async Task<bool> StopAsync()
    {
        var hk = _host!.HotKey!;
        foreach (var key in new[] { "Ctrl+Alt+T", "Ctrl+Alt+Left", "Ctrl+Alt+Right", "Ctrl+Alt+Up", "Ctrl+Alt+Down",
            "Ctrl+Alt+1","Ctrl+Alt+2","Ctrl+Alt+3","Ctrl+Alt+4","Ctrl+Alt+Shift+Left","Ctrl+Alt+Shift+Right" })
            await hk.UnregisterAsync(key);
        State = PluginState.Disabled;
        return true;
    }

    public void ShowMainUI()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var w = new Window
            {
                Title = "窗口管理 - 快捷键",
                Width = 400, Height = 360,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.ToolWindow,
                Content = CreateLayoutGuide(),
            };
            w.Show();
        });
    }

    public FrameworkElement CreateSettingsUI()
    {
        return new LongBetterWindows.Host.Views.HotkeySettingsControl(
            "窗口管理", Id, "Ctrl+Alt+T", _ => { });
    }

    private static FrameworkElement CreateLayoutGuide()
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "窗口管理快捷键", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,16) });

        var sections = new (string, string[])[]
        {
            ("置顶", new[] { "Ctrl+Alt+T  切换置顶/取消" }),
            ("半屏", new[] { "Ctrl+Alt+←  左半屏", "Ctrl+Alt+→  右半屏", "Ctrl+Alt+↑  最大化", "Ctrl+Alt+↓  下半屏" }),
            ("四分屏", new[] { "Ctrl+Alt+1  左上 ¼", "Ctrl+Alt+2  右上 ¼", "Ctrl+Alt+3  左下 ¼", "Ctrl+Alt+4  右下 ¼" }),
            ("三分屏", new[] { "Ctrl+Alt+Shift+←  左⅓", "Ctrl+Alt+Shift+→  右⅔" }),
        };

        foreach (var (title, items) in sections)
        {
            panel.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x00,0x7A,0xFF)), Margin = new Thickness(0,8,0,4) });
            foreach (var item in items)
                panel.Children.Add(new TextBlock { Text = item, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x55,0x55,0x55)), Margin = new Thickness(8,2,0,2) });
        }

        return panel;
    }

    private void ToggleTopmost()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        bool isTopmost = (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
        SetWindowPos(hwnd, isTopmost ? HWND_NOTOPMOST : HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW);
        LongBetterWindows.Host.Views.FloatingHudWindow.ShowToast(isTopmost ? "已取消置顶" : "窗口已置顶");
    }

    private void Snap(string layout)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;
            var area = SystemParameters.WorkArea;
            int L = (int)area.Left, T = (int)area.Top, W = (int)area.Width, H = (int)area.Height;

            (int x, int y, int w, int h) = layout switch
            {
                "left" => (L, T, W / 2, H),
                "right" => (L + W / 2, T, W / 2, H),
                "max" => (L, T, W, H),
                "bottom" => (L, T + H / 2, W, H / 2),
                "top-left" => (L, T, W / 2, H / 2),
                "top-right" => (L + W / 2, T, W / 2, H / 2),
                "bottom-left" => (L, T + H / 2, W / 2, H / 2),
                "bottom-right" => (L + W / 2, T + H / 2, W / 2, H / 2),
                "third-left" => (L, T, W / 3, H),
                "third-right" => (L, T, W * 2 / 3, H),
                _ => (L, T, W, H),
            };

            SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, SWP_SHOWWINDOW);
            LongBetterWindows.Host.Views.FloatingHudWindow.ShowToast(
                layout switch { "left"=>"左半屏","right"=>"右半屏","max"=>"最大化","bottom"=>"下半屏",
                "top-left"=>"左上","top-right"=>"右上","bottom-left"=>"左下","bottom-right"=>"右下",
                "third-left"=>"左⅓","third-right"=>"右⅔", _=>layout });
        });
    }
}
