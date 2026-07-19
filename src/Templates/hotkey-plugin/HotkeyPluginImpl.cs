using System.Windows;
using LongBetterWindows.Host.Core;
using Serilog;

namespace HotkeyPlugin;

/// <summary>
/// 热键插件模板。
/// 注册全局热键 → 热键触发时执行自定义逻辑。
/// 适合：快捷键工具、快速操作、弹窗交互。
/// </summary>
public class HotkeyPluginImpl : ILongPlugin
{
    private IHostApi? _host;

    public string Id => "com.example.hotkey";
    public string Name => "热键插件模板";
    public string Version => "1.0.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;

        // 检查能力是否已授权（必须在 manifest.capabilities 中声明）
        if (!host.HasCapability("system.hotkey"))
        {
            Log.Error("[HotkeyPlugin] 未获得 system.hotkey 能力授权");
            State = PluginState.Error;
            return Task.FromResult(false);
        }

        Log.Information("[HotkeyPlugin] 初始化完成");
        return Task.FromResult(true);
    }

    public async Task<bool> StartAsync()
    {
        // 注册全局热键（从 manifest.default_settings 读取）
        var result = await _host!.HotKey!.RegisterAsync("Alt+X", OnHotkeyTriggered);

        if (!result.IsSuccess)
        {
            Log.Error("[HotkeyPlugin] 热键注册失败: {Error}", result.ErrorMessage);
            State = PluginState.Error;
            return false;
        }

        State = PluginState.Running;
        Log.Information("[HotkeyPlugin] 已启动，热键 Alt+X");
        return true;
    }

    public async Task<bool> StopAsync()
    {
        await _host!.HotKey!.UnregisterAsync("Alt+X");
        State = PluginState.Stopped;
        Log.Information("[HotkeyPlugin] 已停止");
        return true;
    }

    // ===== 热键触发回调 =====
    // 在后台线程调用，UI 操作需要通过 Dispatcher
    private void OnHotkeyTriggered()
    {
        // 在此编写你的业务逻辑
        // 示例：弹出消息框（需要 UseWPF=true）
        Application.Current.Dispatcher.Invoke(() =>
        {
            MessageBox.Show("热键 Alt+X 被触发！\n你可以在这里实现任何功能。",
                "HotkeyPlugin", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }
}
