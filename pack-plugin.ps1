#!/usr/bin/env pwsh
<#
.SYNOPSIS
  验证并确定性打包 Long助手插件。
.DESCRIPTION
  先使用生产规则验证插件目录，再审计包内容、生成文件 SHA-256 总账，
  以固定顺序和时间戳创建 .lpak，最后再次使用生产包验证器复核成品。
.PARAMETER PluginDir
  插件发布目录。原生插件应传入包含构建后 DLL 的 Plugins\<插件> 目录。
.PARAMETER OutputDir
  输出目录，默认 ./dist。
.PARAMETER Force
  允许替换同名本地输出；默认拒绝覆盖。
.EXAMPLE
  .\pack-plugin.ps1 -PluginDir "src\Base64Tool"
.EXAMPLE
  .\pack-plugin.ps1 -PluginDir "Plugins\FolderNotePlugin" -Force
#>
param(
    [Parameter(Mandatory=$true)] [string] $PluginDir,
    [string] $OutputDir = "dist",
    [switch] $Force
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$powerShellHost = (Get-Process -Id $PID).Path
$validatorScript = Join-Path $root "validate-plugin.ps1"
$fixedTimestamp = [DateTimeOffset]::new(
    1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
$reservedLedgerName = "package-files.json"
$blockedDirectories = @(".git", ".vs", "bin", "obj", "node_modules")
$blockedFilePatterns = @(
    "^\.env(?:\.|$)",
    "\.(?:pfx|p12|snk|suo|user|tmp|bak)$",
    "^(?:id_rsa|id_ed25519)$"
)

function Test-IsWithin {
    param([string] $Parent, [string] $Candidate)
    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $candidateFull = [IO.Path]::GetFullPath($Candidate)
    return $candidateFull.StartsWith(
        $parentFull,
        [StringComparison]::OrdinalIgnoreCase)
}

function Invoke-ProductionValidation {
    param(
        [string] $Target,
        [switch] $NoBuild
    )
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $validatorScript,
        "-Path", $Target
    )
    if ($NoBuild) {
        $arguments += "-NoBuild"
    }

    $output = & $powerShellHost @arguments
    $exitCode = $LASTEXITCODE
    try {
        $report = $output | ConvertFrom-Json
    } catch {
        throw "插件验证器未返回有效 JSON：$($output -join [Environment]::NewLine)"
    }
    if ($exitCode -ne 0 -or -not $report.is_success) {
        throw "插件验证失败：$($report.error)"
    }
    return $report
}

if (-not (Test-Path -LiteralPath $validatorScript -PathType Leaf)) {
    throw "插件验证脚本不存在：$validatorScript"
}

$pluginRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
    $PluginDir)
if (-not (Test-Path -LiteralPath $pluginRoot -PathType Container)) {
    throw "插件目录不存在：$pluginRoot"
}
if ((Get-Item -LiteralPath $pluginRoot -Force).Attributes.HasFlag(
    [IO.FileAttributes]::ReparsePoint)) {
    throw "插件根目录不能是重解析点：$pluginRoot"
}

$preflight = Invoke-ProductionValidation -Target $pluginRoot
$manifest = $preflight.manifest
$safeName = $manifest.id -replace '[^a-zA-Z0-9\-]', '-'
if ([string]::IsNullOrWhiteSpace($safeName)) {
    throw "插件 ID 无法生成安全包名。"
}

$outDir = if ([IO.Path]::IsPathRooted($OutputDir)) {
    [IO.Path]::GetFullPath($OutputDir)
} else {
    [IO.Path]::GetFullPath((Join-Path $root $OutputDir))
}
if (Test-IsWithin -Parent $pluginRoot -Candidate $outDir) {
    throw "输出目录不能位于插件目录内部。"
}
if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

$outputFile = Join-Path $outDir "$safeName-v$($manifest.version).lpak"
if (Test-Path -LiteralPath $outputFile) {
    if (-not $Force) {
        throw "输出文件已存在；请更换版本/目录，或显式使用 -Force：$outputFile"
    }
    Remove-Item -LiteralPath $outputFile -Force
}

$directories = @(Get-ChildItem -LiteralPath $pluginRoot -Directory -Recurse -Force)
$reparseDirectory = $directories | Where-Object {
    $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)
} | Select-Object -First 1
if ($null -ne $reparseDirectory) {
    throw "插件目录包含重解析点：$($reparseDirectory.FullName)"
}

