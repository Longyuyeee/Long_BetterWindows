# Win11 Sparse Package 一级右键菜单

> 状态：第一批原生与包基础、第二批受控签名/注册事务和 Tool Center 双通道 UI 已完成；真实签名与 Explorer 验收待第三批。

## 目标与边界

Long 保留现有安装目录和 WPF/.NET 运行方式，通过带外部位置的 Sparse Package 获取包身份，并用原生 `IExplorerCommand` 把“备注此文件夹”放入 Windows 11 一级右键菜单。

本阶段不把 .NET 运行时加载进 Explorer，不用旧注册表菜单冒充 Win11 一级菜单，也不生成或信任自签名证书。生产包的 Publisher 必须与最终签名证书 Subject 一致。

微软规范：

- [Grant package identity by packaging with external location manually](https://learn.microsoft.com/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)
- [Add a File Explorer context menu command to a packaged desktop app](https://learn.microsoft.com/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)
- [desktop4:FileExplorerContextMenus](https://learn.microsoft.com/uwp/schemas/appxpackage/uapmanifestschema/element-desktop4-fileexplorercontextmenus)

## 第一批实现

1. `LongBetterWindows.ShellExtension.dll` 是 x64 原生 COM DLL，实现 `IExplorerCommand`、`IClassFactory`、`DllGetClassObject` 和 `DllCanUnloadNow`。
2. 菜单只对单个文件系统文件夹启用；`GetTitle`、`GetState` 等 Explorer UI 路径不执行插件扫描、磁盘遍历或网络请求。
3. 用户点击后，DLL 从自身外部位置定位 `LongBetterWindows.Host.exe`，仅转发 `--note "<folder>"`；具体备注 UI 与存储仍由现有宿主和 FolderNote 插件负责。
4. Manifest 使用同一个非零 CLSID 注册 `windows.comServer`、`Directory` 与 `Directory\Background` 两个 `windows.fileExplorerContextMenus` 项。
5. Sparse Package 声明 `AllowExternalContent=true`、`win32App`、`mediumIL`、`runFullTrust` 与 `unvirtualizedResources`。
6. 宿主 EXE 嵌入对应的 MSIX 身份元数据；脚本变更 Publisher 时同步重建宿主，避免包身份与可执行文件关联失配。包的 `AppListEntry=none`，不会额外生成一个开始菜单入口。

## 生成未签名候选

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\build-sparse-package.ps1 `
  -OutputDirectory .\artifacts\quality\sparse-package-foundation
```

脚本执行以下只读或可恢复步骤：

- Release 构建现有宿主；
- 使用 Visual Studio x64 C++ 工具编译原生 Shell 扩展；
- 用 `dumpbin` 检查两个必要 COM 导出和 x64 机器类型；
- 从现有 `Assets/app.ico` 生成仅供包清单使用的 PNG；
- 使用 Windows SDK `MakeAppx /nv` 生成未签名 Sparse MSIX；
- 记录源码提交、工作区是否干净、外部位置、DLL/MSIX SHA-256 和明确的未签名、未安装状态。

脚本不会运行包注册，不会创建证书，也不会写入 Trusted People。

未签名构建已在 `df841c8` 整合审计中从干净源码重新执行，宿主 Release、x64 原生 DLL、必要 COM 导出和 MakeAppx `/nv` 均通过。构建报告包含本机绝对路径和可再生成的中间文件，因此不作为最终发布证据提交；正式证据只在未来提供正式证书和干净 Windows 11 环境后，由第三批采集器绑定冻结提交、签名哈希链和 Explorer 截图生成。

## 第二批：签名、注册事务与管理 UI

已完成：

1. `sign-sparse-package.ps1` 只接受 Windows 证书存储中的明确 Thumbprint；验证私钥、有效期、代码签名 EKU、包 Publisher 与证书 Subject，使用 SHA-256 签名并再次调用 SignTool/Authenticode 验证。脚本不接收 PFX 或密码参数，不创建证书，也不修改信任存储。
2. `manage-sparse-package.ps1` 提供 `Status`、`Register`、`Unregister` 三种动作。注册前检查包身份、x64 架构、有效签名、Publisher、宿主 EXE 和 Shell DLL；拒绝降级及相同版本不同证据覆盖，注册成功后原子写入当前用户收据。
3. 注册/升级使用 Windows 的 `Add-AppxPackage -ExternalLocation` 部署事务；卸载按检测到的 Package Full Name 执行并复核消失。两者均不读写兼容旧菜单的注册表键。
4. Tool Center 已拆分“Win11 一级右键菜单”和“兼容旧右键菜单”两张卡片。新通道需要用户选择候选包并再次确认，独立显示版本、架构和外部位置核对状态；卸载也需要确认。
5. 未签名候选实测被注册前验证拒绝，前后状态均为 `NotInstalled`。系统集成页真实 Release 渲染证据位于 `artifacts/quality/sparse-package-management-g6-20260723/`。
6. Release 构建 0 警告、0 错误，全量自动化 500 / 500 通过。

签名示例（Thumbprint 不是秘密，但必须由发布负责人明确选择）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\sign-sparse-package.ps1 `
  -InputPackage .\LongBetterWindows.Sparse.1.9.0.0.x64.msix `
  -OutputPackage .\LongBetterWindows.Sparse.1.9.0.0.x64.signed.msix `
  -CertificateThumbprint <受控代码签名证书 Thumbprint> `
  -TimestampUrl https://<受控时间戳服务>
```

## 第三批：正式 Explorer 验收门禁

验收工具已经完成，但当前没有正式证书和干净 Windows 11 证据，因此产品状态仍保持未完成。

三段式门禁：

1. `capture-sparse-package-explorer-evidence.ps1` 只接受干净受跟踪源码的构建报告、与其输入哈希相连的签名报告、正式证书 Thumbprint 和带时间戳的有效签名。它同时核对宿主/DLL 哈希、包身份、Publisher、x64 架构和当前用户无旧 Sparse Package。
2. 采集器注册候选后打开隔离文件夹，要求操作员真实完成文件夹选中菜单、目录背景菜单和备注调用，并提供三张至少 `640 × 360` 的 PNG。无论交互或截图是否成功，`finally` 都会尝试卸载包；清理失败时证据不能通过。
3. `approve-sparse-package-explorer-evidence.ps1` 要求不同 Windows 账户的审核人复核三张截图、正确目标、Explorer 稳定性和卸载后菜单消失，并锁定所有证据文件哈希。
4. `verify-sparse-package-explorer-evidence.ps1` 只接受已独立批准、源码提交/证书一致、文件未改变、自动检查与人工清单全部通过的证据，并生成最终汇总。

采集器支持 `-PreflightOnly`：它执行完整候选、签名、时间戳、环境和外部文件核验，只写
`sparse-package-explorer-preflight.json`，明确记录未尝试包注册和 Explorer 交互。该结果只能用于准备检查，批准器和汇总器不会接受它替代真实证据。

采集示例：

```powershell
.\capture-sparse-package-explorer-evidence.ps1 `
  -SignedPackage C:\candidate\LongBetterWindows.Sparse.1.9.0.0.x64.signed.msix `
  -SignatureReport C:\candidate\LongBetterWindows.Sparse.1.9.0.0.x64.signed.msix.signature.json `
  -BuildReport C:\candidate\sparse-package-build.json `
  -ExternalLocation C:\LongBetterWindows `
  -ExpectedSourceCommit <完整 40 位提交> `
  -ExpectedCertificateThumbprint <正式证书 Thumbprint> `
  -OutputDirectory C:\evidence\sparse-explorer `
  -ScreenshotInputDirectory C:\evidence\screenshots `
  -EnvironmentLabel clean-win11-vm `
  -ConfirmCleanUserEnvironment `
  -RequireTimestamp
```

首次转移到干净机器时可在同一命令末尾增加 `-PreflightOnly`，并暂时省略
`-ScreenshotInputDirectory`；预检通过后必须使用新的输出目录重新执行正式采集。

操作员必须在采集开始前确保截图输入目录中不存在以下文件，随后按脚本提示新建：

- `selection-primary-menu.png`
- `background-primary-menu.png`
- `note-invocation.png`

另一名审核人在复核截图和卸载状态后执行：

```powershell
.\approve-sparse-package-explorer-evidence.ps1 `
  -EvidenceDirectory C:\evidence\sparse-explorer `
  -ExpectedSourceCommit <完整 40 位提交> `
  -ExpectedCertificateThumbprint <正式证书 Thumbprint> `
  -Reviewer <独立审核人> `
  -ReviewNotes "已复核两类一级菜单、正确备注目标、Explorer 稳定性与卸载结果" `
  -ConfirmSelectionPrimaryMenu `
  -ConfirmBackgroundPrimaryMenu `
  -ConfirmCorrectNoteTarget `
  -ConfirmExplorerStable `
  -ConfirmUninstallRemovedMenu

.\verify-sparse-package-explorer-evidence.ps1 `
  -EvidenceDirectory C:\evidence\sparse-explorer `
  -ExpectedSourceCommit <完整 40 位提交> `
  -ExpectedCertificateThumbprint <正式证书 Thumbprint> `
  -OutputPath C:\evidence\sparse-explorer-verification.json
```

当前本机负向验证确认：工作区不干净、签名链不完整的候选会在创建证据目录和注册包之前被拒绝，前后包状态均为 `NotInstalled`。
本批 Release 构建 0 警告、0 错误，全量自动化 503 / 503 通过。

可选 Sparse Package 通道的正式完成定义仍是：

1. 在干净 Windows 11 x64 环境签名并注册候选。
2. 文件夹选中和目录背景均在一级菜单看到“备注此文件夹”。
3. 调用打开正确目标的备注界面，Explorer 不崩溃、不阻塞。
4. 卸载后菜单消失，兼容旧菜单状态不变。
5. 由独立审核人批准并通过最终汇总；此前该可选通道保持“外部验收未完成”。

该完成定义只约束 Win11 一级右键菜单通道。Long 的 unsigned 便携主产品、兼容旧右键菜单和其他插件能力不依赖 Sparse Package 证书，也不因该通道缺少外部证据而阻断发布。
