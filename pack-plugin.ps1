#!/usr/bin/env pwsh
<#
.SYNOPSIS
  将插件目录打包为 .lpak 文件。
.DESCRIPTION
  读取插件目录，验证 manifest.json，打包为 ZIP 格式的 .lpak 文件。
  输出到 ./dist/ 目录。
.PARAMETER PluginDir
  插件目录路径（包含 manifest.json 和 .dll 的目录）
.EXAMPLE
  .\pack-plugin.ps1 -PluginDir "Plugins/FolderNotePlugin"
#>
param(
    [Parameter(Mandatory=$true)] [string] $PluginDir,
    [string] $OutputDir = "dist"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if (-not (Test-Path "$PluginDir/manifest.json")) {
    Write-Host "[错误] 未找到 manifest.json: $PluginDir" -ForegroundColor Red
    exit 1
}

$manifest = Get-Content "$PluginDir/manifest.json" -Raw | ConvertFrom-Json
$name = $manifest.id -replace '[^a-zA-Z0-9\-]', '-'
$version = $manifest.version
$outDir = Join-Path $root $OutputDir

if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

$outputFile = Join-Path $outDir "$name-v$version.lpak"

Write-Host ""
Write-Host "  打包插件" -ForegroundColor Cyan
Write-Host "  ────────" -ForegroundColor DarkGray
Write-Host "  名称: $($manifest.name)"
Write-Host "  ID  : $($manifest.id)"
Write-Host "  版本: $version"
Write-Host "  输出: $outputFile"
Write-Host ""

# 删除旧输出
if (Test-Path $outputFile) {
    Remove-Item $outputFile
}

# 打包为 ZIP
Compress-Archive -Path "$PluginDir/*" -DestinationPath $outputFile -Force

# 重命名 .zip → .lpak (Compress-Archive 自动加 .zip)
$zipFile = "$outputFile.zip"
if (Test-Path $zipFile) {
    Move-Item $zipFile $outputFile -Force
}

$size = (Get-Item $outputFile).Length
Write-Host "  打包完成: $([math]::Round($size/1KB, 1)) KB" -ForegroundColor Green
Write-Host "  文件: $outputFile"
Write-Host ""
Write-Host "  分发方式:" -ForegroundColor Cyan
Write-Host "  1. 直接拖放 .lpak 到 ToolCenter 安装"
Write-Host "  2. 复制到 Plugins/ 目录，重启宿主自动安装"
Write-Host ""
