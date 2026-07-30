# Long助手兼容协议交付包

把整个 `docs/protocol` 文件夹交给 Long助手即可，不需要同时复制 Long Grid 的其他产品、竞品或任务栏审计文档。

## 必读顺序

1. [LONG_WIDGET_PROTOCOL_V1.md](LONG_WIDGET_PROTOCOL_V1.md)
   唯一行为规范。Manifest、Bridge、生命周期、IPC、安全、兼容降级和测试要求都以它为准。

2. [LONG_ASSISTANT_IMPLEMENTATION_HANDOFF.md](LONG_ASSISTANT_IMPLEMENTATION_HANDOFF.md)
   Long助手仓库的实施交接单，包含当前差异、建议修改位置、PR 拆分、禁区和交付物。

3. [COMPATIBILITY_BASELINE.md](COMPATIBILITY_BASELINE.md)
   Long助手当前能力和兼容边界摘要，用于实施前复核，避免把现有 WPF/WebView 能力误当成 Widget Host。

## 机器文件

- [long-widget.schema.json](long-widget.schema.json)：`widgets` 字段的 Draft 2020-12 JSON Schema。
- [hardware-monitor-widget.manifest.json](examples/hardware-monitor-widget.manifest.json)：参考插件 Manifest。

机器文件不是独立规范；如果它们与 `LONG_WIDGET_PROTOCOL_V1.md` 冲突，应先修正文档和 Schema，再开发代码。

## Long助手应返回的成果

- 实施差异表；
- 分阶段 PR 或等价变更记录；
- Plugin API `1.1.0` 契约；
- 合并后的完整 Manifest Schema；
- C# Contracts/IPC Client；
- TypeScript Widget SDK 与 Mock Host；
- 签名参考 `.lpak`；
- Golden Fixtures；
- 测试、安全复核和未完成项报告。

## 可直接复制给 Long助手的指令

```text
请完整读取本文件夹内的 README.md、LONG_WIDGET_PROTOCOL_V1.md、
LONG_ASSISTANT_IMPLEMENTATION_HANDOFF.md 和 COMPATIBILITY_BASELINE.md，
并把 LONG_WIDGET_PROTOCOL_V1.md 作为唯一行为规范。

先审计当前 Long助手实现与协议的差异并输出差异表，再按照交接单的
PR-A、PR-B、PR-C、PR-D 顺序实施。不要跳过共享契约和一致性测试直接开发 UI。

必须保持现有 Plugin API 1.0 插件兼容；不得让 Long Grid 加载第三方 DLL、
C# Script 或 WPF 控件。插件安装、执行和权限裁决仍由 Long助手负责，
Long Grid 只负责桌面布局、动作卡和受隔离的 Web Widget Surface。

所有新增 Manifest、Bridge、IPC 和安全行为必须具有正常、边界、拒绝、
故障、升级和降级测试。若现有架构与协议冲突，先提交 ADR，不得静默改变协议。

完成后请返回：改动文件、测试结果、兼容矩阵、安全复核、未完成项，
以及供 Long Grid 使用的 Contracts、IPC Client、Schema、SDK、Fixtures 和参考 .lpak。
```
