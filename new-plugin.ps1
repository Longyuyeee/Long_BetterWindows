#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Long窗口·全能助手 — 插件脚手架
.DESCRIPTION
  从模板创建新插件项目，自动配置 manifest、csproj、加入 .sln。
.PARAMETER Name
  插件显示名称
.PARAMETER Id
  唯一标识（反向域名格式，如 com.example.myplugin）
.PARAMETER Template
  模板类型: empty | hotkey | full
.PARAMETER Hotkey
  默认快捷键（仅 hotkey/full 模板）
.PARAMETER Author
  作者名
.EXAMPLE
  .\new-plugin.ps1 -Name "我的插件" -Id "com.example.mine" -Template hotkey
  .\new-plugin.ps1 -Name "截图工具" -Id "com.example.screenshot" -Template full
#>

param(
    [Parameter(Mandatory=$true)] [string] $Name,
    [Parameter(Mandatory=$true)] [string] $Id,
    [ValidateSet("empty", "hotkey", "full")] [string] $Template = "hotkey",
    [string] $Hotkey = "Alt+X",
    [string] $Author = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

# 生成安全目录名
$dirName = ($Id -replace '[^a-zA-Z0-9]', '-').Trim('-')
$pluginDir = "src/$dirName"
$templateDir = "src/Templates/$Template-plugin"

if (-not (Test-Path $templateDir)) {
    Write-Host "[错误] 模板不存在: $Template" -ForegroundColor Red
    Write-Host "  可用模板: empty, hotkey, full" -ForegroundColor Yellow
    exit 1
}

if (Test-Path $pluginDir) {
    Write-Host "[错误] 目录已存在: $pluginDir" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "  Long窗口·全能助手 — 插件脚手架" -ForegroundColor Cyan
Write-Host "  ─────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  名称   : $Name"
Write-Host "  ID     : $Id"
Write-Host "  模板   : $Template"
Write-Host "  快捷键 : $Hotkey"
Write-Host "  目录   : $pluginDir"
Write-Host ""

# 1. 复制模板
Write-Host "[1/4] 复制模板文件..." -ForegroundColor Yellow
Copy-Item -Recurse $templateDir $pluginDir

# 2. 重命名 .csproj 和 .cs 文件
$templatePrefix = switch ($Template) {
    "empty"  { "Empty" }
    "hotkey" { "Hotkey" }
    "full"   { "Full" }
}

$csprojOld = "$pluginDir/${templatePrefix}Plugin.csproj"
$csprojNew = "$pluginDir/$dirName.csproj"
$csOld = "$pluginDir/${templatePrefix}PluginImpl.cs"
$csNew = "$pluginDir/$($dirName)Impl.cs"

if (Test-Path $csprojOld) {
    Move-Item $csprojOld $csprojNew
}
if (Test-Path $csOld) {
    Move-Item $csOld $csNew
}

# 3. 替换占位符
Write-Host "[2/4] 替换占位符..." -ForegroundColor Yellow
$files = Get-ChildItem $pluginDir -Recurse -Include *.cs, *.csproj, *.json
foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw -Encoding UTF8
    $content = $content `
        -replace 'com\.example\.(empty|hotkey|full)', $Id `
        -replace '(空|热键|全功能)插件模板', $Name `
        -replace '(Empty|Hotkey|Full)Plugin', $dirName `
        -replace "(Empty|Hotkey|Full)PluginImpl", "$($dirName)Impl" `
        -replace 'namespace (Empty|Hotkey|Full)Plugin', "namespace $dirName" `
        -replace '"Alt\+X"', "`"$Hotkey`"" `
        -replace '"Ctrl\+Shift\+X"', "`"$Hotkey`"" `
        -replace '"author": ""', "`"author`": `"$Author`""
    Set-Content $file.FullName -Value $content -Encoding UTF8 -NoNewline
    # 确保末尾有换行
    Add-Content $file.FullName -Value ""
}

# 4. 加入 .sln
Write-Host "[3/4] 加入解决方案..." -ForegroundColor Yellow
$slnFile = (Get-ChildItem *.sln | Select-Object -First 1).Name
dotnet sln $slnFile add $csprojNew 2>&1 | Out-Null

# 5. 构建验证
Write-Host "[4/4] 构建验证..." -ForegroundColor Yellow
$buildResult = dotnet build $csprojNew 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "  插件创建成功!" -ForegroundColor Green
    Write-Host "  ────────────" -ForegroundColor DarkGray
    Write-Host "  目录 : $pluginDir"
    Write-Host "  项目 : $csprojNew"
    Write-Host ""
    Write-Host "  下一步:" -ForegroundColor Cyan
    Write-Host "  1. cd $pluginDir" -ForegroundColor White
    Write-Host "  2. 编辑 $($dirName)Impl.cs 实现你的逻辑" -ForegroundColor White
    Write-Host "  3. dotnet build  # 构建并自动复制到 Plugins/" -ForegroundColor White
    Write-Host "  4. 启动宿主，在 ToolCenter 中启用插件" -ForegroundColor White
    Write-Host ""
} else {
    Write-Host ""
    Write-Host "  构建失败！请检查错误信息。" -ForegroundColor Red
    Write-Host $buildResult
}
