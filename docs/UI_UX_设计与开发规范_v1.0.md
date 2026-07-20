# Long窗口 UI/UX 设计与开发规范 v1.0

> 文档状态：已确认，实施中
> 适用版本：v0.5.0 及后续版本
> 更新日期：2026-07-18
> 适用范围：宿主程序、内置插件、第三方插件 UI、插件开发工具、插件市场

> 产品边界：uTools 仅作为统一入口、上下文匹配和插件化工作流的交互参考；Long 不兼容 uTools 插件，不复制其 API、Manifest、运行时、包格式或视觉体系。

---

## 1. 文档目的

本规范用于统一 Long窗口 后续的产品交互、视觉设计、插件 UI、动效、主题、可访问性和工程实现方式。

Long窗口 的目标不是成为一组互不相关的小工具，而是成为一个运行在 Windows 之上的效率增强中枢：用户可以随时唤起、输入或选择内容、获得匹配能力、完成操作，并立即返回原工作状态。

本规范同时解决以下问题：

- 宿主、原生插件和 Web 插件视觉语言不一致。
- 快捷启动器、ToolCenter、插件功能之间缺少统一入口。
- 插件清单无法描述功能指令、输入类型和界面模式。
- 动效只有零散实现，没有统一运动语言。
- 深浅主题没有覆盖全部窗口和插件。
- 插件开发者缺少可直接复用的高质量 UI 组件。
- 缺少可访问性、性能和视觉回归验收标准。

---

## 2. 产品定位

### 2.1 产品愿景

打造一个优雅、华丽、扁平、动效自然，并且深度集成 Windows 的效率工具平台。

Long窗口 应具备以下特征：

1. **呼之即来**：用户在任何窗口都能快速唤起。
2. **搜索即操作**：搜索框不仅用于导航，也用于执行插件指令和处理内容。
3. **插件即能力**：插件通过标准契约向宿主注册功能，而不是形成孤立应用。
4. **即用即走**：执行结束后自动收起并返回原工作流。
5. **系统原生感**：窗口、快捷键、右键菜单、文件选择和通知自然融入 Windows。
6. **开放但有秩序**：第三方插件拥有设计自由，同时可以低成本复用 Long UI Kit。

### 2.2 参考边界

Long窗口 可以参考 uTools 的统一入口、功能指令、输入智能匹配、插件生态和低步骤交互逻辑。

Long窗口 不复制以下内容：

- uTools 的品牌、Logo、配色和图标。
- uTools 官网的营销页面视觉。
- uTools 软件界面的具体布局和素材。
- 与 Long窗口 Windows 深度集成目标无关的产品结构。

视觉品牌必须属于 Long窗口；交互参考用于解决效率问题，而不是进行外观复刻。

---

## 3. 当前基线与已知问题

### 3.1 当前基线

当前项目包含：

- 1 个 WPF 宿主程序。
- 16 个内置插件。
- 9 个 WebView2 插件 UI。
- 多个独立 WPF 窗口、浮层、HUD 和设置控件。
- 基础颜色资源、控件样式和动画辅助类。
- 快捷启动器、插件中心、开发者工具和文档浏览器。

### 3.2 核心问题

| 级别 | 问题 | 影响 |
|---|---|---|
| P0 | 插件清单没有功能指令和输入类型 | 无法建立真正的统一入口 |
| P0 | Web 插件各自维护 CSS | 内置插件观感和交互严重不一致 |
| P0 | 缺少统一窗口宿主 | 标题栏、尺寸、主题和生命周期不一致 |
| P1 | 快捷启动器仍是独立插件 | 搜索无法覆盖全部插件能力 |
| P1 | WebView2 提前初始化 | 插件数量增加后影响启动和内存 |
| P1 | 主题同步不完整 | 宿主、原生插件和 Web 插件可能不同步 |
| P1 | 动效缺少统一规范 | 动画时长、曲线和使用场景混乱 |
| P1 | 可访问性基础薄弱 | 键盘、读屏和高对比度体验不可验收 |
| P2 | 大量硬编码颜色与尺寸 | 主题切换和组件复用困难 |
| P2 | 缺少视觉回归体系 | UI 改动只能依赖人工主观检查 |

### 3.3 当前未提交 UI 草案

当前工作区存在一轮未提交 UI 草案。该草案只用于验证视觉方向，不作为正式设计基线。

