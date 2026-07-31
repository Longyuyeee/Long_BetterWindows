# LPWP 1.0 安全威胁模型

## 1. 范围与安全目标

本文覆盖 Long助手 Web Widget Surface、Long Plugin Broker、IPC 客户端和 Long Grid
接入边界。目标是确保另一桌面应用只能在当前用户明确允许的范围内读取公开元数据、调用
现有命令和请求打开插件 UI，不能借 Broker 提权、绕过插件权限、读取秘密配置或执行包内
原生代码。

不在本模型内：已取得当前用户进程执行权限的恶意软件、Windows 内核/管理员令牌被攻破、
插件自身业务漏洞，以及 Long Grid 仓库中尚未完成的实现。相同用户不等于完全可信，因此
Broker 仍采用最小 DTO、大小限制、并发限制和可关闭设计。

## 2. 资产与信任边界

需要保护的资产：插件设置与凭据、用户文件、剪贴板与屏幕内容、插件包与签名状态、宿主
进程稳定性、命令结果和用户界面控制权。

```text
Long Grid / Probe（非信任调用方）
        │  Named Pipe：当前用户 + 会话 + 完整性级别
        ▼
Long Plugin Broker（协议/大小/deadline/并发边界）
        │  DTO 投影；CommandExecutor；PluginMainUiLauncher
        ▼
PluginRegistry + PluginAccessContext（宿主授权边界）
        │
        ├── Web Widget 虚拟 HTTPS 来源 + CSP + 子资源拦截
        └── 已安装插件运行时（插件自身仍是不完全信任代码）
```

Long Grid 不读取 Long助手插件目录，不加载 DLL、C# Script 或 WPF 控件。Widget 包仍由
Long助手负责安装、校验与生命周期管理。

## 3. 攻击与控制

| 威胁 | 主要控制 | 失败行为 | 自动证据 |
|---|---|---|---|
| 其他用户连接 Pipe | `CurrentUserOnly` + SID 复核 | 断开，不返回业务数据 | 跨 SID 拒绝测试 |
| 管理员/低完整性跨级代理 | 会话与 Token 完整性 RID 必须相同 | 断开，不提权 | 完整性/会话矩阵测试 |
| 未握手调用 | 首帧仅允许 `host.hello` | `unauthenticated` 后断开 | pre-hello 测试 |
| 协议降级或混淆 | 固定 `long.plugin.ipc/1.0`、GUID ID、响应关联 | `incompatible_protocol` / 失败关闭 | Golden + 错协议测试 |
| 内存/磁盘耗尽 | 1 MiB 帧、512 KiB 输出、deadline、每插件并发 4 | 拒绝、超时或取消 | 超大帧/并发/超时测试 |
| 断线后后台副作用继续 | 连接 CTS 传播到命令 | 取消并丢弃迟到结果 | 断线取消测试 |
| 绕过插件能力 | 复用 `CommandExecutor` 与 `PluginAccessContext` | 现有能力检查拒绝 | 源码不变量门禁 |
| 目录或秘密配置泄漏 | 显式 Catalog DTO，不返回目录、入口、能力、默认设置 | 字段不存在 | 目录脱敏测试 |
| 诊断成为旁路 | 仅白名单方法/标准错误聚合计数 | 未知值归一化 | 诊断敏感字段拒绝测试 |
| Web Widget 远程代码注入 | 包内虚拟 HTTPS 来源、CSP、导航与子资源拦截 | 阻断来源外资源 | Widget 沙箱测试 |
| 路径穿越/符号链接逃逸 | Manifest/包账本与规范化包内路径验证 | 安装失败 | 包验证矩阵 |
| 未授权打开 UI | 同安全边界 + 复用统一 Launcher + UI Dispatcher | 稳定错误码 | plugin.open 测试 |

## 4. 隐私与日志

Broker 诊断允许：UTC 时间、运行状态、连接总数/活动数、已知方法计数、标准错误码计数。
禁止：请求/实例 ID、SID、PID、Pipe 名、插件路径/入口、命令参数、文本/图片/路径输入、
返回内容、设置值、令牌和异常原文。Serilog 仅记录连接级状态与宿主内部异常，不记录信封
payload。

## 5. 密钥与发布

私钥不得进入仓库、`.lpak`、NuGet、日志或诊断。参考包签名必须使用本机仓库外密钥，
只提交签名后的公开包、SHA-256、公开验证材料和可复现的 unsigned 源包。若无可用开发
发布者密钥，签名参考包保持阻塞，不能生成临时私钥并冒充正式发布者。

## 6. 残余风险与发布条件

- 相同用户/完整性级别的恶意进程仍可调用公开命令；用户可在设置中即时关闭 Broker。
- 现有插件若忽略 `CancellationToken`，宿主只能丢弃迟到结果，无法撤销已发生的外部副作用。
- `plugin.open` 是用户可见副作用，但当前没有每次确认；首版依赖同用户边界与总开关。
- 真正宣称 LPWP 1.0 兼容仍需要 Long Grid 跨仓库互操作、真实 Widget 桌面验收和发布签名门禁。

安全相关代码或协议变更必须更新机器基线、Golden Fixtures、本文威胁表和对应拒绝测试；
任何一项缺失都不得标记 PR-D 完成。