$sourceFiles = @(
    Get-ChildItem -LiteralPath $pluginRoot -File -Recurse -Force |
        ForEach-Object {
            if (-not (Test-IsWithin -Parent $pluginRoot -Candidate $_.FullName)) {
                throw "插件文件超出插件根目录：$($_.FullName)"
            }
            $relative = $_.FullName.Substring(
                $pluginRoot.TrimEnd('\', '/').Length + 1).Replace('\', '/')
            $segments = $relative.Split('/')
            if ($blockedDirectories | Where-Object {
                $segments -contains $_
            }) {
                throw "插件目录包含禁止打包的缓存目录：$relative"
            }
            if ([string]::Equals(
                $relative,
                $reservedLedgerName,
                [StringComparison]::OrdinalIgnoreCase)) {
                throw "插件目录不能自带保留文件：$reservedLedgerName"
            }
            foreach ($pattern in $blockedFilePatterns) {
                if ($_.Name -match $pattern) {
                    throw "插件目录包含禁止打包的临时或敏感文件：$relative"
                }
            }
            if ($_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
                throw "插件目录包含重解析点文件：$relative"
            }
            [pscustomobject]@{
                File = $_
                Path = $relative
                Size = $_.Length
                Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        } |
        Sort-Object Path
)

if ($sourceFiles.Count -gt 2047) {
    throw "插件源文件数量超过安全限制（最多 2047 个，另保留一份文件总账）。"
}
$totalBytes = ($sourceFiles | Measure-Object -Property Size -Sum).Sum
if ($null -eq $totalBytes) {
    $totalBytes = 0
}
if ($totalBytes -gt 256MB) {
    throw "插件源文件总大小超过 256 MB 安全限制。"
}

$ledger = [ordered]@{
    schema_version = 1
    classification = "long_plugin_file_manifest"
    plugin_id = $manifest.id
    version = $manifest.version
    files = @($sourceFiles | ForEach-Object {
        [ordered]@{
            path = $_.Path
            size = $_.Size
            sha256 = $_.Sha256
        }
    })
}
$ledgerJson = ($ledger | ConvertTo-Json -Depth 6 -Compress) + "`n"
$ledgerBytes = [Text.UTF8Encoding]::new($false).GetBytes($ledgerJson)

Add-Type -AssemblyName System.IO.Compression
$temporaryFile = Join-Path $outDir (
    ".{0}.{1}.tmp" -f [IO.Path]::GetFileName($outputFile), [guid]::NewGuid().ToString("N"))
try {
    $stream = [IO.File]::Open(
        $temporaryFile,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::ReadWrite,
        [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $stream,
            [IO.Compression.ZipArchiveMode]::Create,
            $true,
            [Text.Encoding]::UTF8)
        try {
            foreach ($item in $sourceFiles) {
                $entry = $archive.CreateEntry(
                    $item.Path,
                    [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTimestamp
                $entryStream = $entry.Open()
                try {
                    $input = [IO.File]::OpenRead($item.File.FullName)
                    try {
                        $input.CopyTo($entryStream)
                    } finally {
                        $input.Dispose()
                    }
                } finally {
                    $entryStream.Dispose()
                }
            }

            $ledgerEntry = $archive.CreateEntry(
                $reservedLedgerName,
                [IO.Compression.CompressionLevel]::Optimal)
            $ledgerEntry.LastWriteTime = $fixedTimestamp
            $ledgerStream = $ledgerEntry.Open()
            try {
                $ledgerStream.Write($ledgerBytes, 0, $ledgerBytes.Length)
            } finally {
                $ledgerStream.Dispose()
            }
        } finally {
            $archive.Dispose()
        }
    } finally {
        $stream.Dispose()
    }

    [IO.File]::Move($temporaryFile, $outputFile)
} catch {
    if (Test-Path -LiteralPath $temporaryFile) {
        Remove-Item -LiteralPath $temporaryFile -Force
    }
    throw
}

$postflight = Invoke-ProductionValidation -Target $outputFile -NoBuild
$size = (Get-Item -LiteralPath $outputFile).Length
$requestedPermissions = @($postflight.permission_summary.requested)
$permissionText = if ($requestedPermissions.Count -eq 0) {
    "无"
} else {
    $requestedPermissions -join ", "
}
$localImport = $postflight.distribution_eligibility.local_import
$remoteMarketplace = $postflight.distribution_eligibility.remote_marketplace
$localText = if ($localImport.requires_high_trust_warning) {
    "可导入（需要完全信任确认）"
} else {
    "可导入"
}
$remoteText = if ($remoteMarketplace.package_eligible) {
    if ($remoteMarketplace.currently_trusted) {
        "可发布（发布者签名已验证）"
    } else {
        "包形态合格（仍需发布者签名）"
    }
} else {
    "不可发布（$($remoteMarketplace.block_reason)）"
}

Write-Host ""
Write-Host "  Long助手插件包已完成" -ForegroundColor Green
Write-Host "  ──────────────────" -ForegroundColor DarkGray
Write-Host "  ID       : $($manifest.id)"
Write-Host "  版本     : $($manifest.version)"
Write-Host "  文件     : $outputFile"
Write-Host "  大小     : $([math]::Round($size / 1KB, 1)) KB"
Write-Host "  源文件   : $($sourceFiles.Count)"
Write-Host "  SHA-256  : $($postflight.sha256)"
Write-Host "  请求权限 : $permissionText"
Write-Host "  本地导入 : $localText"
Write-Host "  远程市场 : $remoteText"
Write-Host "  验证状态 : passed"
Write-Host ""
