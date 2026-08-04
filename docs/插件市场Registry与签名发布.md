# Long 插件市场 Registry 与签名发布

> 状态：v0.8 传输协议已实现；生产 Registry 地址与正式根密钥由发布流程注入。
> 安全原则：远程包必须同时通过 HTTPS 来源限制、SHA-256、可信发布者 RSA 签名、Manifest 和兼容性校验；当前远程市场仅允许 `runtime: "webview"`，DLL 与 C# 脚本只能本地导入并按完全信任扩展处理。

## 1. 宿主配置

宿主从 `Marketplace/marketplace-settings.json` 读取远程源：

```json
{
  "SchemaVersion": 1,
  "RegistryUri": "https://market.example.com/registry.json",
  "CatalogTimeoutSeconds": 8,
  "DownloadTimeoutSeconds": 60,
  "AllowedPackageHosts": ["packages.example.com"]
}
```

- `RegistryUri` 必须使用 HTTPS；为空时市场保持本地目录模式。
- Registry 所在主机会自动加入下载允许列表；独立 CDN 必须显式加入 `AllowedPackageHosts`。
- 重定向后的最终地址仍需使用 HTTPS 且位于允许列表。
- Registry 最大 2 MB、最多 5000 个插件条目；插件包最大 256 MB。
- 网络失败时只回退到上一次成功解析并原子写入的目录缓存。

## 2. 发布者根信任

正式公钥写入 `Marketplace/trusted-publishers.json`：

```json
{
  "SchemaVersion": 1,
  "Publishers": [
    {
      "KeyId": "publisher-key-2026-01",
      "Publisher": "Example Publisher",
      "Algorithm": "RSA-SHA256",
      "PublicKeyPem": "-----BEGIN PUBLIC KEY-----\n...\n-----END PUBLIC KEY-----",
      "Sha256Fingerprint": "公钥 DER 的 SHA-256 十六进制指纹"
    }
  ]
}
```

- RSA 密钥不得小于 2048 位。
- `KeyId` 不得重复。
- 指纹按 `SubjectPublicKeyInfo` DER 字节计算。
- 私钥不得进入仓库、Registry、插件包或宿主配置。
- 当前仓库内置空信任库；这会安全地拒绝所有远程包，不应使用测试密钥替代生产根密钥。

## 3. Registry 版本记录

每个远程版本至少包含：

```json
{
  "Version": "1.2.0",
  "PackageUri": "https://packages.example.com/plugin-1.2.0.lpak",
  "Sha256": "插件包完整字节的 SHA-256",
  "Signature": "对 SHA-256 摘要执行 RSA PKCS#1 SHA-256 签名后的 Base64",
  "PublisherKeyId": "publisher-key-2026-01",
  "PublisherPublicKeyPem": "与根信任库一致的公钥",
  "Capabilities": ["storage.local"],
  "MinHostVersion": "0.5.0",
  "MinApiVersion": "1.0.0",
  "MinUiKitVersion": "1.0.0"
}
```

Registry 公钥必须与根信任库中相同 Key ID 的公钥完全一致。Manifest 的插件 ID、版本和能力仍会从实际包中重新读取，不能由 Registry 绕过。

## 4. 客户端处理顺序

```text
HTTPS Registry
  → 目录大小与结构校验
  → 内存搜索 / 原子缓存
  → 允许主机 HTTPS 下载
  → 流式大小限制与 SHA-256
  → 发布者根信任与 RSA 签名
  → Manifest / 入口 / 兼容性 / 权限差异
  → 用户确认
  → 暂存 / 替换 / 扫描 / 失败回滚
```

取消、超时、哈希不匹配或重定向越界时，临时下载文件会被清理。通过哈希复核的缓存包可以复用，但安装前仍会再次执行完整包校验。

## 5. Registry 生成流水线

发布源使用 `marketplace-source.example.json` 的结构，只填写展示信息、版本、相对包路径和更新说明。权限、最低版本、ID 与版本会从实际 `.lpak` Manifest 重新读取。

```powershell
.\publish-marketplace.ps1 `
  -SourceCatalog .\marketplace-source.json `
  -PackagesDir .\dist `
  -OutputDir .\artifacts\marketplace `
  -PrivateKeyPath C:\secure\publisher.private.pem `
  -PublisherKeyId long-labs-2026-01 `
  -PublisherName "Long Labs" `
  -BasePackageUri https://packages.example.com/
