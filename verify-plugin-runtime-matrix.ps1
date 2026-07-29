#!/usr/bin/env pwsh
<#
.SYNOPSIS
  构建并验证 Long助手四种插件运行时的发布矩阵。
.DESCRIPTION
  使用真实 Web、C# Script、Native 和 Hybrid 样板，依次执行目录生产验证、
  确定性打包和成品生产验证，并审计本地导入、远程市场与权限摘要。
.PARAMETER Configuration
  原生和 Hybrid 样板的构建配置，默认 Release。
.PARAMETER OutputPath
  可选的 JSON 报告路径。目标必须不存在，报告以同目录临时文件原子创建。
.EXAMPLE
  .\verify-plugin-runtime-matrix.ps1
.EXAMPLE
  .\verify-plugin-runtime-matrix.ps1 -OutputPath ".\plugin-runtime-matrix.json"
#>
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [string] $OutputPath
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root "LongBetterWindows.sln"
$validatorScript = Join-Path $root "validate-plugin.ps1"
$packerScript = Join-Path $root "pack-plugin.ps1"
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$matrixRoot = Join-Path (
    [IO.Path]::GetTempPath()) (
    "long-plugin-runtime-matrix-{0}" -f [guid]::NewGuid().ToString("N"))
$results = @()
$exitCode = 0
$report = $null

if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw ".NET SDK 未安装或 dotnet.exe 不在 PATH 中。"
    }
    $dotnet = $dotnetCommand.Source
}

function Invoke-Validation {
    param([string] $Target)

    $output = & powershell -NoProfile -ExecutionPolicy Bypass `
        -File $validatorScript -Path $Target -NoBuild 2>&1
    $toolExitCode = $LASTEXITCODE
    try {
        $validation = $output | ConvertFrom-Json
    } catch {
        throw "验证器未返回有效 JSON：$($output -join [Environment]::NewLine)"
    }
    if ($toolExitCode -ne 0 -or -not $validation.is_success) {
        throw "插件生产验证失败：$($validation.error)"
    }
    return $validation
}

function Invoke-Package {
    param(
        [string] $PluginDirectory,
        [string] $PackageDirectory
    )

    $output = & powershell -NoProfile -ExecutionPolicy Bypass `
        -File $packerScript `
        -PluginDir $PluginDirectory `
        -OutputDir $PackageDirectory 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "插件打包失败：$($output -join [Environment]::NewLine)"
    }

    $packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter *.lpak)
    if ($packages.Count -ne 1) {
        throw "运行时样板应只生成一个 .lpak，实际为 $($packages.Count) 个。"
    }
    return $packages[0].FullName
}

function Assert-MatrixContract {
    param(
        [string] $RuntimeKind,
        [object] $Validation,
        [bool] $ExpectedHighTrust,
        [bool] $ExpectedRemoteEligible
    )

    $local = $Validation.distribution_eligibility.local_import
    $remote = $Validation.distribution_eligibility.remote_marketplace
    if (-not $local.eligible) {
        throw "$RuntimeKind 样板不具备本地导入资格。"
    }
    if ([bool]$local.requires_high_trust_warning -ne $ExpectedHighTrust) {
        throw "$RuntimeKind 样板的高信任判断不符合预期。"
    }
    if ([bool]$remote.package_eligible -ne $ExpectedRemoteEligible) {
        throw "$RuntimeKind 样板的远程市场资格不符合预期。"
    }
    if ($ExpectedRemoteEligible -and -not $remote.requires_publisher_signature) {
        throw "$RuntimeKind 未签名样板必须明确要求发布者签名。"
    }
    if ((-not $ExpectedRemoteEligible) -and
        $remote.block_reason -ne "high_trust_runtime_not_supported") {
        throw "$RuntimeKind 高信任样板缺少稳定的远程市场阻断原因。"
    }
}

function Add-MatrixCase {
    param(
        [string] $RuntimeKind,
        [string] $PluginDirectory,
        [bool] $ExpectedHighTrust,
        [bool] $ExpectedRemoteEligible
    )

    $directoryValidation = Invoke-Validation -Target $PluginDirectory
    Assert-MatrixContract `
        -RuntimeKind $RuntimeKind `
        -Validation $directoryValidation `
        -ExpectedHighTrust $ExpectedHighTrust `
        -ExpectedRemoteEligible $ExpectedRemoteEligible

    $caseOutput = Join-Path $matrixRoot "packages-$RuntimeKind"
    New-Item -ItemType Directory -Path $caseOutput | Out-Null
    $package = Invoke-Package `
        -PluginDirectory $PluginDirectory `
        -PackageDirectory $caseOutput
    $packageValidation = Invoke-Validation -Target $package
    Assert-MatrixContract `
        -RuntimeKind $RuntimeKind `
        -Validation $packageValidation `
        -ExpectedHighTrust $ExpectedHighTrust `
        -ExpectedRemoteEligible $ExpectedRemoteEligible

    $script:results += [ordered]@{
        runtime_kind = $RuntimeKind
        plugin_id = $packageValidation.manifest.id
        version = $packageValidation.manifest.version
        package_sha256 = $packageValidation.sha256
        requested_permissions =
            @($packageValidation.permission_summary.requested)
        local_import = [ordered]@{
            eligible =
                [bool]$packageValidation.distribution_eligibility.local_import.eligible
            requires_high_trust_warning =
                [bool]$packageValidation.distribution_eligibility.local_import.requires_high_trust_warning
        }
        remote_marketplace = [ordered]@{
            package_eligible =
                [bool]$packageValidation.distribution_eligibility.remote_marketplace.package_eligible
            currently_trusted =
                [bool]$packageValidation.distribution_eligibility.remote_marketplace.currently_trusted
            requires_publisher_signature =
                [bool]$packageValidation.distribution_eligibility.remote_marketplace.requires_publisher_signature
            block_reason =
                $packageValidation.distribution_eligibility.remote_marketplace.block_reason
        }
    }
}

