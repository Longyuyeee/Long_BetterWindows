# LongBetterWindows.PluginSdk.Wpf

Long 助手原生插件的 WPF UI Kit。它只依赖 `LongBetterWindows.PluginSdk`，不依赖宿主程序集。

当前提供：

- `HotkeySettingsControl`：支持冲突检测、事务回滚、运行时本地化和 Long 语义主题资源的热键设置控件；
- `AnchoredTextEditorWindow`：支持锚点定位、失焦延迟保存、`Ctrl+Enter` 提交和 `Esc` 取消的轻量编辑 Surface；
- `MonitorHelper`：支持多显示器与 DPI 转换的窗口定位辅助。

宿主负责提供 `Long.*` 动态资源。插件必须从 `IHostApi` 显式传入能力服务，不应访问宿主静态服务定位器。