```

流水线执行以下门槛：

1. 包路径必须是 `PackagesDir` 内的相对路径。
2. 包结构、Manifest、入口、ID、版本和当前宿主兼容性必须通过校验。
3. 私钥必须是至少 2048 位的 RSA 私钥，仅用于内存签名。
4. 每个包复制到发布暂存区后生成 SHA-256 和 RSA PKCS#1 SHA-256 签名。
5. 同时生成 `registry.json`、`trusted-publisher.fragment.json` 和 `publish-report.json`。
6. 整个暂存目录完成后才替换目标目录；覆盖必须显式使用 `-Force`。
7. 替换失败会恢复旧输出；恢复失败时保留 `.market-backup-*` 供人工处理。

产物目录中的 `packages/`、Registry、信任片段和报告可以进入部署步骤。私钥不会复制到产物，仓库也通过 `.gitignore` 排除常见私钥文件名。信任片段仍需由发布负责人审查后合并，不能自动修改客户端根信任库。

### 一键发布准备

PX5C-1 提供只读发布准备入口，把签名生成、独立 Bundle 复核和部署 Dry Run 串成一个不可覆盖批次：

```powershell
.\prepare-marketplace-release.ps1 `
  -SourceCatalog .\marketplace-source.json `
  -PackagesDir .\dist `
  -BundleDir C:\release\marketplace-bundle `
  -EvidenceDir C:\release\marketplace-preparation `
  -PrivateKeyPath C:\secure\publisher.private.pem `
  -PublisherKeyId long-labs-2026-01 `
  -PublisherName "Long Labs" `
  -BasePackageUri https://packages.example.com/ `
  -Target Https `
  -Destination https://market.example.com/
```

该命令不会上传或修改目标，也不读取部署凭据。Bundle 与证据目录必须不存在且与包源目录隔离；任一步失败会清理本批新产物。证据目录包含 `bundle-verification.json`、`deployment-dry-run.json` 和 `preparation-summary.json`，摘要绑定 Release ID、发布者公钥指纹、Registry 哈希、两份底层报告哈希和完整部署文件计划。

这里的 RSA 私钥只用于插件包和 Registry 信任链，与 Windows Authenticode 代码签名证书无关。应用继续采用 unsigned 分发通道不会阻断本地开发；只有正式远程市场包需要该独立的发布者信任链。

## 6. 部署适配器

发布工具生成的包使用 `{插件ID}-v{版本}-{SHA256前12位}.lpak` 不可变文件名。部署前会重新校验 `publish-report.json`、Registry 引用以及每个包的大小和 SHA-256，产物被篡改或 Registry 指向报告外文件时立即终止。

先执行只读预演：

```powershell
.\deploy-marketplace.ps1 `
  -BundleDir .\artifacts\marketplace `
  -Target Local `
  -Destination C:\staging\marketplace `
  -DryRun
```

本地目录部署用于受控环境演练；替换已有发布目录必须显式传入 `-Force`：

```powershell
.\deploy-marketplace.ps1 `
  -BundleDir .\artifacts\marketplace `
  -Target Local `
  -Destination C:\staging\marketplace `
  -Force
```

本地完整演练可使用一次性 3072 位 RSA 密钥自动生成两个 Base64 测试版本，依次执行签名发布、基线部署、候选 Dry Run、覆盖部署和显式回滚：

```powershell
.\rehearse-marketplace-local.ps1 `
  -OutputDirectory .\artifacts\quality\marketplace-local-release-rehearsal
```

输出目录必须不存在。演练会保留机器可读报告和回滚后的本地部署，删除私钥及构建工作区，并验证旧 Registry 引用的不可变包仍存在。该演练用于提前发现工具链与回滚问题，不能替代正式 HTTPS 端点、正式根信任和无上传凭据公开拉取验收。

HTTPS 部署只从命名环境变量读取 Bearer 凭据，凭据值不会进入命令行、报告或部署产物：

```powershell
$env:LONG_MARKETPLACE_DEPLOY_TOKEN = '<由受控凭据系统注入>'
.\deploy-marketplace.ps1 `
  -BundleDir .\artifacts\marketplace `
  -Target Https `
  -Destination https://packages.example.com/releases/ `
  -CredentialEnvironmentVariable LONG_MARKETPLACE_DEPLOY_TOKEN
