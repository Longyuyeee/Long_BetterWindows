# LPWP 签名参考 Widget 发布

## 目标

该流程把确定性参考 Widget 打包为 `.lpak`，使用既有 Marketplace RSA 发布者私钥生成签名 Registry，
再以明确给定的发布者 Key ID 和公钥 SHA-256 指纹执行本地复核。输出不包含私钥，并保持
`release_eligible=false`，直到 Long Grid 跨仓库 E2E 和人工 Widget 验收完成。

应用自动更新私钥与 Marketplace 发布者私钥用途不同，禁止混用。当前仓库中的
`local-secrets/update-signing/update-signing.private.key` 仅供应用更新清单使用，不能用于插件发布。

## 密钥前提

- 使用用户认可的至少 2048 位 RSA PEM 发布者私钥；脚本不会创建临时密钥冒充正式发布者；
- 推荐存放在仓库外的受控位置；如按项目约定放入 `local-secrets/marketplace/`，必须保持 Git 忽略；
- 私钥不得复制到包、Registry、诊断、日志或 Git；公开产物仅包含公钥、Key ID 和指纹；
- Key ID、Publisher 名称和公钥指纹需要由维护者独立记录，后续验证不能只信任包自带字段。

## 一键生成与验证

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\publish-signed-reference-widget.ps1 `
  -OutputDirectory C:\release\lpwp-reference `
  -PrivateKeyPath C:\secure\long-marketplace.private.pem `
  -PublisherKeyId long-marketplace-2026 `
  -PublisherName "Long" `
  -BasePackageUri https://downloads.example.com/plugins/
```

流程依次执行确定性打包、包清单校验、SHA-256/RSA-SHA256 签名、Registry 与发布报告生成、
部署计划一致性检查、独立 Key ID/指纹匹配及宿主 `PluginPackageValidator` 远程信任路径验证。

输出目录至少包含：

- `registry.json`；
- `packages/*.lpak`；
- `trusted-publisher.fragment.json`（公开信任根候选，需维护者批准后配置）；
- `publish-report.json`；
- `bundle-verification-report.json`；
- `signed-reference-widget-report.json`。

也可对已有 Bundle 单独复核：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\verify-marketplace-bundle.ps1 `
  -BundleDirectory C:\release\lpwp-reference `
  -ExpectedPublisherKeyId long-marketplace-2026 `
  -ExpectedPublicKeyFingerprint <64位SHA-256十六进制指纹> `
  -ReportPath C:\release\lpwp-reference-verification.json
```

## 当前阻塞与验收

仓库当前没有 Marketplace 发布者私钥，只有用途隔离的应用更新私钥。因此代码链和自动测试已具备，
真实签名参考包仍需用户提供或确认 Marketplace 发布者密钥、Key ID、Publisher 名称和正式 HTTPS 包地址。
在此之前不得把测试临时密钥生成的包标记为官方参考包。
