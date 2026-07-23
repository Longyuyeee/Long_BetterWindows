# Long Design System 使用指南

Long Design System 是宿主、原生插件和 Web 插件共享的界面基础。视觉方向为深色石墨、电光紫、冷色高光和克制的层级光感；uTools 只作为交互结构参考，不作为视觉皮肤复制目标。

## WPF 使用方式

颜色只能引用语义画刷：

```xml
Background="{DynamicResource Long.Brush.Background.Base}"
Foreground="{DynamicResource Long.Brush.Text.Primary}"
BorderBrush="{DynamicResource Long.Brush.Stroke.Default}"
```

组件使用命名 Style：

```xml
<Button Style="{StaticResource LongButton.Primary}" Content="执行" />
<TextBox Style="{StaticResource LongSearchBox}" />
<Border Style="{StaticResource LongCard}">...</Border>
<ToggleButton Style="{StaticResource LongToggle}" />
```

核心样式包括：

- `LongButton`、`LongButton.Primary`、`LongButton.Danger`
- `LongIconButton`
- `LongTextBox`、`LongTextBox.Error`、`LongSearchBox`
- `LongCard`、`LongPluginCard`、`LongCommandItem`
- `LongBadge`、`LongHotkeyBadge`、`LongToggle`
- `LongToast`、`LongEmptyState`、`LongLoadingState`
- `LongNavigationItem`、`LongDialog`、`LongWindowChrome`

间距使用 `Long.Space.1` 至 `Long.Space.10`，圆角使用 `Long.Radius.*`，阴影使用 `Long.Shadow.Level1` 至 `Level3`。

## Web 插件使用方式

宿主会在 WebView 页面创建前自动注入 `long-ui.css` 和 `long-ui.js`，插件无需复制主题文件。推荐主动使用以下类名：

```html
<section class="long-card">
  <input class="long-input" placeholder="输入内容">
  <button class="long-button long-button--primary">执行</button>
  <span class="long-badge">内置</span>
</section>
```

可使用的脚本辅助方法：

```javascript
LongUI.setBusy(button, true)
LongUI.announce('处理完成')
```

内容区域的加载、空数据和失败状态统一使用 `LongUI.renderState`，业务页面不得再为同类状态声明私有 `.loading` 或 `.empty-state` 样式：

```javascript
LongUI.renderState(container, {
  kind: 'loading',
  title: '正在加载端口信息'
})

LongUI.renderState(container, {
  kind: 'error',
  title: '端口信息加载失败',
  detail: error.message,
  actionLabel: '重试',
  onAction: reload
})
```

`kind` 只接受 `empty`、`loading`、`error`。组件使用 `textContent` 写入标题和详情；加载态为容器设置 `aria-busy`，错误态使用 `role="alert"`，空态和加载态使用礼貌播报。调用 `LongUI.clearState(container)` 可清空状态并移除忙碌语义。

主题通过 `data-long-theme="dark|light"` 同步，减少动画遵循系统的 `prefers-reduced-motion` 设置。

## 约束

- 业务界面不得新增十六进制颜色；颜色值只存在于令牌文件中。
- 正式功能图标使用 `Icons.xaml` 中的矢量 Geometry，不使用 Emoji。
- 强阴影只用于全局入口和模态层，普通卡片使用 Level 1。
- 所有交互控件必须具备悬浮、按下、聚焦、禁用状态和自动化名称。
- 动画时长必须来自 `Long.Motion.*`，不得在业务页面散落毫秒值。

## 预览

打开 ToolCenter → 开发工具 → Long Design System → 预览，可检查当前主题下的组件样板。
