# Long助手原生性能 WPR 采样验收

## 目的

本流程用于定位 WPF、D3D、DWM 或显示驱动链中的间歇性 CPU 与合成活动。它补充现有托管 Dispatcher、窗口消息和工作集探针，不替代严格 `<200 MB` 内存门禁。

采集使用 `CPU.Light` 与 `DesktopComposition.Verbose` 两个 WPR 配置。原始 ETL 可能包含机器、进程和文件路径信息，必须保留在本地 `artifacts/quality/`，不得提交到 GitHub。

## 1. 普通会话预检

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\capture-native-performance-evidence.ps1 `
  -PreflightOnly
```

只有 `windows=true`、`administrator=true`、`wpr_available=true`、`ready=true` 时才能正式采集。普通 Codex/终端会话即使账户属于 Administrators，也可能因令牌未提升而显示 `administrator=false`。

## 2. 管理员采集

先确认跟踪工作区干净，再从“以管理员身份运行”的 PowerShell 执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\capture-native-performance-evidence.ps1 `
  -OutputDirectory .\artifacts\quality\native-performance-<commit>
```

脚本会：

1. 验证管理员令牌、WPR、干净 Git 提交和 25 个正式插件；
2. Release 构建宿主；
3. 同时启动 `CPU.Light` 与 `DesktopComposition.Verbose`；
4. 运行 `1120×760` 插件页和 9 秒质量空闲探针；
5. 生成压缩 ETL、插件页结构化性能报告及 SHA-256 Manifest；
6. 将 `analysis_status` 固定为 `pending_analysis`，并保持 `release_gate_passed=false`。

采集异常时脚本只取消自己成功启动的 WPR 会话，并终止自己启动且超时的宿主进程。
Manifest 只在 ETL 与性能报告全部完成后不可覆盖地原子创建。中断目录若只有 ETL、运行目录或性能报告而没有完整 `native-performance-evidence.json`，只能用于诊断；不得手工补写，正式重采必须使用新的输出目录。

## 3. 完整性验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\verify-native-performance-evidence.ps1 `
  -EvidenceDirectory .\artifacts\quality\native-performance-<commit>
```

验证器检查提交、管理员状态、两个配置、25 插件、ETL/JSON 哈希，以及“原始采集不得直接通过发布门禁”的约束。

## 4. WPA 人工分析

在 Windows Performance Analyzer 中至少检查：

- CPU Usage (Sampled)：LongBetterWindows.Host、dwm、WPF/D3D 相关模块的权重、线程和调用栈；
- Desktop Composition：合成帧、DWM 活动、GPU/显示驱动相关异常区间；
- 以宿主插件页报告中的运行时就绪、1 秒、3 秒和 9 秒检查点对照 ETL 时间线；
- 是否存在可重复归因到 Long助手源码的方法、资源创建或视觉树变化。

分析结论必须区分：

- 可由 Long助手源码修复的热点；
- Windows/WPF/DWM/驱动外部活动；
- 当前证据无法稳定复现。

只有形成可复核的 WPA 表格/截图、分析说明和审阅人签核后，才可以另行批准原生性能项。原始 ETL 捕获本身永远不等于发布通过。

## 5. 严格工作集门禁

WPR 用于归因原生 CPU/合成活动；工作集仍由独立的 25 插件多进程样本判定。正式执行要求干净跟踪工作区、Release 宿主、至少 5 次样本、每次运行时就绪后等待至少 9 秒，且所有样本必须严格 `<200 MB`：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\measure-plugin-memory.ps1 `
  -OutputDirectory .\artifacts\quality\plugin-memory-<commit> `
  -Samples 5 `
  -IdleMilliseconds 9000 `
  -NoBuild

powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\verify-plugin-memory-evidence.ps1 `
  -EvidenceDirectory .\artifacts\quality\plugin-memory-<commit>
```

报告绑定 Git 提交、工作区状态、Release 宿主 SHA-256、25 个插件 ID、采样数、空闲时长和每次工作集。`-AllowDirty` 只允许开发诊断，生成的报告不得用于发布。
