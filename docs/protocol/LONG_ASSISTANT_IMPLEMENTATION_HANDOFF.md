# Long助手实施交接单：LPWP 1.0

状态：Ready for implementation planning
Long助手审计基线：`Longyuyeee/Long_BetterWindows@0d1366f`
上位规范：[Long 插件小组件兼容协议（LPWP）1.0](LONG_WIDGET_PROTOCOL_V1.md)

## 1. 给 Long助手的任务

请在不破坏现有插件的前提下，为 Long助手实现 LPWP 1.0 的“契约、Web Widget 支持、跨进程 Broker 和一致性测试”。不要直接改成共享插件目录，也不要让 Long Grid 加载第三方 DLL、C# Script 或 WPF 控件。

实现完成后，Long助手应向 Long Grid 提供：

- 版本化、无 WPF 依赖的 Manifest/Widget DTO；
- 相同版本的 JSON Schema 与语义校验器；
- Broker IPC 契约与客户端包；
- Web Widget TypeScript 类型、Bridge SDK 和 Mock Host；
- 一个签名的 Web Widget 参考 `.lpak`；
- Golden Fixtures 与测试报告；
- 宿主最低版本、权限和错误码说明。

## 2. 代码基线判断

当前代码已经具备：

- Plugin API `1.0.0` 和同 major/minor 兼容判断；
- 严格的 Draft 2020-12 Manifest Schema；
- `PluginManifest`、`ManifestReader`、包校验与事务安装；
- WebView2 Bridge 的 `{id, method, args}` 调用模式；
- 本地顶层导航限制、新窗口/下载阻止和消息来源检查；
- `.NET PluginSdk` 与单独的 WPF SDK；
- 插件命令、能力、生命周期和签名/市场校验。

当前尚缺：

- Manifest `widgets` 定义；
- Widget 实例、尺寸、可见性、暂停/恢复等生命周期；
- `host.getInfo` 和 Widget Bridge；
- 足够严格的 Widget 子资源沙箱；
- Long Grid 可调用的同用户 IPC Broker；
- 跨仓库 Golden Fixtures。

## 3. 建议变更位置

以下路径基于审计提交，实际实施时允许重构，但协议行为必须保持一致。

### 3.1 API 与 Manifest

| 当前文件 | 建议修改 |
|---|---|
| `src/LongBetterWindows.Host/Contracts/ApiVersion.cs` | 将当前 API 增至 `1.1.0`，保留现有兼容算法 |
| `src/LongBetterWindows.Host/Contracts/PluginManifest.cs` | 增加 `Widgets` 及 Widget DTO |
| `schemas/plugin-manifest.schema.json` | 合并 `long-widget.schema.json` 中的定义 |
| `src/LongBetterWindows.Host/Engine/ManifestReader.cs` | 反序列化并产生带 JSON 路径的错误 |
| `src/LongBetterWindows.Host/Engine/PluginPackageValidator.cs` | 校验入口、runtime、尺寸和包内路径 |
| `src/LongBetterWindows.Host/Engine/LpakInstaller.cs` | 保持事务行为，确保 Widget 资源进入文件总账 |
| `src/LongBetterWindows.PluginSdk/` | 输出无 WPF 的公共 Widget 契约 |

不要只修改 Schema。DTO、读取器、打包校验、市场校验、SDK 与测试必须在同一个版本批次完成。

### 3.2 Web Host

| 当前文件 | 建议修改/拆分 |
|---|---|
| `Engine/WebPluginBridgeProtocol.cs` | 增加 `host.getInfo`、Widget 方法、限制与稳定错误映射 |
| `Engine/WebPluginHostDispatcher.cs` | 识别 Widget 上下文；从宿主绑定身份 |
| `Engine/WebPluginViewLifecycle.cs` | 增加 mounted/ready/suspend/resume/unmount 和资源回收 |
| `Engine/WebPluginNavigationPolicy.cs` | 扩展为主文档与子资源共同策略 |
| `Engine/WebPluginPresentationCoordinator.cs` | 不把 WPF Window 当作 Widget Surface |
| `sdk/web/` | 增加 TypeScript 类型、事件助手与 Mock Widget Host |

建议新建：

```text
src/LongBetterWindows.Host/Engine/Widgets/
├─ WidgetManifestValidator.cs
├─ WidgetInstanceContext.cs
├─ WidgetBridgeDispatcher.cs
├─ WidgetLifecycleCoordinator.cs
├─ WidgetResourcePolicy.cs
└─ WidgetHostInfoProvider.cs
```

Widget Surface 与现有 embedded/detached Web 窗口可以复用底层 WebView2 初始化，但不能复用 WPF Window 的位置/尺寸所有权。

### 3.3 Broker

建议新增独立、可替换的模块：

```text
src/LongBetterWindows.PluginIpc/
├─ Contracts/
│  ├─ IpcEnvelope.cs
│  ├─ HostHello.cs
│  ├─ PluginCatalogDtos.cs
│  └─ IpcErrorCodes.cs
├─ Framing/
│  └─ LengthPrefixedJsonFraming.cs
└─ Client/
   └─ LongPluginBrokerClient.cs

src/LongBetterWindows.Host/Broker/
├─ LongPluginBrokerService.cs
├─ BrokerConnection.cs
├─ BrokerAuthentication.cs
├─ PluginCatalogEndpoint.cs
└─ PluginCommandEndpoint.cs
```

