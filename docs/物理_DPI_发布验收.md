# Long助手物理 DPI 发布验收

> 更新日期：2026-08-21
> 目标：为 100%、125%、150%、200% 四档真实 Windows 缩放生成自动、哈希锁定的发布证据。

## 验收边界

- 必须在真实设置为 100%、125%、150%、200% 的显示环境分别采集。
- 采集器读取窗口所在显示器的实际 DPI；声明档位或指定设备不一致时失败。
- 工程离屏渲染、远程缩放、图片放大或手工修改元数据不能替代物理设备证据。
- 250% 可作为兼容性补充，不属于四档发布矩阵。

## 采集

在目标缩放生效并重新启动应用后执行：

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\capture-physical-dpi-evidence.ps1 `
  -OutputDirectory .\artifacts\physical-dpi-100-YYYYMMDD `
  -ExpectedSourceCommit "完整的 40 位候选源码提交" `
  -ExpectedScalePercent 100 `
  -MonitorDeviceName '\\.\DISPLAY1'
```

每档生成亮/暗主题下主界面、插件市场、命令面板和 URL Web 插件共 8 张截图及元数据。Manifest 使用 schema v3、分类 `automated_physical_device_dpi_evidence`，记录真实档位、文件与元数据 SHA-256 和自动检查结果。采集要求指定提交、干净受跟踪源码和重新构建；正式证据不得使用 `-NoBuild`。

## 汇总

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

汇总器只接受同一提交的四个唯一档位，要求每档 8 份真实文件、实际缩放一致、自动检查通过且全部哈希未变化。输出使用 schema v4、分类 `automated_physical_device_dpi_matrix`，并生成可移植 `.sources` 来源包。任何缺档、重复、篡改或失败都会关闭门禁，不再生成或读取人工批准字段。
