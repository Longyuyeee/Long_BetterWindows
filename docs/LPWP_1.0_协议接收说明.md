# LPWP 1.0 协议接收说明

## 当前状态

- `docs/protocol` 原样同步自 `long_grid` 提交 `8a93904f74cb1ab802f34e0eb9f9c38d6a5ab065`。
- 该目录是 Long Assistant 与 Long Grid 的小组件兼容协议、Schema、示例和实施交接资料。
- 资料当前属于规划基线，不表示 LongBetterWindows 已实现 Plugin API 1.1、Widget Broker 或 LPWP 1.0。
- 现有 Plugin API 1.0 兼容性和当前真实桌面验收、unsigned RC 收口顺序保持不变。

## 接手后检查

1. 先重新审计当前 Contracts SDK、Manifest Schema、Web Host 和 IPC 代码，不直接沿用交接文档中的旧路径。
2. 按 PR-A（共享协议）→ PR-B（Web Widget 与沙箱）→ PR-C（Broker）→ PR-D（兼容与发布门禁）分批实施。
3. 保持职责边界：Long Assistant 管理安装、权限和执行，Long Grid 管理布局和展示；Long Grid 不直接加载第三方 DLL、C# Script 或 WPF。
4. 每批同步协议 fixture、Schema、兼容测试、安全测试和两仓库版本状态。

协议入口见 [`protocol/README.md`](protocol/README.md)。