要求：

- IPC Contracts 项目不能引用 Host、WPF 或具体插件实现；
- Named Pipe ACL 仅允许当前用户；
- 每个请求有上限、deadline、取消令牌和审计 ID；
- Broker 调用现有 Host API/插件管理器，不能绕过能力检查；
- Broker 默认随 Long助手启动，可在设置中禁用；
- Long Grid 断开不会停止 Long助手本身。

## 4. 分阶段 PR

不要把全部工作塞入一个 PR。

### PR-A：共享契约

- API `1.1.0`；
- Widget DTO；
- Manifest Schema；
- 语义校验；
- 合法/非法 Fixtures；
- 文档。

验收：所有旧 Manifest 测试不变，新 Schema 测试通过；尚不要求 UI 显示 Widget。

### PR-B：Web Widget Bridge 与沙箱

- `host.getInfo`；
- Widget Bridge 与事件；
- 受控 HTTPS 虚拟来源；
- 子资源拦截和 CSP；
- Web SDK/Mock Host；
- 参考 Widget 开发包。

验收：生命周期、来源伪造、超大消息、导航、挂起与恢复测试通过。

### PR-C：Broker IPC

- Named Pipe 帧协议与握手；
- catalog/list/get；
- command invoke/cancel；
- plugin.open；
- 权限、超时、断线和崩溃隔离。

验收：Long Grid 的最小测试客户端能枚举并调用现有命令插件。

### PR-D：发行与兼容门禁

- 签名参考 `.lpak`；
- 市场最低 API/宿主校验；
- 新权限提示；
- 诊断日志与关闭 Broker 的设置；
- 发布迁移说明。

## 5. 关键实现禁区

- 禁止把 `LongBetterWindows.PluginSdk.Wpf` 作为共享契约依赖。
- 禁止让 Long Grid 直接读取并执行插件 DLL/Script。
- 禁止为图方便在 IPC 中使用 `BinaryFormatter`、任意类型反序列化或 CLR 类型名。
- 禁止仅检查 WebView 顶层导航而放过远程脚本、iframe、WebSocket 或任意子资源。
- 禁止让 Web 页面自己声明 `plugin_id`/`instance_id` 后直接获得信任。
- 禁止两个应用同时改写 Long助手现有 Plugins 目录或激活状态。
- 禁止旧宿主静默忽略安全相关 Manifest 字段。
- 禁止把 `ui.floating_box` 直接重命名为 Widget；两者生命周期和所有权不同。

## 6. 必须回答的实现问题

Long助手提交设计 PR 时，需要明确回答：

1. 哪个组件是安装、升级、启用和权限状态的唯一写入者？
2. Long Grid 如何获得经校验的只读 Widget 资源？
3. Pipe 如何限制为当前用户，如何处理进程完整性级别不同？
4. Bridge 如何把消息绑定到真实插件与实例？
5. WebView 如何阻止包外子资源、远程脚本和任意协议？
6. 隐藏、锁屏、内存压力和进程崩溃时如何释放资源？
7. 老版本 Long助手遇到带 `widgets` 的包时，市场如何提前阻止？
8. 单独升级或回滚任一宿主时，降级路径是什么？
9. 哪些测试夹具会同时进入 Long助手与 Long Grid CI？
10. 哪些功能留到 LPWP 1.1/2.0，避免首版范围膨胀？

## 7. 交付物清单

- [ ] 协议文档副本及版本号
- [ ] 合并后的完整 Manifest Schema
- [ ] C# Contracts NuGet 或项目包
- [ ] IPC Client 包
- [ ] TypeScript Web SDK
- [ ] Mock Host
- [ ] 合法/非法 Manifest Fixtures
- [ ] IPC 请求/响应 Golden Fixtures
- [ ] Bridge 生命周期 Fixtures
- [ ] 签名参考 `.lpak`
- [ ] Long Grid 最小互操作测试说明
- [ ] 安全威胁模型
- [ ] 性能基线与资源回收结果
- [ ] 兼容、升级、回滚和卸载测试报告

## 8. 给 Long助手代理的可复制指令

```text
请以 docs/protocol/LONG_WIDGET_PROTOCOL_V1.md 为唯一行为规范，
按 docs/protocol/LONG_ASSISTANT_IMPLEMENTATION_HANDOFF.md 的 PR-A 到 PR-D 顺序实施。

先审计当前实现与协议的差异并输出差异表，再实施 PR-A；不要跳过契约测试直接开发 UI。
必须保持现有 Plugin API 1.0 插件兼容，不得让 Long Grid 加载 DLL、C# Script 或 WPF 控件。
所有新增 Manifest、Bridge 与 IPC 行为都要有合法、边界、拒绝和降级测试。
若现有架构与协议冲突，先提交 ADR 说明冲突、风险和替代方案，不得静默改变协议。
完成后输出：改动文件、测试结果、未完成项、兼容矩阵、安全复核和供 Long Grid 使用的制品。
```