可以保留并继续评估：

- 深色石墨背景与紫色品牌方向。
- 卡片、描边、圆角和空间层次。
- 统一命令入口概念。
- 插件开发工具的深色工作台方向。

需要重新设计：

- 占用首屏的大型营销式 Hero。
- 仅能匹配页面和插件名称的浅层命令搜索。
- 使用 `!important` 强制覆盖插件 DOM 的 CSS 注入方案。
- 所有 Web 插件统一使用固定窗口尺寸的方案。

在正式实施前，应先决定如何保存或回退该草案，不得直接将其作为完成版本提交。

---

## 4. 设计原则

### 4.1 效率优先

- 常用任务应在 1–3 次操作内完成。
- 全局入口默认聚焦输入框。
- 支持方向键、Enter、Esc、Tab 完整操作。
- 操作完成后尽量自动返回原应用。
- 页面不应使用大面积装饰阻挡核心功能。

### 4.2 华丽但克制

华丽来自材质、比例、光影、排版和动效，而不是堆叠渐变、阴影和发光。

- 每个页面最多一个主要视觉焦点。
- 主要操作使用品牌色，普通操作保持中性。
- 阴影表达层级，不用于装饰所有元素。
- 渐变只用于品牌、主按钮和关键状态。
- 背景应衬托内容，而不是与内容竞争。

### 4.3 扁平但有层级

- 使用描边、色差和空间表达层级。
- 不使用厚重拟物按钮和复杂立体边框。
- 卡片层级不得超过三层。
- 同层元素使用相同圆角、间距和高度。

### 4.4 动效服务于理解

- 动效必须解释界面变化、状态变化或操作反馈。
- 不对静态装饰元素进行持续动画。
- 高频操作的动画应短于 220ms。
- HUD、Toast 和命令入口应快速进入、快速退出。
- 必须支持 Windows“减少动画”设置。

### 4.5 插件开放性

- 内置插件必须遵循 Long Design System。
- 第三方插件可以选择标准 UI Kit 或完全自定义 UI。
- 宿主不得通过强制 CSS 覆盖破坏第三方插件设计。
- 宿主始终负责插件窗口外壳、主题信息、安全区域和生命周期。

---

## 5. 产品信息架构

Long窗口 分为两个主要使用面：

### 5.1 Command Palette：执行入口

Command Palette 用于快速完成任务，而不是管理系统。

支持：

- 搜索应用、文件、插件和功能指令。
- 计算表达式和打开 URL。
- 输入或粘贴文本、图片、文件、文件夹。
- 根据当前 Explorer 目录、选中项和剪贴板推荐功能。
- 固定常用指令。
- 根据使用频率和上下文排序。

Command Palette 应在任何应用中通过全局快捷键唤起。

### 5.2 ToolCenter：管理入口

ToolCenter 用于管理和配置，不承担日常快速执行职责。

建议导航结构：

1. **概览**
   - 平台状态
   - 常用指令
   - 最近使用
   - 更新和风险提示

2. **插件**
   - 已安装
   - 发现插件
   - 更新
   - 权限

3. **系统集成**
   - 右键菜单
   - Explorer 列
   - 开机自启
   - 全局快捷键

4. **开发者**
   - 创建插件
   - 开发工作台
   - 日志
   - 打包
   - API 文档

5. **设置**
   - 外观
   - 动效
   - 数据
   - 隐私
   - 关于

### 5.3 Context Panel：上下文入口

作为后续阶段能力，根据用户选中的文本、图片、文件或文件夹显示可执行功能。

Context Panel 不应在 v0.5.0 第一阶段阻塞 Command Palette 和 ToolCenter 的完成。

---

## 6. 统一命令系统

### 6.1 插件命令模型

建议为插件增加以下契约：

```csharp
public sealed class PluginCommand
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public IReadOnlyList<AcceptedInputType> AcceptedInputs { get; init; } = [];
    public PluginViewMode ViewMode { get; init; }
    public bool KeepAlive { get; init; }
}
```

```csharp
public enum AcceptedInputType
{
    None,
    Text,
    Url,
    Image,
    File,
    Files,
    Folder,
    Clipboard,
    ExplorerSelection
}
```

```csharp
public enum PluginViewMode
{
    None,
    List,
    Form,
    Document,
    Custom
}
```

### 6.2 Manifest 示例

