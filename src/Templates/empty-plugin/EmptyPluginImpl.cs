using LongBetterWindows.Host.Core;
using Serilog;

namespace EmptyPlugin;

/// <summary>
/// 空插件 — 最小骨架模板。
/// 仅实现 ILongPlugin 生命周期，不使用任何宿主能力。
/// 适合：纯后台任务、定时器、系统监控等不需要 UI 或热键的场景。
/// </summary>
public class EmptyPluginImpl : ILongPlugin
{
    // ===== 元数据 =====
    // 修改这些值以匹配你的 manifest.json
    public string Id => "com.example.empty";
    public string Name => "空插件模板";
    public string Version => "1.0.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    // ===== 初始化 =====
    // 在插件加载后、启动前调用。
    // host 参数提供对所有宿主能力服务的访问。
    // 在此阶段完成资源分配、配置加载等准备工作。
    public Task<bool> InitializeAsync(IHostApi host)
    {
        Log.Information("[EmptyPlugin] 初始化完成");
        return Task.FromResult(true);
    }

    // ===== 启动 =====
    // 初始化完成后调用。在此注册热键、启动后台任务等。
    // 返回 false 表示启动失败，状态会变为 Error。
    public Task<bool> StartAsync()
    {
        State = PluginState.Running;
        Log.Information("[EmptyPlugin] 已启动");
        return Task.FromResult(true);
    }

    // ===== 停止 =====
    // 插件被禁用或宿主退出时调用。
    // 释放所有资源：注销热键、停止定时器、保存状态等。
    public Task<bool> StopAsync()
    {
        State = PluginState.Stopped;
        Log.Information("[EmptyPlugin] 已停止");
        return Task.FromResult(true);
    }
}
