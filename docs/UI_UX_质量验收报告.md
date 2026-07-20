# Long窗口 UI/UX 阶段 5 与交互平台质量验收报告

> 历史基线：本文的 20 插件、188 项测试和 v0.5.0-rc.1 数据属于合并前候选线证据。当前 v1.9 状态以 `docs/当前开发状态.md` 为准。

> 验收日期：2026-07-18
> 基准规范：`UI_UX_设计与开发规范_v1.0.md`
> 结论：**本机工程基线条件通过**

## 1. 结论摘要

阶段 5 已完成本机可执行的性能、视觉、可访问性、回归测试和发布包验证。核心交互指标全部达到规范目标，20 个互不重复的内置插件可从正式 `win-x64` 发布目录完整扫描和运行。

当前不直接标记为“全矩阵通过”，原因是 100%、125%、150%、200% DPI 的完整物理设备组合仍需在发布设备矩阵中复核。20 个互不重复真实插件的重复空闲内存采样已经达到门槛。

## 2. 验收环境

| 项目 | 环境 |
|---|---|
| 操作系统 | Windows，Asia/Shanghai |
| SDK | .NET SDK 10.0.100，目标框架 `net8.0-windows` |
| 发布目标 | `Release / win-x64 / framework-dependent` |
| 当前 DPI | 240 DPI（250%）实际渲染 |
| 插件规模 | 正式包 20 个互不重复插件，共 37 条命令 |
| 自动化测试 | 118 项 |

## 3. 性能验收

| 指标 | 目标 | 本机结果 | 状态 |
|---|---:|---:|---|
| Command Palette 热唤起到可输入 | ≤250ms | 34.4ms | 通过 |
| Command Palette 冷启动到可输入 | ≤700ms | 147.5ms | 通过 |
| 统一搜索首批结果 | ≤100ms | 1000 命令、20 次采样 P95 `<100ms`；Release 冷启动 12.1–14.2ms，动态查询 2.2ms | 通过 |
| Long 超级面板呼出到可见 | ≤250ms | Release 活上下文 166.9ms | 通过 |
| Long 超级面板首批结果 | ≤100ms | 活上下文首批 12.3ms，上下文刷新首批 2.1ms | 通过 |
| Web 插件首次可交互 | ≤1000ms | Base64 最终发布包首次命令 742.4ms | 通过 |
| 20 个插件宿主内存 | <200MB | 真实组合 5 次空闲工作集，中位数 193.3MB、最大值 194.6MB | 通过 |
| 动画期间主线程卡顿 | 无明显掉帧 | 本机亮/暗主题交互未观察到明显卡顿 | 通过 |

性能相关实现包括：搜索请求取消与防抖、Provider 并发与 900ms 单源超时、异步增量批次、插件列表回收虚拟化、非活动页面按需构建、WebView2 首次使用时初始化、隔离插件扫描目录，以及进程内匿名性能测量日志。搜索日志只记录首批/总耗时与数量，不记录查询、路径、剪贴板或结果内容。

v0.6 真实搜索补充数据：

| 场景 | 首批结果 | 总耗时 | Provider / 批次 / 结果 | 状态 |
|---|---:|---:|---:|---|
| Release 冷启动空查询 | 12.1–14.2ms | 17.4–19.6ms | 2 / 2 / 12 | 通过 |
| Release 动态“启动”查询 | 2.2ms | 23.3ms | 2 / 2 / 4 | 通过 |

自动化另行覆盖快速 Provider 在慢 Provider 完成前先行展示、单 Provider 超时不隐藏其他结果、异常隔离、稳定 ID 去重和偏好排序。真实运行日志未出现搜索 Provider 超时、失败或未处理异常。

20 个互不重复真实插件压力场景补充数据：

连续 5 次独立启动均加载 20 个插件与 37 条命令，工作集分别为 193.3、193.4、187.1、194.6、187.5MB；中位数 193.3MB，最大值 194.6MB。证据见 `artifacts/quality/20-real-plugins-20260719/plugin-memory-report.json`。插件市场页面改为首次访问时按需构建，避免启动阶段创建未显示的市场视图。

| 场景 | 宿主工作集 | 宿主私有内存 | 完整进程树工作集 | 说明 |
|---|---:|---:|---:|---|
| 空闲 | 188.2MB | 171.7MB | 188.2MB | 达到宿主 `<200MB` 门槛 |
| Command Palette | 218.6MB | 378.1MB | 218.6MB | 移除透明窗口后降低 9.2MB，仍作为交互峰值观察项 |
| Base64 Web 插件 | 197.5MB | 203.8MB | 538.5MB | 宿主达标；进程树包含 6 个 WebView2 子进程 |

规范中的“宿主内存”按宿主进程口径验收；WebView2 进程树数据单列，避免将两个口径混为同一结论。

## 4. 视觉与 DPI 验收

