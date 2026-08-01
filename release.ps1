#!/usr/bin/env pwsh
<#
.SYNOPSIS
  构建、验证并打包 Long助手 Windows x64 发布候选版本。
.EXAMPLE
  .\release.ps1
.EXAMPLE
  .\release.ps1 -PackageKind SelfContained
#>
param(
    [string] $Version = '1.11.0-rc.3',
    [ValidateSet('All', 'FrameworkDependent', 'SelfContained')]
    [string] $PackageKind = 'All',
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$evidenceIo = Join-Path $repoRoot 'release-evidence-io.ps1'
if (-not (Test-Path -LiteralPath $evidenceIo -PathType Leaf)) {
    throw "Release evidence writer was not found: $evidenceIo"
}
. $evidenceIo
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$project = Join-Path $repoRoot 'src\LongBetterWindows.Host\LongBetterWindows.Host.csproj'
$tests = Join-Path $repoRoot 'tests\LongBetterWindows.Tests\LongBetterWindows.Tests.csproj'
$releaseBase = Join-Path $repoRoot 'artifacts\releases'
$releaseRoot = Join-Path $releaseBase "v$Version"
$expectedPluginCount = 25
$expectedCommandCount = 42
$smokeCommandKey = 'com.long.base64:base64.encode'
$smokeTimeoutMilliseconds = 60000
$webViewCleanupTimeoutSeconds = 45

function Get-ProductWebViewProcessIds {
    return @(
        Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" |
            Where-Object {
                $_.CommandLine -like '*--webview-exe-name=LongBetterWindows.Host.exe*'
            } |
            ForEach-Object ProcessId
    )
}

function Wait-ForNoAddedProductWebViewProcesses(
    [int[]] $BaselineProcessIds,
    [int] $TimeoutSeconds = 15
) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $current = Get-ProductWebViewProcessIds
        $added = @($current | Where-Object { $_ -notin $BaselineProcessIds })
        if ($added.Count -eq 0) { return @() }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    return $added
}

function New-ReleaseZip([string] $SourceDirectory, [string] $DestinationPath) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $sourceRoot = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd('\') + '\'
    $archive = [IO.Compression.ZipFile]::Open($DestinationPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $SourceDirectory -File -Recurse) {
            if (-not $file.FullName.StartsWith($sourceRoot, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Release archive input escapes source root: $($file.FullName)"
            }
            $entryName = $file.FullName.Substring($sourceRoot.Length).Replace('\', '/')
            $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
            $entryStream = $entry.Open()
            $fileStream = [IO.File]::OpenRead($file.FullName)
            try { $fileStream.CopyTo($entryStream) }
            finally {
                $fileStream.Dispose()
                $entryStream.Dispose()
            }
        }
    }
    finally { $archive.Dispose() }
}

function Assert-ReleaseZipLayout([string] $ZipPath) {
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $hostCount = @($archive.Entries | Where-Object { $_.FullName -eq 'LongBetterWindows.Host.exe' }).Count
        $manifestCount = @($archive.Entries | Where-Object {
            $_.FullName -match '^Plugins/[^/]+/manifest\.json$'
        }).Count
        if ($hostCount -ne 1 -or $manifestCount -ne $expectedPluginCount) {
            throw "ZIP layout check failed: Hosts=$hostCount, PluginManifests=$manifestCount"
        }
        $runtimeStateEntries = @($archive.Entries | Where-Object {
            $_.FullName -match '(^|/)LongBetterWindows\.Host\.exe\.WebView2/'
        })
        if ($runtimeStateEntries.Count -gt 0) {
            throw "ZIP layout check failed: runtime-generated WebView2 state was packaged."
        }
    }
    finally { $archive.Dispose() }
}