try {
    foreach ($required in @($solution, $validatorScript, $packerScript)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "运行时矩阵依赖不存在：$required"
        }
    }

    New-Item -ItemType Directory -Path $matrixRoot | Out-Null
    $buildOutput = & $dotnet build $solution `
        -c $Configuration --nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "解决方案构建失败：$($buildOutput -join [Environment]::NewLine)"
    }

    $hostPlugins = Join-Path $root (
        "src\LongBetterWindows.Host\bin\{0}\net8.0-windows\Plugins" -f
            $Configuration)
    $scriptPlugin = Join-Path $matrixRoot "script-plugin"
    Copy-Item -LiteralPath (
        Join-Path $root "src\Templates\script-plugin") `
        -Destination $scriptPlugin -Recurse

    Add-MatrixCase `
        -RuntimeKind "web" `
        -PluginDirectory (Join-Path $root "src\Base64Tool") `
        -ExpectedHighTrust $false `
        -ExpectedRemoteEligible $true
    Add-MatrixCase `
        -RuntimeKind "script" `
        -PluginDirectory $scriptPlugin `
        -ExpectedHighTrust $true `
        -ExpectedRemoteEligible $false
    Add-MatrixCase `
        -RuntimeKind "native" `
        -PluginDirectory (Join-Path $hostPlugins "SamplePlugin") `
        -ExpectedHighTrust $true `
        -ExpectedRemoteEligible $false
    Add-MatrixCase `
        -RuntimeKind "hybrid" `
        -PluginDirectory (Join-Path $hostPlugins "ClipboardHistory") `
        -ExpectedHighTrust $true `
        -ExpectedRemoteEligible $false

    $report = [ordered]@{
        schema_version = 1
        classification = "long_plugin_runtime_matrix"
        is_success = $true
        configuration = $Configuration.ToLowerInvariant()
        case_count = $results.Count
        cases = $results
    }
} catch {
    $exitCode = 1
    $report = [ordered]@{
        schema_version = 1
        classification = "long_plugin_runtime_matrix"
        is_success = $false
        configuration = $Configuration.ToLowerInvariant()
        error = $_.Exception.Message
        case_count = $results.Count
        cases = $results
    }
} finally {
    if (Test-Path -LiteralPath $matrixRoot) {
        $resolvedMatrixRoot = [IO.Path]::GetFullPath($matrixRoot)
        $systemTemp = [IO.Path]::GetFullPath(
            [IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if ($resolvedMatrixRoot.StartsWith(
                $systemTemp,
                [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $resolvedMatrixRoot) -like
                "long-plugin-runtime-matrix-*") {
            Remove-Item -LiteralPath $resolvedMatrixRoot -Recurse -Force
        }
    }
}

$json = $report | ConvertTo-Json -Depth 10
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = $ExecutionContext.SessionState.Path.
        GetUnresolvedProviderPathFromPSPath($OutputPath)
    if (Test-Path -LiteralPath $resolvedOutput) {
        throw "输出报告已存在，拒绝覆盖：$resolvedOutput"
    }
    $outputDirectory = Split-Path -Parent $resolvedOutput
    if (-not (Test-Path -LiteralPath $outputDirectory)) {
        New-Item -ItemType Directory -Path $outputDirectory | Out-Null
    }
    $temporaryOutput = Join-Path $outputDirectory (
        ".{0}.{1}.tmp" -f
            [IO.Path]::GetFileName($resolvedOutput),
            [guid]::NewGuid().ToString("N"))
    try {
        [IO.File]::WriteAllText(
            $temporaryOutput,
            $json + [Environment]::NewLine,
            [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryOutput, $resolvedOutput)
    } finally {
        if (Test-Path -LiteralPath $temporaryOutput) {
            Remove-Item -LiteralPath $temporaryOutput -Force
        }
    }
}

Write-Output $json
exit $exitCode
