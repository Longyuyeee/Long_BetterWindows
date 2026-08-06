# 插件进程隔离兼容性审计（PX6B-1）

> 日期：2026-08-06
> 结论：完成评估与合同冻结，本批不迁移现有插件、不实现生产 Worker。

## 1. 当前库存与边界

| 运行时 | 权威目录数量 | 当前执行边界 | 评估结论 |
| --- | ---: | --- | --- |
| Native DLL | 7 | 宿主进程内 `AssemblyLoadContext`，直接调用 `IHostApi` 和 WPF 对象 | 合同完成后可实验无 UI Worker，现有资产迁移风险很高 |
| C# Script | 0 | Roslyn 在宿主内执行，`ScriptGlobals` 直接注入 `IHostApi` | 可在代理合同完成后实验，但没有正式资产可证明兼容迁移 |
| 纯 WebView | 19 | WebView2 管理页面进程，WPF 控件、生命周期和 Bridge 在宿主 | 保留现有边界，不再额外套一层 Long Worker |
| WebView + Native Background | 1 | Web UI 与原生后台共享宿主内生命周期 | 未来只拆后台，需先解决事件、资源租约和失败回滚 |

机器可读事实见 [`plugin-isolation-compatibility-matrix.json`](plugin-isolation-compatibility-matrix.json)，格式由
[`plugin-isolation-compatibility.schema.json`](../schemas/plugin-isolation-compatibility.schema.json) 固定。

## 2. 共享 Worker 约束

- 每个插件实例使用独立 Worker，只有此边界才允许归因单插件 CPU、内存与崩溃。
- 复用 LPWP 的长度前缀 JSON、1 MiB 帧限制与 100 ms 至 120 s 超时范围，但使用独立实验协议 `long.plugin.worker/experimental-1`。
- 管道必须由宿主创建，并同时校验进程身份和一次性随机凭据；外部 Broker 的客户端授权不能直接复用为 Worker 信任。
- Worker 不持有 `IHostApi` 对象。能力调用必须经过显式 DTO 代理，回到宿主后重新进入插件访问上下文，再执行权限检查和副作用。
- WPF 与 WebView UI 始终留在宿主；生命周期至少覆盖初始化、启动、停止、后台、恢复、资源释放与语言变化。

## 3. 下一步检查

PX6B-2 只实现 `synthetic_headless_native_command_worker`：

1. 建立独立实验协议、宿主拉起、握手和确定性退出。
2. 用无 UI、无宿主能力的合成插件验证命令、取消、超时、崩溃和清理。
3. 将 Worker 失败映射到现有健康度分类，并从专属进程读取 CPU / 内存快照。
4. 未通过上述门禁前，不迁移 27 项权威目录资产，不修改默认运行模式。

真实插件迁移、沙箱声明、高风险能力代理和后台资源租约均属于后续批次。

## 4. 验证

- 隔离合同专项：3 / 3。
- 全量自动化：1155 / 1155。
- Release 构建：0 警告、0 错误。
- 权威目录派生检查：通过。

## 5. PX6B-2 实验结果

合成无界面 Native Worker 已通过 PID + nonce 握手、完整生命周期、命令、取消、deadline、崩溃、
确定性清理、专属进程资源快照、健康映射和公开 API 隔离门禁。该结果只证明实验架构可行：
生产开关仍关闭，真实插件迁移数仍为 0，下一步先完成最小能力代理与资源租约设计。
最终门禁为 Release 0 警告 / 0 错误、全量自动化 1161 / 1161、目录派生检查通过、
`LongBetterWindows.PluginIpc` 1.1.0 打包通过，且测试后没有 Worker 进程残留。