```

部署提交顺序固定为：

1. 上传不可变 `packages/` 文件并通过远端读取复核 SHA-256。
2. 上传 `publish-report.json` 作为本次发布审计记录。
3. 在 `releases/{releaseId}/` 持久化上一版 Registry 快照和部署清单。
4. 最后提交 `registry.json`，使客户端只看到完整版本集合。

`trusted-publisher.fragment.json` 不会自动部署，仍须由发布负责人审查后进入宿主根信任配置。HTTPS 目标只允许同源地址且拒绝重定向；提交 Registry 前会读取旧版本，提交或复核失败时恢复旧 Registry，原先不存在时删除失败的新提交。包阶段失败不会改变线上 Registry；已经上传但尚未被 Registry 引用的不可变包是安全孤儿，可由后续保留策略清理。

### 批准并执行正式发布

PX5C-2 的正式入口只接受 PX5C-1 生成且未被修改的 Bundle 与准备证据，并要求人工输入完全相同的 Release ID：

```powershell
$env:LONG_MARKETPLACE_DEPLOY_TOKEN = '<由受控凭据系统注入>'
.\release-marketplace.ps1 `
  -BundleDir C:\release\marketplace-bundle `
  -PreparationEvidenceDir C:\release\marketplace-preparation `
  -Destination https://market.example.com/ `
  -TrustStorePath C:\secure\trusted-publishers.json `
  -AllowedPackageHosts packages.example.com `
  -ExecutionEvidenceDir C:\release\marketplace-execution `
  -ConfirmReleaseId 20260804093000-ABCDEF123456
```

执行器会先重新计算准备摘要、Registry、Bundle 验证报告、Dry Run 报告和完整文件计划，再从无上传凭据路径验证当前公开基线；上传前会再次复核准备证据。部署后公开验证必须同时通过签名信任链，并且包 ID、版本、SHA-256、发布者 Key ID 和字节数与批准候选完全一致。成功时保留新 Registry；上传或公开验证失败时使用同一 Release ID 回滚，再验证公开包集合已恢复到发布前基线。

正式入口要求目标已有可公开验证的 Registry 基线，因此不承担首次市场引导发布。首次引导仍需独立审批，不得伪造“可回滚”保证。执行证据目录不可覆盖，`release-summary.json` 会记录准备摘要哈希、生命周期状态、失败与回滚结果以及底层报告哈希。

## 7. 部署后公开端到端验收

部署完成后必须从无上传凭据的公开客户端路径重新拉取 Registry 和全部版本包。验收器不会复用发布目录，也不会读取部署 Bearer Token：

```powershell
.\verify-marketplace.ps1 `
  -RegistryUri https://market.example.com/registry.json `
  -TrustStorePath C:\secure\trusted-publishers.json `
  -AllowedPackageHosts packages.example.com `
  -ReportPath .\artifacts\marketplace-verification.json
```

Registry 所在主机会自动允许；独立 CDN 主机必须通过 `-AllowedPackageHosts` 明确列出。验证顺序为：

1. 公开 HTTPS 拉取 Registry，拒绝重定向、缓存回退、超限和无效目录。
2. 按 Registry 逐个下载版本包，并执行允许主机、256 MB 上限和 SHA-256 验证。
3. 使用正式根信任文件复核 Key ID、公钥一致性和 RSA/SHA-256 签名。
4. 重新校验 Manifest ID、版本、入口、包结构及宿主/API/UI Kit 兼容性。
5. 原子写入机器可读验收报告，记录地址、时间、数量、字节、哈希和 Key ID，但不写入签名、公钥正文或任何上传凭据。

命令返回非零状态即表示发布不可验收。真实环境发布应将“部署成功”和“公开端到端验收成功”同时作为流水线放行条件；若验收失败，应阻止后续推广并使用上一版 Registry 执行回滚流程。

## 8. Registry 人工回滚

覆盖部署前，部署器会把当前 Registry 保存为 `releases/{releaseId}/previous-registry.json`，并生成包含新旧 Registry SHA-256 的 `deployment-manifest.json`。恢复点在新 Registry 提交前写入并完成远端读取复核；首次部署没有上一版 Registry，因此不会声明可回滚。

回滚是显式高风险操作，`-ConfirmReleaseId` 必须与 `-ReleaseId` 完全一致：

```powershell
$env:LONG_MARKETPLACE_DEPLOY_TOKEN = '<由受控凭据系统注入>'
.\rollback-marketplace.ps1 `
  -Target Https `
  -Destination https://market.example.com/ `
  -ReleaseId 20260719093000-ABCDEF123456 `
  -ConfirmReleaseId 20260719093000-ABCDEF123456
```