```json
{
  "id": "com.long.translate",
  "name": "翻译工具",
  "version": "1.0.0",
  "entry_point": "index.html",
  "runtime": "webview",
  "commands": [
    {
      "id": "translate.text",
      "title": "翻译文本",
      "aliases": ["翻译", "fy", "translate"],
      "accepted_inputs": ["text", "clipboard"],
      "view_mode": "form"
    }
  ],
  "window": {
    "mode": "standard",
    "preferred_width": 680,
    "preferred_height": 520,
    "min_width": 480,
    "min_height": 360
  }
}
```

### 6.3 搜索排序

建议综合以下信号：

1. 指令标题完全匹配。
2. 指令别名完全匹配。
3. 标题前缀匹配。
4. 拼音和拼音首字母匹配。
5. 模糊匹配。
6. 当前输入类型匹配。
7. 当前应用和 Explorer 上下文匹配。
8. 使用频率和最近使用时间。
9. 用户固定权重。

### 6.4 键盘交互

| 按键 | 行为 |
|---|---|
| `Alt + Space` 或用户自定义 | 唤起 Command Palette |
| `↑ / ↓` | 切换结果 |
| `Enter` | 执行当前结果 |
| `Tab` | 切换分组或参数 |
| `Shift + Tab` | 反向切换 |
| `Esc` | 返回上一级或关闭 |
| `Ctrl + K` | 在 ToolCenter 内聚焦命令搜索 |
| `Ctrl + ,` | 打开设置 |

不得将键盘支持作为鼠标功能完成后的附加任务，应与控件同时实现。

---

## 7. Long Design System

### 7.1 品牌方向

建议采用“深色石墨 + 电光紫 + 冷色高光”的 Long 品牌方向。

关键词：

- 深邃
- 精密
- 柔和光感
- 克制华丽
- Windows 原生融合
- 高信息密度下仍然清晰

### 7.2 颜色令牌

所有颜色必须通过语义令牌引用，不允许业务控件直接使用十六进制颜色。

```text
Background/Base
Background/Raised
Surface/Card
Surface/Hover
Surface/Overlay
Stroke/Default
Stroke/Strong
Text/Primary
Text/Secondary
Text/Muted
Accent/Primary
Accent/Hover
Accent/Soft
State/Success
State/Warning
State/Danger
```

颜色值可以调整，但语义名称必须稳定。

### 7.3 字体

WPF 推荐字体栈：

```text
Segoe UI Variable Text
Segoe UI
Microsoft YaHei UI
```

代码字体：

```text
Cascadia Code
Consolas
```

建议字号：

| 用途 | 字号 | 字重 |
|---|---:|---|
| 页面主标题 | 24–28 | SemiBold |
| 页面标题 | 18–20 | SemiBold |
| 卡片标题 | 14 | SemiBold |
| 正文 | 12–13 | Regular |
| 辅助信息 | 11 | Regular |
| 徽章 | 10 | Medium |

### 7.4 间距

基础间距单位为 4px。

推荐序列：

```text
4 / 8 / 12 / 16 / 20 / 24 / 32 / 40
```

- 同一控件内部使用 4–12px。
- 卡片内部使用 16–24px。
- 页面区块之间使用 24–32px。
- 不允许随意出现 13、17、19 等无语义间距。

### 7.5 圆角

| 类型 | 圆角 |
|---|---:|
| 小徽章、快捷键 | 6 |
| 按钮、输入框 | 8–10 |
| 卡片 | 12–14 |
| 浮层、命令面板 | 16–18 |
| 品牌视觉区域 | 20–24 |

### 7.6 阴影

阴影只表达层级：

- Level 0：无阴影，普通页面内容。
- Level 1：轻阴影，卡片和工具栏。
- Level 2：中等阴影，下拉面板和弹出菜单。
- Level 3：强阴影，模态框和全局命令入口。

禁止对所有按钮和卡片同时使用强阴影。

### 7.7 图标

- 使用统一矢量图标库。
- 不使用 Emoji 作为正式功能图标。
- 图标默认尺寸为 16、20、24。
- 图标应支持主题颜色和 DPI 缩放。
- 只有状态图标可以使用语义色。

---

## 8. 核心组件

第一阶段必须实现以下 WPF 与 Web 对应组件：

