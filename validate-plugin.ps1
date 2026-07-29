#!/usr/bin/env pwsh
<#
.SYNOPSIS
  使用 Long助手生产验证规则检查插件目录或 .lpak 包。
.PARAMETER Path
  插件目录或 .lpak 文件路径。
.PARAMETER InstalledPluginDirectory
  可选。现有插件目录，用于输出新增、移除和不变的权限。
.EXAMPLE
  .\validate-plugin.ps1 -Path "Plugins\MyPlugin"
.EXAMPLE
  .\validate-plugin.ps1 -Path "dist\com-example-myplugin-v1.0.0.lpak"
#>
param(
    [Parameter(Mandatory=$true)] [string] $Path,
    [string] $InstalledPluginDirectory
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$toolProject = Join-Path $root "tools\LongBetterWindows.PluginValidator\LongBetterWindows.PluginValidator.csproj"

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw '.NET SDK 未安装或 dotnet.exe 不在 PATH 中。'
    }
    $dotnet = $dotnetCommand.Source
}

if (-not (Test-Path -LiteralPath $toolProject -PathType Leaf)) {
    throw "插件验证工具项目不存在：$toolProject"
}

$target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
$arguments = @(
    "run",
    "--project", $toolProject,
    "-c", "Release",
    "--no-launch-profile",
    "--",
    $target
)
if (-not [string]::IsNullOrWhiteSpace($InstalledPluginDirectory)) {
    $installed = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
        $InstalledPluginDirectory)
    $arguments += @("--installed", $installed)
}

& $dotnet @arguments
exit $LASTEXITCODE
