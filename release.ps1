#!/usr/bin/env pwsh
<#
.SYNOPSIS
  构建、验证并打包 Long窗口 Windows x64 发布候选版本。
.EXAMPLE
  .\release.ps1
.EXAMPLE
  .\release.ps1 -PackageKind SelfContained
#>
param(
    [string] $Version = '1.9.0',
    [ValidateSet('All', 'FrameworkDependent', 'SelfContained')]
    [string] $PackageKind = 'All',
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$project = Join-Path $repoRoot 'src\LongBetterWindows.Host\LongBetterWindows.Host.csproj'
$tests = Join-Path $repoRoot 'tests\LongBetterWindows.Tests\LongBetterWindows.Tests.csproj'
$releaseBase = Join-Path $repoRoot 'artifacts\releases'
$releaseRoot = Join-Path $releaseBase "v$Version"

if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) { throw '未找到 dotnet CLI。发布需要 .NET SDK。' }
    $dotnet = $dotnetCommand.Source
}

$projectText = Get-Content -LiteralPath $project -Raw -Encoding UTF8
$declaredVersion = [regex]::Match($projectText, '<Version>([^<]+)</Version>').Groups[1].Value
if ($declaredVersion -ne $Version) {
    throw "请求版本 $Version 与项目版本 $declaredVersion 不一致。请先统一版本元数据。"
}

if (Test-Path -LiteralPath $releaseRoot) {
    if (-not $Force) { throw "候选目录已存在：$releaseRoot。确认覆盖时使用 -Force。" }
    $resolvedBase = [IO.Path]::GetFullPath($releaseBase).TrimEnd('\') + '\'
    $resolvedTarget = [IO.Path]::GetFullPath($releaseRoot).TrimEnd('\') + '\'
    if (-not $resolvedTarget.StartsWith($resolvedBase, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理发布目录之外的路径：$resolvedTarget"
    }
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $releaseRoot | Out-Null
$smokeDirectory = Join-Path $releaseRoot 'smoke'
New-Item -ItemType Directory -Path $smokeDirectory | Out-Null

Push-Location $repoRoot
try {
    & $dotnet build 'LongBetterWindows.sln' -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Release 构建失败。' }

    & $dotnet test $tests -c Release --no-build --logger 'console;verbosity=minimal'
    if ($LASTEXITCODE -ne 0) { throw 'Release 自动化测试失败。' }

    $variants = switch ($PackageKind) {
        'FrameworkDependent' { @(@{ Name = 'framework-dependent'; SelfContained = $false }) }
        'SelfContained' { @(@{ Name = 'self-contained'; SelfContained = $true }) }
        default {
            @(
                @{ Name = 'self-contained'; SelfContained = $true },
                @{ Name = 'framework-dependent'; SelfContained = $false }
            )
        }
    }

    $packages = @()
    foreach ($variant in $variants) {
        $publishDirectory = Join-Path $releaseRoot $variant.Name
        & $dotnet publish $project -c Release -r win-x64 `
            --self-contained $variant.SelfContained.ToString().ToLowerInvariant() `
            -o $publishDirectory
        if ($LASTEXITCODE -ne 0) { throw "$($variant.Name) 发布失败。" }

        $hostExecutable = Join-Path $publishDirectory 'LongBetterWindows.Host.exe'
        $pluginDirectory = Join-Path $publishDirectory 'Plugins'
        $pluginCount = @(Get-ChildItem -LiteralPath $pluginDirectory -Directory).Count
        $manifestCount = @(Get-ChildItem -LiteralPath $pluginDirectory -Filter manifest.json -File -Recurse).Count
        if (-not (Test-Path -LiteralPath $hostExecutable) -or $pluginCount -ne 25 -or $manifestCount -ne 25) {
            throw "$($variant.Name) 完整性检查失败：Plugins=$pluginCount, Manifests=$manifestCount"
        }

        Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\安装升级与卸载.md') -Destination $publishDirectory
        Copy-Item -LiteralPath (Join-Path $repoRoot 'CHANGELOG.md') -Destination $publishDirectory

        $process = Start-Process -FilePath $hostExecutable `
            -ArgumentList @('--theme', 'dark', '--quality-idle-ms', '1200') `
            -WorkingDirectory $smokeDirectory -PassThru
        if (-not $process.WaitForExit(20000)) {
            Stop-Process -Id $process.Id -Force
            throw "$($variant.Name) 冒烟测试超时。"
        }
        if ($process.ExitCode -ne 0) { throw "$($variant.Name) 冒烟测试退出码为 $($process.ExitCode)。" }

        $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($hostExecutable).ProductVersion
        $semanticProductVersion = $productVersion.Split('+')[0]
        if ($semanticProductVersion -ne $Version) {
            throw "$($variant.Name) 文件产品版本为 $productVersion，预期为 $Version。"
        }

        $zipName = "LongBetterWindows-v$Version-win-x64-$($variant.Name).zip"
        $zipPath = Join-Path $releaseRoot $zipName
        Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
        $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $packages += [pscustomobject]@{
            file = $zipName
            kind = $variant.Name
            sha256 = $hash
            bytes = (Get-Item -LiteralPath $zipPath).Length
            plugins = $pluginCount
        }
    }

    $checksumLines = $packages | ForEach-Object { "$($_.sha256)  $($_.file)" }
    $checksumLines | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding UTF8

    $sourceDirty = @((git status --porcelain 2>$null)).Count -gt 0
    $manifest = [ordered]@{
        product = 'Long窗口·全能助手'
        version = $Version
        runtime = 'win-x64'
        created_at = [DateTimeOffset]::Now.ToString('o')
        commit = (git rev-parse HEAD 2>$null)
        source_dirty = $sourceDirty
        packages = $packages
        signed = $false
        release_eligible = $false
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content `
        -LiteralPath (Join-Path $releaseRoot 'release-manifest.json') -Encoding UTF8

    Remove-Item -LiteralPath $smokeDirectory -Recurse -Force
    Write-Host "发布候选已生成：$releaseRoot" -ForegroundColor Green
    $packages | Format-Table file, kind, bytes, sha256 -AutoSize
}
finally {
    Pop-Location
}