- `LongButton`
- `LongIconButton`
- `LongTextBox`
- `LongSearchBox`
- `LongCard`
- `LongCommandItem`
- `LongPluginCard`
- `LongBadge`
- `LongHotkeyBadge`
- `LongToggle`
- `LongDialog`
- `LongToast`
- `LongEmptyState`
- `LongLoadingState`
- `LongNavigationItem`
- `LongWindowChrome`

组件必须支持：

- 暗色和亮色主题。
- 禁用、悬浮、按下、聚焦和错误状态。
- 键盘导航。
- 自动化名称。
- 减少动画模式。
- 100%–200% DPI。

---

## 9. 窗口系统

### 9.1 窗口模式

```csharp
public enum PluginWindowMode
{
    Compact,
    Standard,
    Wide,
    Document,
    Overlay,
    FullScreen
}
```

| 模式 | 适用场景 |
|---|---|
| Compact | 颜色拾取、快速输入、小工具 |
| Standard | 翻译、重命名、密码生成 |
| Wide | 文本对比、Markdown、开发工具 |
| Document | 文档浏览和长内容 |
| Overlay | HUD、Toast、录制状态 |
| FullScreen | 截图选区和沉浸操作 |

### 9.2 PluginWindowHost 职责

- 统一窗口标题栏和系统按钮。
- 读取插件建议尺寸。
- 保存和恢复窗口位置。
- 同步主题和 DPI。
- 管理 WebView2 生命周期。
- 处理打开、隐藏、关闭和后台驻留。
- 提供进入和退出动效。
- 注入安全区域和主题变量。

### 9.3 WebView2 生命周期

- 插件扫描时只读取 Manifest，不创建 WebView2。
- 第一次打开插件时延迟创建 WebView2。
- 可配置关闭后缓存或释放。
- 同一插件窗口重复打开时不得重复挂载同一 WebView 控件。
- 插件停止或卸载时必须释放 WebView2。

---

## 10. Web 插件 UI Kit

### 10.1 使用原则

Long UI Kit 应作为版本化、可选的标准包提供：

```html
<link rel="stylesheet" href="long://ui/v1/long-ui.css">
<script src="long://ui/v1/long-ui.js"></script>
```

第三方插件可以：

1. 完整使用 Long UI Kit。
2. 只使用主题变量。
3. 完全自定义 UI。

宿主不得通过大量 `!important` 强制改变第三方页面。

### 10.2 主题变量

```css
:root {
  --long-bg-base: ...;
  --long-bg-raised: ...;
  --long-surface-card: ...;
  --long-text-primary: ...;
  --long-text-secondary: ...;
  --long-accent-primary: ...;
  --long-stroke-default: ...;
  --long-radius-control: ...;
  --long-radius-card: ...;
  --long-motion-fast: ...;
  --long-motion-standard: ...;
}
```

宿主主题变化时通过 JS 事件通知：

```javascript
long.ui.onThemeChanged(theme => {
  document.documentElement.dataset.theme = theme;
});
```

### 10.3 官方插件要求

所有官方 Web 插件必须：

- 使用 Long UI Kit。
- 不使用业务硬编码主题颜色。
- 为输入控件提供 `<label>` 或 `aria-label`。
- 支持键盘操作。
- 支持深浅主题。
- 支持减少动画。
- 不在主线程执行长时间文件扫描。

---

## 11. 动效系统

### 11.1 动效令牌

```text
Motion/Instant: 80ms
Motion/Fast: 140ms
Motion/Standard: 200ms
Motion/Emphasis: 280ms
Motion/Entrance: 320ms
```

标准曲线：

```text
Standard: cubic-bezier(0.2, 0.8, 0.2, 1)
Enter: cubic-bezier(0.16, 1, 0.3, 1)
Exit: cubic-bezier(0.4, 0, 1, 1)
```

### 11.2 使用场景

| 场景 | 动效 |
|---|---|
| Command Palette 打开 | 轻微上移 + 淡入 + 细微缩放 |
| 搜索结果更新 | 交叉淡化或短距离位移 |
| 页面切换 | 8–12px 位移 + 淡入 |
| 按钮悬浮 | 颜色与描边变化，必要时轻微位移 |
| 按钮按下 | 0.97–0.99 缩放 |
| Toast | 边缘滑入 + 自动淡出 |
| Dialog | 背景遮罩淡入 + 内容轻微缩放 |
| 加载 | 骨架或低干扰进度反馈 |

### 11.3 禁止事项