- 主窗口和 Command Palette 已完成亮色、暗色、250% DPI 实际截图检查。
- 主窗口最小尺寸已从 900×600 对齐为规范要求的 720×560，并完成亮/暗 250% DPI 实际截图。
- ToolCenter 在窄视口下压缩导航与内容边距，状态卡转为纵向排列，未发现横向裁切。
- 未发现标题栏、导航、卡片、输入框、结果列表或滚动条裁切。
- 命令面板滚动条已替换为 Long Design System 语义样式。
- 宿主清单启用 `PerMonitorV2` 与 `True/PM`。
- 150% DPI 已在设计系统阶段完成实际渲染检查；100%、125%、200% 仍列入正式设备矩阵。
- 新增应用内确定性截图入口，以 WPF 逻辑尺寸分别输出 96、120、144、192 DPI PNG，并在伴随元数据中分别记录 `render_dpi` 与当前显示器真实 `actual_monitor_dpi`。
- 亮/暗主题 × 主界面/插件市场/命令面板 × 4 档目标渲染 DPI 共 24 张工程矩阵已生成；24 / 24 文件存在且 SHA-256 与清单一致，代表性 100%、150%、200% 图像抽查未发现裁切、错位、系统白底或滚动条异常。
- 当前显示器真实值为 240 DPI（250%）。工程离屏渲染不能替代物理 100%、125%、150%、200% 显示设备，因此正式设备矩阵仍保持未签核状态。
- 已建立 `capture-physical-dpi-evidence.ps1`、`approve-physical-dpi-evidence.ps1` 与 `verify-physical-dpi-matrix.ps1` 三段式门禁：采集时拒绝声明 DPI 与真实显示器 DPI 不一致，批准时复核证据哈希，汇总时要求四档各 8 张且具备明确人工署名。
- 当前 250% 设备已生成亮/暗 × 主界面/市场/命令面板/Web 插件共 8 张补充证据；实际 DPI 均为 240，自动检查通过，人工状态为 `pending`，且明确标记为非正式四档矩阵成员。

视觉基线：

- `artifacts/visual-matrix-20260719/visual-matrix.json`（24 张工程矩阵的尺寸、真实/目标 DPI、哈希与边界声明）
- `artifacts/physical-dpi-250-20260719/physical-dpi-evidence.json`（250% 真实显示器补充证据）

- `artifacts/visual-regression/light-250-main.png`
- `artifacts/visual-regression/dark-250-main.png`
- `artifacts/visual-regression/light-250-command-palette.png`
- `artifacts/visual-regression/dark-250-command-palette.png`
- `artifacts/visual-regression/light-250-main-720x560.png`
- `artifacts/visual-regression/dark-250-main-720x560.png`
- `artifacts/visual-regression/dark-250-command-palette-context-settled.png`
- `artifacts/visual-regression/dark-250-command-palette-image-context.png`
- `artifacts/visual-regression/dark-250-dynamic-search-mixed.png`
- `artifacts/visual-regression/dark-250-dynamic-search-scoped.png`
- `artifacts/visual-regression/dark-250-pinned-recent-result.png`
- `artifacts/visual-regression/dark-250-super-panel-context.png`
- `artifacts/visual-regression/dark-250-super-panel-groups-pinned.png`
- `artifacts/visual-regression/dark-250-super-panel-custom-folder.png`
- `artifacts/visual-regression/dark-250-embedded-plugin-session.png`
- `artifacts/visual-regression/dark-new-url-toolkit.png`
- `artifacts/visual-regression/dark-new-timestamp-converter.png`
- `artifacts/visual-regression/dark-new-regex-tester.png`
- `artifacts/visual-regression/dark-new-uuid-generator.png`

四个新增 Web 插件使用 WebView2 原生预览接口采集，伴随元数据的 `capture_kind` 均为 `webview_preview`，避免 WPF `RenderTargetBitmap` 的 HWND 空域限制导致内容空白。

超级面板真实交互验收已覆盖：窗口句柄级滚轮消息可在智能、固定、最近三组间切换；固定组完成一次真实鼠标拖拽，界面显示“固定顺序已保存”，重载后的稳定结果 ID 顺序与拖拽结果一致，且拖拽过程未误执行命令。

自定义文件夹真实交互验收已覆盖：通过“+”创建分组后，窗口级滚轮返回固定组，将真实卡片拖至文件夹标签，存储中出现对应稳定结果 ID；插件窗口关闭与宿主正常退出两条路径均记录 WebView2 释放，进程前后新增 WebView2 残留为 0。

统一窗口与鼠标手势验收已覆盖：Base64 插件按 Manifest 进入主窗口嵌入模式，可无重载分离并从分离窗口返回管理中心，返回后新增 WebView2 残留为 0；真实短右键仍打开 TextBox 原生菜单，真实 720ms 长按右键打开超级面板且不再出现该次菜单。

## 5. 可访问性验收

