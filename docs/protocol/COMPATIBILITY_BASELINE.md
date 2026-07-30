# Long助手兼容基线摘要

审计仓库：`Longyuyeee/Long_BetterWindows`
审计提交：`0d1366f`
审计日期：2026-07-30

## 已有能力

- Long助手当前 Plugin API 为 `1.0.0`。
- API 兼容规则为主版本相同，宿主 minor 不低于插件请求的 minor。
- Manifest 使用 JSON Schema Draft 2020-12，并启用严格的 `additionalProperties: false`。
- 支持 `.NET DLL`、C# Script 和 HTML/JavaScript WebView2 插件。
- 支持命令、能力、生命周期、窗口、后台运行、本地化、依赖和默认设置。
- Web Bridge 使用 `{id, method, args}` 请求和 `{id, result|error}` 响应。
- Web 插件已经限制包外顶层导航，并阻止新窗口和下载。
- 插件包支持确定性 `.lpak`、文件 SHA-256 总账、签名和事务安装/回滚。
- `.NET PluginSdk` 可以作为共享契约基础；WPF SDK 不能被 Long Grid 引用。

## 尚缺能力

- Manifest 没有 `widgets` 字段。
- 没有 Widget Definition、Widget Instance 和桌面表面类型。
- 没有 mounted、resize、visibility、suspend、resume、unmount 生命周期。
- 没有 `host.getInfo` 和 Widget Bridge 方法。
- 没有面向 Long Grid 的跨进程 Broker。
- 当前 Web 安全主要覆盖顶层导航，尚未完整约束子资源、远程脚本和稳定来源。
- 当前 `ui.floating_box` 创建的是 WPF Window + WebView2 独立窗口，不是桌面 Widget Host。

## 必须保留的边界

1. 插件安装、升级、启用和权限由 Long助手裁决。
2. Long Grid 不直接加载 DLL、C# Script 或 WPF 控件。
3. 现有命令插件通过 Broker 映射为动作卡。
4. 原生 WPF 插件在 Long Grid 中降级为“在 Long助手中打开”。
5. 只有声明 `widgets` 的 Web 插件才能成为完整桌面小组件。
6. Widget 页面不能自行控制顶层窗口位置、桌面层级或布局。
7. 两个宿主不能同时写 Long助手现有插件目录。
8. 旧插件继续按照 Plugin API `1.0` 语义运行。

## 协议升级结论

LPWP 1.0 建议对应 Plugin API `1.1.0`：

- `1.0.x` 插件保持兼容；
- 使用 `widgets` 的插件声明 `min_api_version: 1.1.0`；
- 插件市场按最低 API 阻止旧宿主安装新 Widget 包；
- 协议不兼容时必须明确拒绝或降级，不能静默执行。