function Remove-PublishRuntimeState([string] $PublishDirectory) {
    $publishRoot = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd('\') + '\'
    $webViewState = [IO.Path]::GetFullPath(
        (Join-Path $PublishDirectory 'LongBetterWindows.Host.exe.WebView2'))
    if (-not ($webViewState + '\').StartsWith(
        $publishRoot,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean runtime state outside the publish directory: $webViewState"
    }
    if (Test-Path -LiteralPath $webViewState) {
        Remove-Item -LiteralPath $webViewState -Recurse -Force
    }
}

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
        $manifestFiles = @(Get-ChildItem -LiteralPath $pluginDirectory -Filter manifest.json -File -Recurse)
        $manifestCount = $manifestFiles.Count
        $pluginManifests = @($manifestFiles | ForEach-Object {
            Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        })
        $pluginIds = @($pluginManifests | ForEach-Object id)
        $uniquePluginIdCount = @($pluginIds | Sort-Object -Unique).Count
        $commandCount = ($pluginManifests | ForEach-Object {
            @($_.commands).Count
        } | Measure-Object -Sum).Sum
        $hasMissingPluginId = @($pluginIds | Where-Object {
            [string]::IsNullOrWhiteSpace($_)
        }).Count -gt 0
        if (-not (Test-Path -LiteralPath $hostExecutable) `
            -or $pluginCount -ne $expectedPluginCount `
            -or $manifestCount -ne $expectedPluginCount `
            -or $uniquePluginIdCount -ne $expectedPluginCount `
            -or $hasMissingPluginId `
            -or $commandCount -ne $expectedCommandCount) {
            throw "$($variant.Name) 完整性检查失败：Plugins=$pluginCount, Manifests=$manifestCount, UniqueIds=$uniquePluginIdCount, Commands=$commandCount"
        }

        Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\安装升级与卸载.md') -Destination $publishDirectory
        Copy-Item -LiteralPath (Join-Path $repoRoot 'CHANGELOG.md') -Destination $publishDirectory

        $smokeStopwatch = [Diagnostics.Stopwatch]::StartNew()
        $process = Start-Process -FilePath $hostExecutable `
            -ArgumentList @('--theme', 'dark', '--quality-idle-ms', '1200') `
            -WorkingDirectory $smokeDirectory -PassThru
        if (-not $process.WaitForExit($smokeTimeoutMilliseconds)) {
            Stop-Process -Id $process.Id -Force
            throw "$($variant.Name) 冒烟测试超时。"
        }
        $smokeStopwatch.Stop()
        if ($process.ExitCode -ne 0) { throw "$($variant.Name) 冒烟测试退出码为 $($process.ExitCode)。" }

        $webViewBefore = Get-ProductWebViewProcessIds
        $commandStopwatch = [Diagnostics.Stopwatch]::StartNew()
        $commandProcess = Start-Process -FilePath $hostExecutable `
            -ArgumentList @(
                '--run-command', $smokeCommandKey,
                '--command-text', 'release-smoke',
                '--exit-after-command') `
            -WorkingDirectory $smokeDirectory -PassThru
        if (-not $commandProcess.WaitForExit($smokeTimeoutMilliseconds)) {
            Stop-Process -Id $commandProcess.Id -Force
            throw "$($variant.Name) 真实命令冒烟测试超时。"
        }
        $commandStopwatch.Stop()
        if ($commandProcess.ExitCode -ne 0) {
            throw "$($variant.Name) 真实命令冒烟测试退出码为 $($commandProcess.ExitCode)。"
        }
        $webViewCleanupStopwatch = [Diagnostics.Stopwatch]::StartNew()
        $addedWebViewProcessIds = @(
            Wait-ForNoAddedProductWebViewProcesses `
                -BaselineProcessIds $webViewBefore `
                -TimeoutSeconds $webViewCleanupTimeoutSeconds)
        $webViewCleanupStopwatch.Stop()
        if ($addedWebViewProcessIds.Count -gt 0) {
            throw "$($variant.Name) 真实命令退出后残留 WebView2 进程：$($addedWebViewProcessIds -join ', ')"
        }
        Remove-PublishRuntimeState -PublishDirectory $publishDirectory

        $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($hostExecutable).ProductVersion
        $semanticProductVersion = $productVersion.Split('+')[0]
        if ($semanticProductVersion -ne $Version) {
            throw "$($variant.Name) 文件产品版本为 $productVersion，预期为 $Version。"
        }

        $zipName = "LongBetterWindows-v$Version-win-x64-$($variant.Name).zip"
        $zipPath = Join-Path $releaseRoot $zipName
        New-ReleaseZip -SourceDirectory $publishDirectory -DestinationPath $zipPath
        Assert-ReleaseZipLayout -ZipPath $zipPath
        $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $packages += [pscustomobject]@{
            file = $zipName
            kind = $variant.Name
            sha256 = $hash
            bytes = (Get-Item -LiteralPath $zipPath).Length
            plugins = $pluginCount
            manifests = $manifestCount
            unique_plugin_ids = $uniquePluginIdCount
            commands = $commandCount
            startup_smoke_elapsed_ms = [math]::Round($smokeStopwatch.Elapsed.TotalMilliseconds)
            command_smoke = $smokeCommandKey
            command_smoke_exit_code = $commandProcess.ExitCode
            command_smoke_elapsed_ms = [math]::Round($commandStopwatch.Elapsed.TotalMilliseconds)
            webview_cleanup_elapsed_ms = [math]::Round($webViewCleanupStopwatch.Elapsed.TotalMilliseconds)
            added_webview_processes = $addedWebViewProcessIds.Count
        }
    }

    $installers = @()
    $selfContainedDirectory = Join-Path $releaseRoot 'self-contained'
    if (Test-Path -LiteralPath $selfContainedDirectory -PathType Container) {
        $installerResultJson = & (Join-Path $repoRoot 'build-installer.ps1') `
            -SourceDirectory $selfContainedDirectory `
            -OutputDirectory $releaseRoot `
            -Version $Version `
            -NumericVersion "$(([version]($Version -replace '-.*$','')).ToString(3)).0"
        if ($LASTEXITCODE -ne 0) { throw 'Setup.exe 安装器构建失败。' }
        $installerResult = $installerResultJson | ConvertFrom-Json
        $installers += $installerResult
    }

    $checksumLines = @($packages) + @($installers) |
        ForEach-Object { "$($_.sha256)  $($_.file)" }
    Write-NewTextFileAtomically `
        -Value (($checksumLines -join [Environment]::NewLine) + [Environment]::NewLine) `
        -Path (Join-Path $releaseRoot 'SHA256SUMS.txt') `
        -Label 'Release checksum ledger'

    # Generated evidence and local secrets are intentionally untracked and do not
    # change the reproducible source tree used by this release.
    $sourceDirty = @((git status --porcelain --untracked-files=no 2>$null)).Count -gt 0
    $manifest = [ordered]@{
        schema_version = 1
        product = 'Long Assistant'
        version = $Version
        runtime = 'win-x64'
        created_at = [DateTimeOffset]::Now.ToString('o')
        commit = (git rev-parse HEAD 2>$null)
        source_dirty = $sourceDirty
        packages = $packages
        installers = $installers
        distribution_channel = 'unsigned'
        publisher_identity = 'unverified'
        security_notice = 'Windows publisher identity is not verified; validate the SHA-256 checksums before running.'
        signed = $false
        release_eligible = -not $sourceDirty
    }
    Write-NewJsonFileAtomically `
        -Value $manifest `
        -Path (Join-Path $releaseRoot 'release-manifest.json') `
        -Depth 5 `
        -Label 'Release manifest'

    Remove-Item -LiteralPath $smokeDirectory -Recurse -Force
    Write-Host "发布候选已生成：$releaseRoot" -ForegroundColor Green
    $packages | Format-Table file, kind, bytes, sha256 -AutoSize
}
finally {
    Pop-Location
}
