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
$packer = Join-Path $root "pack-plugin.ps1"

if (-not (Test-Path -LiteralPath $pluginDirectory -PathType Container)) {
    throw "参考 Widget 源码目录不存在：$pluginDirectory"
}
if (-not (Test-Path -LiteralPath $packer -PathType Leaf)) {
    throw "插件打包脚本不存在：$packer"
}

$arguments = @{
    PluginDir = $pluginDirectory
    OutputDir = $OutputDir
}
if ($Force) {
    $arguments.Force = $true
}

& $packer @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