- WPF 核心输入与操作控件补充自动化名称。
- Web 插件表单补充 Label 或 ARIA，并恢复 `:focus-visible` 焦点环。
- 应用监听 Windows 高对比度和客户端动画设置，运行时更新语义颜色与动效时长。
- Web UI Kit 响应 `prefers-reduced-motion`。
- 自动化对比度测试确认亮色次要文字、暗色品牌色和强调色达到 WCAG AA 4.5:1 门槛。
- ToolCenter 的 `Ctrl+Tab` / `Ctrl+Shift+Tab` 页面循环逻辑已修正。
- 进程级高对比度与减少动画联合冒烟通过，日志确认两项均已启用且插件加载 20 / 20。
- 新增高对比度、减少动画、组合模式 × 96/192 DPI × 主界面/插件市场/命令面板共 18 张可访问性工程矩阵；18 / 18 状态元数据与预期一致，SHA-256 全部通过。
- 高对比度时品牌渐变压平为 Windows 系统 Highlight 色；代表性市场页和 200% 命令面板抽查确认焦点边框、选中态、文字、滚动条与卡片边界清晰，无系统白底污染或内容裁切。
- 工程强制参数验证资源切换与布局结果，但不等同于用户真实切换 Windows 高对比度/关闭动画后的完整键盘操作，因此人工键盘路径门槛继续保留。

可访问性矩阵清单：`artifacts/accessibility-matrix-20260719/visual-matrix.json`。

桌面 UI 自动化补充证据：`artifacts/quality/desktop-ui-smoke-20260719-final2/desktop-ui-smoke.json`。该 smoke 启动真实 Release 宿主，验证 Command Palette 搜索焦点、`wifi` 设置检索、`Shift+Enter` 次级动作复制 URI、动作后窗口保持，以及 Command Palette / 超级面板的 `Escape` 关闭链路。脚本使用进程范围的顶层窗口枚举和稳定 AutomationId，不扫描或操作其他应用窗口。

第二批桌面 UI 证据：`artifacts/quality/desktop-ui-smoke-batch2-20260719-final/desktop-ui-smoke.json`。在第一批基础上新增可访问性菜单 Invoke、超级面板到命令中心的窗口交接，以及 Base64 Web 插件从嵌入模式分离并通过 `Escape` 返回管理中心的真实生命周期验证；同时通过回归门禁锁定 `Shift+Enter` 必须在异步执行前阻止事件冒泡。

第三批桌面 UI 证据：`artifacts/quality/desktop-ui-smoke-batch3-20260719-final/desktop-ui-smoke.json`。新增可信市场目录加载、零结果恢复和卸载确认取消验证；脚本明确不调用确认卸载，复核后已安装状态保持。高对比度、减少动画和组合模式分别以独立 Release 进程运行，启动日志确认请求状态生效，且命令中心焦点与关闭链路均通过。

第四批桌面 UI 证据：`artifacts/quality/marketplace-ui-transaction-20260720-final/marketplace-transaction.json`。脚本在一次性隔离根内生成签名 v1/v2 测试包和独立信任库，通过真实 Release 界面完成安装、升级、版本回退、卸载，并在退出时删除测试私钥、包和隔离插件目录。报告确认可信签名、四段生命周期、隔离根清理全部通过，且正式 Release 插件目录前后指纹一致。

第五批桌面 UI 证据：`artifacts/quality/marketplace-failure-recovery-20260720-final/marketplace-transaction.json`。在完整生命周期基础上增加篡改哈希与伪造签名更新，界面均明确拒绝且确认动作保持禁用，已安装 v1 未变化；随后人工构造升级移动完成但事务未提交的现场，真实 Release 宿主重启后自动恢复 v1 并清除事务目录。安装器行为测试另行覆盖首次安装中断撤销和已提交事务残留清理。

第六批网络韧性证据：`artifacts/quality/marketplace-network-resilience-20260720-final/network-resilience.json`。受控传输测试确认瞬时断网可有限重试恢复、持续超时在预算内终止、相同包并发请求合并为一次传输、异常退出遗留分片自动回收、可信目录离线回退保持可用；并发版本安装由事务门串行化，最终 Manifest 有效且无事务目录残留。

## 6. 构建、测试与发布包

| 检查项 | 结果 |
|---|---|
| Debug 解决方案构建 | 通过，0 警告、0 错误 |
| 自动化测试 | 188 / 188 通过 |
| Release win-x64 发布 | 通过 |
| 发布包插件目录 | 20 个目录、20 个 Manifest |
| 发布包插件扫描 | 20 / 20 加载成功，共 37 条命令 |
| 发布包真实命令 | URL、时间戳、正则、UUID 四个新增插件命令均成功，退出码 0 |
| WebView2 进程释放 | 执行前后均为 14 个，无新增残留 |

发布过程中发现并修复了两个关键问题：WebView2 在控件进入视觉树前初始化会等待，以及宿主的 `win-x64` 运行时标识被透传给原生插件项目。两项均已由重新发布和真实命令冒烟验证。

## 7. 未关闭的发布门槛

1. 在 100%、125%、150%、200% DPI 上分别复核亮/暗主题、主窗口、Command Palette 与至少一个 Web 插件窗口。
2. 在 Windows 高对比度与“关闭动画效果”的真实系统设置下完成一次人工键盘路径验收。

上述两项完成后，阶段 5 可由“条件通过”升级为“正式通过”。当前发布候选冻结条件见 `发布候选检查表.md`。
