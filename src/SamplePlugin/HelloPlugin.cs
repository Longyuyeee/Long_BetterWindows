using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Views;
using Serilog;

namespace SamplePlugin;

/// <summary>
/// SamplePlugin — 参考实现。
///
/// 本文件展示了 ILongPlugin 接口的完整实现模式。
/// 阅读顺序：从上到下，每个区域有注释说明其职责。
///
/// 插件生命周期：
///   PluginScanner 发现 → Loaded
///     → InitializeAsync() → StartAsync() → Running
///       → (用户禁用或宿主退出) → StopAsync() → Disabled
///
/// 关键约定：
///   1. Id/Name/Version 必须与 manifest.json 一致
///   2. 所有能力通过 IHostApi 访问，未声明的能力返回 null
///   3. 日志使用 Serilog，格式: [插件名] 消息
///   4. InitializeAsync 返回 false → 状态变为 Error
///   5. StartAsync 返回 false → 插件不会进入 Running
/// </summary>
public class HelloPlugin :
    ILongPlugin,
    IHasMainUI,
    IPluginCommandHandler,
    IPluginLanguageLifecycle
{
    private IReadOnlyDictionary<string, string> _strings =
        new Dictionary<string, string>(StringComparer.Ordinal);

    // ═══════════════════════════════════════════════
    // 元数据 — 必须与 manifest.json 保持一致
    // ═══════════════════════════════════════════════
    public string Id => "com.long.sample";
    public string Name => Text("plugin.name", "示例插件");
    public string Version => "1.1.0";

    // 当前状态：Loaded → Running → Disabled
    public PluginState State { get; private set; } = PluginState.Loaded;

    // ═══════════════════════════════════════════════
    // InitializeAsync — 阶段 1: 初始化
    // 此时可访问 IHostApi 检查能力是否已授权。
    // 在此完成：配置加载、资源分配、能力验证。
    // ═══════════════════════════════════════════════
    public Task<bool> InitializeAsync(IHostApi host)
    {
        // host.HotKey  — 热键服务（需要 system.hotkey 能力）
        // host.ShellSelection — Explorer 感知（需要 shell.selection 能力）
        // host.ADS     — NTFS 备用数据流（需要 fs.ads.access 能力）
        // host.Registry — 注册表操作（需要 system.registry.* 能力）
        // host.Storage — 本地 KV 存储（需要 storage.local 能力）

        // 示例：检查能力授权
        // if (host.HotKey == null) {
        //     Log.Error("[SamplePlugin] 未获得热键能力");
        //     return Task.FromResult(false);  // 返回 false → Error 状态
        // }

        Log.Information("[SamplePlugin] 初始化完成");
        return Task.FromResult(true);
    }

    // ═══════════════════════════════════════════════
    // StartAsync — 阶段 2: 启动
    // 初始化成功后调用。在此注册热键、启动定时器、打开监听等。
    // ═══════════════════════════════════════════════
    public Task<bool> StartAsync()
    {
        // 示例：注册热键
        // await host.HotKey!.RegisterAsync("Alt+X", OnHotkey);

        State = PluginState.Running;
        Log.Information("[SamplePlugin] 已启动");
        return Task.FromResult(true);
    }

    // ═══════════════════════════════════════════════
    // StopAsync — 阶段 3: 停止
    // 插件禁用/宿主退出时调用。释放所有资源。
    // ═══════════════════════════════════════════════
    public Task<bool> StopAsync()
    {
        // 示例：注销热键
        // await host.HotKey!.UnregisterAsync("Alt+X");

        State = PluginState.Stopped;
        Log.Information("[SamplePlugin] 已停止");
        return Task.FromResult(true);
    }

    // 统一主入口：插件卡片和命令中心最终复用同一个行为。
    public void ShowMainUI()
        => FloatingHudWindow.ShowToast(
            Text("toast.ready", "示例插件运行正常"));

    // 声明式命令处理器：command_id 来自 manifest.json。
    public Task<PluginCommandResult> ExecuteCommandAsync(
        PluginCommandInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (invocation.CommandId != "sample.hello")
        {
            return Task.FromResult(PluginCommandResult.Failure(
                string.Format(
                    Text("error.unknownCommand", "未知示例命令: {0}"),
                    invocation.CommandId)));
        }

        ShowMainUI();
        return Task.FromResult(PluginCommandResult.Success(
            Text("result.success", "示例命令执行成功")));
    }

    // 语言切换只更新展示资源，不重复启动插件或执行示例命令。
    public Task OnLanguageChangedAsync(
        PluginLanguageContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _strings = context.Resources;
        return Task.CompletedTask;
    }

    private string Text(string key, string fallback)
        => _strings.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;

    // ═══════════════════════════════════════════════
    // 扩展提示：
    //
    // 1. 自定义设置 UI: 让类实现 IHasSettingsUI 接口
    //    public class HelloPlugin : ILongPlugin, IHasSettingsUI { ... }
    //
    // 2. 配置持久化: 通过 host.Storage 存取配置
    //    await host.Storage.SetAsync("key", "value");
    //    var v = await host.Storage.GetAsync("key");
    //
    // 3. 使用 Explorer 能力:
    //    var path = await host.ShellSelection.GetActiveExplorerFolderPathAsync();
    //
    // 4. NTFS ADS 存储:
    //    await host.ADS.WriteAsync(path, "stream", "content");
    //    var content = await host.ADS.ReadAsync(path, "stream");
    // 5. 可发现命令: 在 manifest.json 声明 commands，并实现 IPluginCommandHandler。
    // ═══════════════════════════════════════════════
}
