#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Long助手 — 插件脚手架
.DESCRIPTION
  从模板创建新插件，自动配置 manifest；原生项目同时配置 csproj、加入 .sln 并构建。
.PARAMETER Name
  插件显示名称
.PARAMETER Id
  唯一标识（反向域名格式，如 com.example.myplugin）
.PARAMETER Template
  模板类型: empty | hotkey | full | script
.PARAMETER Hotkey
  默认快捷键（hotkey/full/script 模板）
.PARAMETER Author
  作者名
.EXAMPLE
  .\new-plugin.ps1 -Name "我的插件" -Id "com.example.mine" -Template hotkey
  .\new-plugin.ps1 -Name "截图工具" -Id "com.example.screenshot" -Template full
.EXAMPLE
  .\new-plugin.ps1 -Name "我的脚本" -Id "com.example.script" -Template script
#>

param(
    [Parameter(Mandatory=$true)] [string] $Name,
    [Parameter(Mandatory=$true)] [string] $Id,
    [ValidateSet("empty", "hotkey", "full", "script")] [string] $Template = "hotkey",
    [string] $Hotkey = "Alt+X",
    [string] $Author = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw '.NET SDK 未安装或 dotnet.exe 不在 PATH 中。'
    }
    $dotnet = $dotnetCommand.Source
}

# 生成安全目录名
$dirName = ($Id -replace '[^a-zA-Z0-9]', '-').Trim('-')
$pluginDir = "src/$dirName"
$templateDir = "src/Templates/$Template-plugin"

if (-not (Test-Path $templateDir)) {
    Write-Host "[错误] 模板不存在: $Template" -ForegroundColor Red
    Write-Host "  可用模板: empty, hotkey, full, script" -ForegroundColor Yellow
    exit 1
}

if (Test-Path $pluginDir) {
    Write-Host "[错误] 目录已存在: $pluginDir" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "  Long助手 — 插件脚手架" -ForegroundColor Cyan
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

# 2. 重命名原生项目文件；脚本插件无需项目文件
$templatePrefix = switch ($Template) {
    "empty"  { "Empty" }
    "hotkey" { "Hotkey" }
    "full"   { "Full" }
    "script" { $null }
}

$typeName = (($Id -split '[^a-zA-Z0-9]+' | Where-Object { $_ }) | ForEach-Object {
    if ($_.Length -eq 1) {
        $_.ToUpperInvariant()
    } else {
        $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1)
    }
}) -join ''
if ([string]::IsNullOrWhiteSpace($typeName)) {
    $typeName = 'LongPlugin'
}
if ($typeName[0] -match '\d') {
    $typeName = "Plugin$typeName"
}

$csprojNew = $null
$testProjectNew = $null
if ($Template -ne "script") {
    $csprojOld = "$pluginDir/${templatePrefix}Plugin.csproj"
    $csprojNew = "$pluginDir/$typeName.csproj"
    $csOld = "$pluginDir/${templatePrefix}PluginImpl.cs"
    $csNew = "$pluginDir/$($typeName)Impl.cs"

    if (Test-Path $csprojOld) {
        Move-Item $csprojOld $csprojNew
    }
    if (Test-Path $csOld) {
        Move-Item $csOld $csNew
    }

    $testProjectOld = "$pluginDir/tests/${templatePrefix}Plugin.Tests.csproj"
    $testProjectNew = "$pluginDir/tests/$typeName.Tests.csproj"
    if (Test-Path $testProjectOld) {
        Move-Item $testProjectOld $testProjectNew
    }
}

# 3. 替换占位符
Write-Host "[2/4] 替换占位符..." -ForegroundColor Yellow
$files = Get-ChildItem $pluginDir -Recurse -Include *.cs, *.csx, *.csproj, *.json
foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw -Encoding UTF8
    $content = $content `
        -replace 'com\.example\.(empty|hotkey|full|script)', $Id `
        -replace '(空|热键|全功能|脚本)插件模板', $Name `
        -replace '(Empty|Hotkey|Full)PluginImpl', "$($typeName)Impl" `
        -replace '(Empty|Hotkey|Full)Plugin', $typeName `
        -replace '"Alt\+X"', "`"$Hotkey`"" `
        -replace '"Ctrl\+Shift\+X"', "`"$Hotkey`"" `
        -replace '"author": ""', "`"author`": `"$Author`""
    Set-Content $file.FullName -Value $content -Encoding UTF8 -NoNewline
    # 确保末尾有换行
    Add-Content $file.FullName -Value ""
}

# 4. 原生项目加入解决方案；脚本插件直接校验清单和入口
if ($Template -eq "script") {
    Write-Host "[3/4] 脚本插件无需加入解决方案。" -ForegroundColor DarkGray
    Write-Host "[4/4] 校验清单和脚本入口..." -ForegroundColor Yellow
    $manifestPath = "$pluginDir/manifest.json"
    $manifest = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $entryPath = Join-Path $pluginDir $manifest.entry_point
    $validationPassed = (
        $manifest.runtime -eq "csharp-script" -and
        -not [string]::IsNullOrWhiteSpace($manifest.id) -and
        -not [string]::IsNullOrWhiteSpace($manifest.entry_point) -and
        (Test-Path -LiteralPath $entryPath -PathType Leaf)
    )
    $buildResult = if ($validationPassed) { "" } else { "manifest.json 或脚本入口无效。" }
} else {
    Write-Host "[3/4] 加入解决方案..." -ForegroundColor Yellow
    $slnFile = (Get-ChildItem *.sln | Select-Object -First 1).Name
    & $dotnet sln $slnFile add $csprojNew $testProjectNew 2>&1 | Out-Null

    Write-Host "[4/4] 构建并执行合同测试..." -ForegroundColor Yellow
    $solutionDir = $root.TrimEnd('\') + '\'
    $buildResult = & $dotnet test $testProjectNew "-p:SolutionDir=$solutionDir" 2>&1
    $validationPassed = $LASTEXITCODE -eq 0
}

if ($validationPassed) {
    Write-Host ""
    Write-Host "  插件创建成功!" -ForegroundColor Green
    Write-Host "  ────────────" -ForegroundColor DarkGray
    Write-Host "  目录 : $pluginDir"
    if ($Template -eq "script") {
        Write-Host "  入口 : $entryPath"
    } else {
        Write-Host "  项目 : $csprojNew"
        Write-Host "  测试 : $testProjectNew"
    }
    Write-Host ""
    Write-Host "  下一步:" -ForegroundColor Cyan
    Write-Host "  1. cd $pluginDir" -ForegroundColor White
    if ($Template -eq "script") {
        Write-Host "  2. 编辑 plugin.csx 实现你的逻辑" -ForegroundColor White
        Write-Host "  3. 启动宿主，在 ToolCenter 中启用插件" -ForegroundColor White
    } else {
        Write-Host "  2. 编辑 $($typeName)Impl.cs 实现你的逻辑" -ForegroundColor White
        Write-Host "  3. dotnet test $testProjectNew  # 合同测试 + 构建" -ForegroundColor White
        Write-Host "  4. 启动宿主，在 ToolCenter 中启用插件" -ForegroundColor White
    }
    Write-Host ""
} else {
    Write-Host ""
    Write-Host "  创建后验证失败！请检查错误信息。" -ForegroundColor Red
    Write-Host $buildResult
    exit 1
}