本地演练使用相同命令并将 `-Target` 改为 `Local`、`-Destination` 指向部署目录。回滚器会依次验证 Release ID 安全字符、部署清单、恢复点存在性、快照 SHA-256 和 Registry 结构，再原子替换当前 Registry 并重新读取复核。HTTPS 回滚拒绝重定向且只允许同源；回滚提交失败时会恢复操作前的 Registry。

回滚只移动 Registry 指针，不删除任何不可变包、审计报告或恢复点。回滚完成后必须立即执行第 7 节公开端到端验收，并把回滚结果与验收报告一起归档。

## 9. 生产全链演练

`rehearse-marketplace.ps1` 将部署、公开验收、回滚和再次公开验收编排为一次受控演习。它会把 Registry 恢复到演练前版本，因此只适用于已经存在可回滚基线的环境，并要求显式传入 `-ConfirmRehearsal`：

```powershell
$env:LONG_MARKETPLACE_DEPLOY_TOKEN = '<由受控凭据系统注入>'
.\rehearse-marketplace.ps1 `
  -BundleDir .\artifacts\marketplace `
  -Destination https://market.example.com/ `
  -TrustStorePath C:\secure\trusted-publishers.json `
  -AllowedPackageHosts packages.example.com `
  -EvidenceDirectory C:\release-evidence\marketplace-20260719 `
  -ConfirmRehearsal
```

演练目录必须不存在，防止覆盖既有证据。任何线上写入前，脚本先生成 `preflight-dry-run.json`，确认候选文件完整且 Registry 最后提交；随后从无上传凭据的公开路径生成 `baseline-verification.json`，确认现网基线和回滚起点可读取。两项均通过后才设置部署开始状态。部署器会在提交前写入包含同一 Release ID 的 `deployment.json`，成功后更新状态；脚本随后生成部署后与回滚后的两份公开验收报告，以及 `rehearsal-summary.json`。

发布负责人可先在不注入上传令牌、不写入线上状态的情况下执行只读预检；此模式不要求 `-ConfirmRehearsal`：

`rehearsal-summary.json` 通过同目录临时文件不可覆盖地原子创建。若进程中断且目录中没有完整摘要，该目录只能用于诊断；不要手工补写或复用，正式重试必须指定新的证据目录。

```powershell
.\rehearse-marketplace.ps1 `
  -BundleDir .\artifacts\marketplace `
  -Destination https://market.example.com/ `
  -TrustStorePath C:\secure\trusted-publishers.json `
  -AllowedPackageHosts packages.example.com `
  -EvidenceDirectory C:\release-evidence\marketplace-preflight-20260720 `
  -PreflightOnly
```

候选 Dry Run 或现网基线验收失败时不会开始部署。设置部署开始状态后的任一步骤失败，`finally` 安全路径都会尝试回滚并再次验收，同时把原始失败、回滚失败和回滚验收失败分别写入摘要。脚本不把 Bearer Token 作为参数传递给 .NET 工具，也不将其写入任何报告。只有摘要中的 `preflight_dry_run_verified`、`baseline_verified`、`deployment_completed`、`deployment_verified`、`rollback_completed` 和 `rollback_verified` 全部为 `true`，才可签核全链演练。

最终外部门禁不会只信任这些布尔值和文件哈希。它会解析五份底层报告，要求 Dry Run 与部署使用相同 Release ID、HTTPS 目标和完整文件计划，三次公开验证指向同一 Registry，部署后状态区别于基线，回滚后状态恢复基线，并验证各报告时间按实际生命周期排序。手工替换报告并同步更新摘要 SHA-256 不能形成有效发布证据。

## 10. 客户端网络恢复策略

- Registry 请求失败时只回退到最近一次通过结构校验的可信目录；没有可信缓存时明确显示离线状态，本地插件不受影响。
- 插件包仅对超时、断流、5xx、408 和 429 执行最多 3 次有限重试；取消、非法地址、超限、哈希或签名拒绝不会被重试掩盖。
- 相同插件 ID、版本和哈希的并发下载合并为一次传输，等待者复用完成 SHA-256 复核后的缓存。
- 下载分片使用唯一临时文件，任务退出时立即删除；新任务会回收超过一小时的异常退出遗留分片。
- 安装、替换和卸载由单实例事务门串行化，并继续受事务日志与启动恢复保护。

运行 `run-marketplace-network-resilience.ps1` 可生成 TRX 和机器可读 JSON 证据；正式候选要求全部韧性字段通过且 Release 插件目录指纹保持不变。