- 高频列表项使用弹簧抖动。
- 所有卡片同时持续发光。
- 通过动画隐藏性能问题。
- 动画期间阻塞用户输入。
- 忽略 Windows 减少动画设置。

---

## 12. 主题与可访问性

### 12.1 主题要求

必须支持：

- 暗色主题。
- 亮色主题。
- 跟随系统。
- Windows 高对比度。
- 主题实时同步到已打开插件。

应用启动时必须同时应用 WPF-UI 主题和 Long 自定义资源，不允许只更新其中一套。

### 12.2 可访问性要求

- 所有按钮、输入框和图标按钮提供可访问名称。
- 不只依靠颜色表达状态。
- 文字与背景对比度符合 WCAG AA。
- 焦点状态必须可见。
- 所有核心流程可以只用键盘完成。
- Web 插件提供合理的 HTML 语义和 ARIA。
- 支持系统文字缩放和 200% DPI。
- 动画关闭后不影响功能理解。

---

## 13. 开发架构建议

### 13.1 宿主层新增模块

```text
Interaction/
  CommandRegistry.cs
  CommandSearchEngine.cs
  CommandHistoryService.cs
  ContextInputService.cs
  CommandPaletteController.cs

UI/
  DesignTokens/
  Components/
  Windows/
  Motion/
  Themes/

PluginUI/
  PluginWindowHost.cs
  PluginViewFactory.cs
  WebThemeBridge.cs
  WebUiKitProvider.cs
```

### 13.2 数据驱动 UI

插件列表、命令结果和设置页面应逐步从代码动态创建控件迁移至：

- ViewModel
- `ItemsControl` / `ListBox`
- `DataTemplate`
- `ICommand`
- 虚拟化面板

这样才能可靠实现：

- 搜索和排序。
- 状态更新。
- 主题切换。
- 动画。
- 自动化测试。
- 大量插件下的性能。

---

## 14. 分阶段开发计划

### 阶段 0：冻结和评审草案

目标：避免在未确认架构前继续扩大 UI 改动。

任务：

- 保存当前未提交草案的差异。
- 标注可保留和需要重做的部分。
- 确认品牌方向和信息架构。
- 建立原始界面截图基线。

验收：

- 草案未混入正式功能提交。
- 产品入口和 ToolCenter 职责得到确认。

### 阶段 1：统一交互协议

目标：让所有插件能力能够注册到统一入口。

任务：

- 扩展 PluginManifest。
- 实现 PluginCommand 和输入类型。
- 实现 CommandRegistry。
- 实现搜索排序、别名和拼音匹配。
- 将 QuickLaunch 能力迁入宿主命令入口。

验收：

- 至少 5 个内置插件可以通过功能指令调用。
- 支持文本、文件和 Explorer 选区三种输入。
- 全键盘流程可用。

### 阶段 2：Long Design System

目标：建立 WPF 与 Web 共用的设计语言。

任务：

- 确认设计令牌。
- 实现核心 WPF 组件。
- 实现 Web UI Kit。
- 实现主题桥接和减少动画。
- 建立图标资源。

验收：

- 核心组件覆盖所有状态。
- 深浅主题切换没有硬编码残留。
- 组件示例页通过设计评审。

### 阶段 3：重做核心界面

目标：完成日常使用入口和管理后台。

任务：

- 完成全局 Command Palette。
- 重构 ToolCenter 导航。
- 实现 PluginWindowHost。
- 完成插件管理和设置框架。
- 实现统一 Toast、Dialog、HUD。

验收：

- Command Palette 可以全局唤起并失焦收起。
- ToolCenter 不再承担快速执行职责。
- 所有新窗口使用统一 WindowHost。

### 阶段 4：迁移内置插件

目标：让全部官方插件形成统一体验。

任务：

- 迁移 9 个 Web 插件。
- 迁移原生插件窗口。
- 补充无 UI、列表、表单和文档模板。
- 更新开发文档和示例插件。

验收：

- 16 个内置插件全部通过 UI 检查表。
- 每个插件拥有功能指令。
- Web 插件全部支持主题同步和键盘操作。

### 阶段 5：性能与质量

目标：建立可以持续迭代的 UI 质量保障。

任务：

- WebView2 延迟加载。
- 插件列表虚拟化。
- 搜索索引和取消机制。
- UI 自动化测试。
- 视觉回归截图。
- DPI、对比度和减少动画测试。

