# Long助手 Windows 代码签名发布

> 适用版本：v1.9.0 及以后
> 目标：提供可选的 Authenticode 增强通道；当前公开 `unsigned` 通道不依赖本流程。

v1.9 当前选择未签名分发。`release.ps1` 会生成 `distribution_channel=unsigned`、`publisher_identity=unverified`、`signed=false` 的候选，并在源码干净且全部工程门禁通过时写入 `release_eligible=true`。本页仅在未来决定升级为 `signed` 通道时使用。

## 1. 安全边界

- 签名脚本只从 Windows `CurrentUser\My` 或 `LocalMachine\My` 证书存储区调用私钥，适配受保护证书、硬件令牌和云密钥提供程序。
- 脚本不接受 PFX 文件或口令参数，私钥、口令和访问令牌不得写入仓库、日志或发布 Manifest。
- 输入候选目录永不原地修改。签名在同级临时目录进行，全部签名、时间戳、ZIP 重建和独立复验成功后才移动到新输出目录。
- `source_dirty=true` 的候选直接拒绝；必须先冻结提交并在干净工作区重新运行 `release.ps1`。
- 签名和独立复验都必须显式传入完整的 40 位源码提交；候选、签名元数据与人工批准的提交任一不一致都会阻断。

## 2. 前置条件

1. 安装 Windows SDK Signing Tools，确保存在 x64 `signtool.exe`。
2. 将正式代码签名证书接入 Windows `My` 存储区；证书必须具有可访问私钥和 Code Signing EKU `1.3.6.1.5.5.7.3.3`。
3. 证书主题必须与批准的发布主体一致，且至少还有 7 天有效期。
4. 准备受信任的 HTTPS RFC 3161 时间戳服务地址。
5. 输入候选的 `release-manifest.json` 必须记录 `source_dirty=false`、`signed=false` 和完整源码提交 SHA。

### v1.9.0 当前前置审计

2026-07-22 已确认本机安装 Windows SDK x64 SignTool，但当前不采购正式证书，v1.9 继续使用合格的 `unsigned` 候选。若未来升级签名，必须从当时冻结候选的完整源码提交执行，不得复用本节示例中的历史提交。

## 3. 签名

```powershell
.\sign-release.ps1 `
  -InputReleaseDirectory .\artifacts\releases\v1.9.0 `
  -OutputDirectory .\artifacts\releases\v1.9.0-signed `
  -CertificateThumbprint "正式证书指纹" `
  -ExpectedSubject "批准的发布主体" `
  -ExpectedSourceCommit "完整的 40 位候选源码提交" `
  -TimestampUrl "https://timestamp.example.com" `
  -CertificateStoreLocation CurrentUser `
  -ConfirmSign
```

脚本签署宿主 EXE/DLL 和每个本地 DLL/EXE 插件入口，不会重签 Microsoft、WebView2、Serilog、Wpf.Ui 等第三方依赖。每个文件使用 SHA-256 文件摘要和 SHA-256 RFC 3161 时间戳，随后执行默认 Authenticode 策略、全部签名和时间戳验证。

成功后重新生成两个 ZIP、`SHA256SUMS.txt` 和 `release-manifest.json`。Manifest 会记录源码提交、证书指纹、主题、到期时间、存储区、摘要算法、时间戳地址、SignTool 版本和每包签名文件数，并只在完整复验后写入 `signed=true`、`release_eligible=true`。

## 4. 独立复验

在另一台可信 Windows 机器上执行：

```powershell
.\verify-signed-release.ps1 `
  -ReleaseDirectory .\artifacts\releases\v1.9.0-signed `
  -ExpectedSourceCommit "完整的 40 位候选源码提交" `
  -ExpectedCertificateThumbprint "正式证书指纹" `
  -OutputPath .\artifacts\quality\v1.9.0-code-signing-verification.json
```

复验器会先核对顶层候选提交与签名元数据中的源码提交，再重新校验 ZIP SHA-256，解压用户实际下载的 ZIP，枚举产品签名目标，并同时使用 `Get-AuthenticodeSignature` 与 SignTool `/pa /all /tw` 校验签名链、签名者和时间戳。任何提交不一致、警告、错误、目标缺失、签名数不一致或证书指纹变化都会阻断。

## 5. 后续门禁

签名包通过独立复验后，必须重新执行：

1. 干净 Windows 用户环境安装、升级、回滚和卸载门禁。
2. SmartScreen 与至少一种主流杀毒软件的实际下载/启动观察。
3. 物理 DPI 与辅助功能人工签核。
4. 最终 Registry/CDN 只读预检和受控发布演练。

代码签名能证明发布者与文件完整性，但不能保证立即获得 SmartScreen 信誉，因此 SmartScreen 结果仍作为独立人工证据记录。
