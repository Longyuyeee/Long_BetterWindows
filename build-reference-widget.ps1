#!/usr/bin/env pwsh
<#
.SYNOPSIS
  生产验证并确定性打包 LPWP 1.0 参考 Widget。
.PARAMETER OutputDir
  输出目录，默认 artifacts/reference-widget-package。
.PARAMETER Force
  允许替换同名本地包。
#>
param(
    [string] $OutputDir = "artifacts/reference-widget-package",
    [switch] $Force
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$pluginDirectory = Join-Path $root "samples\LongWidgetReference"
$expectedHashFile = Join-Path $root "samples\LongWidgetReference.package.sha256"
$packer = Join-Path $root "pack-plugin.ps1"

if (-not (Test-Path -LiteralPath $pluginDirectory -PathType Container)) {
    throw "参考 Widget 源码目录不存在：$pluginDirectory"
}
if (-not (Test-Path -LiteralPath $packer -PathType Leaf)) {
    throw "插件打包脚本不存在：$packer"
}
if (-not (Test-Path -LiteralPath $expectedHashFile -PathType Leaf)) {
    throw "参考 Widget 确定性哈希基线不存在：$expectedHashFile"
}

$packerArguments = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", $packer,
    "-PluginDir", $pluginDirectory,
    "-OutputDir", $OutputDir
)
if ($Force) {
    $packerArguments += "-Force"
}

& powershell @packerArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputDir)) {
    [IO.Path]::GetFullPath($OutputDir)
} else {
    [IO.Path]::GetFullPath((Join-Path $root $OutputDir))
}
$packagePath = Join-Path $resolvedOutput "com-long-reference-widgets-v1.1.0.lpak"
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
    throw "参考 Widget 成品不存在：$packagePath"
}

$expectedHash = ((Get-Content -LiteralPath $expectedHashFile -Raw).Trim() -split '\s+')[0]
$actualHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
if (-not [string]::Equals(
        $expectedHash,
        $actualHash,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "参考 Widget 确定性哈希不匹配。期望 $expectedHash，实际 $actualHash"
}
Write-Host "  确定性基线 : matched" -ForegroundColor Green