验收：

- 热唤起达到性能目标。
- 20 个插件状态下达到内存目标。
- 暗色、亮色和多 DPI 视觉回归通过。

---

## 15. 验收指标

### 15.1 性能

| 指标 | 目标 |
|---|---:|
| Command Palette 热唤起到可输入 | ≤250ms |
| Command Palette 冷启动到可输入 | ≤700ms |
| 普通搜索结果更新 | ≤100ms |
| 插件页面首次可交互 | ≤1000ms |
| 20 个插件宿主内存 | <200MB |
| 动画期间主线程卡顿 | 无明显掉帧 |

### 15.2 视觉

- 720×560 至宽屏尺寸无内容溢出。
- 100%、125%、150%、200% DPI 正常。
- 暗色和亮色主题无不可读文字。
- 主要页面只使用语义令牌。
- 官方插件不包含未经批准的独立主题。

### 15.3 交互

- Command Palette 全流程可用键盘完成。
- Esc 行为在所有窗口中一致。
- 插件执行后正确返回原工作状态。
- 错误、加载、空状态和权限拒绝均有明确反馈。
- 快捷键冲突可以定位到占用插件。

### 15.4 可访问性

- 核心控件拥有自动化名称。
- Web 插件表单拥有 Label 或 ARIA。
- 对比度达到 WCAG AA。
- 高对比度模式可用。
- 减少动画模式可用。

---

## 16. 测试策略

### 16.1 单元测试

- 命令匹配与排序。
- 输入类型匹配。
- Manifest 兼容性。
- 主题令牌解析。
- 窗口尺寸约束。

### 16.2 集成测试

- 全局快捷键唤起。
- 命令执行和插件跳转。
- WebView2 延迟初始化。
- 主题实时切换。
- 插件关闭和重复打开。

### 16.3 UI 自动化

- 键盘焦点路径。
- Enter、Esc、Tab 和方向键。
- 插件安装、启用、禁用和打开。
- 设置保存和重启恢复。

### 16.4 视觉回归

每个正式 UI 变更至少保存以下基线：

- 暗色主题 100% DPI。
- 亮色主题 100% DPI。
- 暗色主题 150% DPI。
- 空状态、加载状态和错误状态。
- Command Palette 无输入、有输入和多类型输入。

---

## 17. 代码审查清单

提交 UI 代码前必须检查：

- [ ] 是否使用语义设计令牌？
- [ ] 是否存在无必要的硬编码颜色？
- [ ] 深浅主题是否都验证？
- [ ] 键盘是否可以完成操作？
- [ ] 是否提供焦点和可访问名称？
- [ ] 是否支持减少动画？
- [ ] 动画是否使用统一时长和曲线？
- [ ] WebView2 是否延迟加载并正确释放？
- [ ] 是否避免强制覆盖第三方插件 DOM？
- [ ] 窗口尺寸是否来自标准模式或 Manifest？
- [ ] 是否验证 125% 和 150% DPI？
- [ ] 是否补充测试和视觉回归截图？

---

## 18. 版本与兼容策略

- UI Kit 使用独立语义版本，例如 `long-ui/v1`。
- Manifest 新字段必须提供默认值，旧插件应继续加载。
- 破坏性插件 UI API 变更必须提升主版本。
- 废弃组件至少保留两个小版本。
- 宿主应允许插件声明最小 UI Kit 版本。
- 第三方自定义 UI 不受官方视觉升级强制影响。

---

## 19. 推荐实施顺序

严格按照以下顺序实施：

```text
插件命令与输入协议
        ↓
Long Design System
        ↓
Command Palette + ToolCenter
        ↓
PluginWindowHost
        ↓
内置插件迁移
        ↓
动效、可访问性、性能与视觉回归
```

不得在协议和设计系统稳定前批量重写全部插件页面。

---

## 20. 最终目标

完成本规范后，Long窗口 应表现为一个统一的平台：

- 用户只需要记住一个入口。
- 插件能力可以处理不同类型的上下文输入。
- ToolCenter 专注管理，Command Palette 专注执行。
- 宿主和内置插件拥有统一但不单调的 Long 视觉语言。
- 动效增强理解和品质，不拖慢操作。
- 第三方开发者既能快速获得高质量 UI，也保留设计自由。
- UI 质量可以通过性能、可访问性和视觉回归持续验收。
