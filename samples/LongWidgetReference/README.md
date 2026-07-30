# Long Widget Reference

这是 LPWP 1.0 的可安装参考插件，覆盖：

- 单实例与多实例 Widget；
- 受信 `host.getInfo` 身份；
- `widget.ready`、实例状态、Badge 与刷新请求；
- resize、visibility、suspend/resume 等宿主事件；
- 响应式、键盘可达且不依赖远程资源的 Web UI；
- 生产验证、确定性 `.lpak` 和 SHA-256 文件总账。

从仓库根目录执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-reference-widget.ps1
```

默认成品写入 `artifacts/reference-widget-package/`，不会被提交。也可以使用
`-OutputDir` 指定其他目录。生成后在插件市场选择“导入本地 .lpak”，再打开
“桌面组件”，分别添加“专注脉搏”和多个“灵感计数”实例。

参考包只使用无需额外权限的 Widget Bridge，不包含 DLL、C# Script、远程请求
或系统副作用。
