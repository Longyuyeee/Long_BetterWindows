# Long窗口物理 DPI 发布验收

> 建立日期：2026-07-19
> 目标：为 100%、125%、150%、200% 四档 Windows 物理缩放生成可追溯、不可混用工程离屏渲染的发布证据。

## 验收边界

- 正式矩阵必须来自真实设置为 100%、125%、150%、200% 的显示环境。
- 采集程序读取目标窗口所在显示器的 `actual_monitor_dpi`；与声明档位不一致时立即失败。
- 工程 `render_dpi` 图片、远程缩放、图片放大或修改元数据均不能代替物理设备证据。
- 自动校验通过不等于人工签核。人工检查和署名分为独立步骤。
- 250% 可作为补充设备基线，但不属于规范要求的四档发布矩阵。

## 每台设备的执行步骤

先在 Windows 显示设置中选择目标缩放，注销或重启需要重新加载缩放的应用，然后在对应设备执行：

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\capture-physical-dpi-evidence.ps1 `
  -OutputDirectory .\artifacts\physical-dpi-100-YYYYMMDD `
  -ExpectedSourceCommit "完整的 40 位候选源码提交" `
  -ExpectedScalePercent 100
```

脚本生成亮/暗主题下的主界面、插件市场、命令面板和 URL Web 插件，共 8 张图片及伴随元数据。测试人员逐张检查：

1. 无裁切、溢出、重叠或异常滚动条。
2. 中文、英文、数字和图标清晰。
3. 键盘焦点可见，主要操作不依赖鼠标悬停。
4. 亮色与暗色的信息层级一致。
5. Web 插件内容真实可见，不是空白 HWND 截图。

采集器为每个视图保留 90 秒进程预算，以覆盖高 DPI 下 WebView2 冷启动和连续视图采集；如设备性能较慢，可显式传入 `-ProcessTimeoutSeconds 120`，但不得使用 `-NoBuild` 绕过候选重建。

检查完成后显式批准现有证据：

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\approve-physical-dpi-evidence.ps1 `
  -EvidenceDirectory .\artifacts\physical-dpi-100-YYYYMMDD `
  -ExpectedSourceCommit "完整的 40 位候选源码提交" `
  -ConfirmScalePercent 100 `
  -Reviewer "测试人员姓名" `
  -ReviewNotes "亮暗主题和四类视图均已逐张检查" `
  -ConfirmVisualReview
```

125%、150%、200% 使用相同步骤，仅替换目录和缩放参数。批准脚本会在写入署名前重新核对图片与元数据哈希。

## 汇总发布矩阵

四档证据全部批准后执行：

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\verify-physical-dpi-matrix.ps1 `
  -EvidenceDirectories `
    .\artifacts\physical-dpi-100-YYYYMMDD, `
    .\artifacts\physical-dpi-125-YYYYMMDD, `
    .\artifacts\physical-dpi-150-YYYYMMDD, `
    .\artifacts\physical-dpi-200-YYYYMMDD `
  -ExpectedSourceCommit "完整的 40 位候选源码提交" `
  -OutputPath .\artifacts\quality\physical-dpi-release-matrix.json
```

采集要求仓库 `HEAD` 等于指定的完整提交 SHA、受跟踪文件无改动，并重新构建该提交；`-NoBuild` 不能用于正式物理证据。汇总门禁要求四个唯一档位来自同一个指定提交、每档 8 张图片、人工状态为 `approved`、文件哈希未变化、实际显示器缩放与档位一致。任一条件不满足即失败。

## 当前证据

- `artifacts/physical-dpi-250-20260719/physical-dpi-evidence.json`
- 实际显示器：240 DPI（250%）。
- 自动检查：通过，亮/暗主题共 8 张。
- 人工签核：`pending`。
- 发布矩阵成员：否，仅作为高缩放补充基线。
