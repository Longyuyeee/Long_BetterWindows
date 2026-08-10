#!/usr/bin/env pwsh
param(
    [string] $ApprovalDirectory = 'docs/final-validation-approvals',
    [string] $SubjectExecutable =
        'src/LongBetterWindows.Host/bin/Release/net8.0-windows/LongBetterWindows.Host.exe',
    [Parameter(Mandatory=$true)] [string] $OutputPath,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'release-evidence-io.ps1')

function Resolve-RepositoryPath([string] $PathValue) {
    if ([IO.Path]::IsPathRooted($PathValue)) {
        return [IO.Path]::GetFullPath($PathValue)
    }
    return [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

$requiredIds = @(
    'taskbar-visual-grouping',
    'native-performance',
    'lpwp-long-grid-e2e',
    'lpwp-widget-desktop',
    'lpwp-signed-reference')
$currentHead = (& git -C $PSScriptRoot rev-parse HEAD).Trim().ToLowerInvariant()
$sourceCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit.'
}
& git -C $PSScriptRoot merge-base --is-ancestor $sourceCommit HEAD 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'ExpectedSourceCommit must be an ancestor of HEAD.'
}
$trackedStatus = @(& git -C $PSScriptRoot status --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0 -or $trackedStatus.Count -ne 0) {
    throw 'Final product acceptance requires a clean tracked worktree.'
}
$postCandidateChanges = @(& git -C $PSScriptRoot diff --name-only $sourceCommit HEAD --)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to compare HEAD with ExpectedSourceCommit.'
}
$unexpectedChanges = @($postCandidateChanges | Where-Object {
    $_ -notmatch '^docs/(plugin-manual-approvals|final-validation-approvals)/[^/]+\.json$'
})
if ($unexpectedChanges.Count -ne 0) {
    throw ('Product files changed after ExpectedSourceCommit: ' + ($unexpectedChanges -join ', '))
}
$hostPath = Resolve-RepositoryPath $SubjectExecutable
if (-not (Test-Path -LiteralPath $hostPath -PathType Leaf)) {
    throw "Release host executable was not found: $hostPath"
}
$hostHash = (Get-FileHash -LiteralPath $hostPath -Algorithm SHA256).Hash.ToLowerInvariant()

$matrixOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass
    -File (Join-Path $PSScriptRoot 'verify-plugin-positive-matrix.ps1')
    -RequireReleaseEligible 2>&1)
$matrixExitCode = $LASTEXITCODE
$matrix = $null
try { $matrix = ($matrixOutput -join [Environment]::NewLine) | ConvertFrom-Json } catch { }
if ($matrixExitCode -ne 0 -or $null -eq $matrix `
    -or -not [bool]$matrix.contract_valid `
    -or -not [bool]$matrix.release_eligible `
    -or [string]$matrix.source_commit -ne $currentHead `
    -or [bool]$matrix.source_dirty `
    -or [int]$matrix.plugin_count -ne 25 `
    -or [int]$matrix.command_count -ne 42 `
    -or [int]$matrix.approval_receipt_count -ne 25) {
    throw 'Plugin positive matrix is not release eligible with 25 approved plugins and 42 commands.'
}

$approvalRoot = Resolve-RepositoryPath $ApprovalDirectory
if (-not (Test-Path -LiteralPath $approvalRoot -PathType Container)) {
    throw "Final validation approval directory was not found: $approvalRoot"
}
$receiptFiles = @(Get-ChildItem -LiteralPath $approvalRoot -Filter '*.json' -File)
if ($receiptFiles.Count -ne $requiredIds.Count) {
    throw "Exactly $($requiredIds.Count) final validation approval receipts are required."
}
$receiptIds = @($receiptFiles | ForEach-Object { $_.BaseName } | Sort-Object -Unique)
if ($receiptIds.Count -ne $requiredIds.Count `
    -or (Compare-Object ($requiredIds | Sort-Object) $receiptIds).Count -ne 0) {
    throw 'Final validation approval receipt file set is incomplete or contains unknown IDs.'
}

$validated = @($receiptFiles | Sort-Object BaseName | ForEach-Object {
    & git -C $PSScriptRoot ls-files --error-unmatch -- $_.FullName 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Final validation approval receipt must be committed to Git: $($_.Name)"
    }
    $receipt = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    $id = [string]$receipt.validation_id
    if ([int]$receipt.schema_version -ne 1 `
        -or [string]$receipt.classification -ne 'final_validation_approval' `
        -or $id -ne $_.BaseName `
        -or $id -notin $requiredIds `
        -or [string]$receipt.status -ne 'passed' `
        -or [string]$receipt.source_commit -ne $sourceCommit `
        -or [string]::IsNullOrWhiteSpace([string]$receipt.reviewer) `
        -or [string]::IsNullOrWhiteSpace([string]$receipt.notes) `
        -or [string]$receipt.subject_executable_sha256 -ne $hostHash) {
        throw "Final validation approval identity is invalid: $($_.Name)"
    }
    $evidence = @($receipt.evidence_files)
    if ($evidence.Count -eq 0) {
        throw "Final validation approval has no evidence metadata: $id"
    }
    foreach ($item in $evidence) {
        if ([string]$item.relative_path -notlike 'artifacts/quality/*' `
            -or [string]$item.sha256 -notmatch '^[0-9a-f]{64}$' `
            -or [long]$item.size_bytes -le 0) {
            throw "Final validation approval evidence metadata is invalid: $id"
        }
    }
    if ($id -eq 'native-performance' `
        -and ([string]$receipt.verified_contract.native_performance_manifest_sha256 -notmatch '^[0-9a-f]{64}$' `
            -or [string]$receipt.verified_contract.native_performance_analysis_sha256 -notmatch '^[0-9a-f]{64}$' `
            -or [string]$receipt.verified_contract.native_performance_export_sha256 -notmatch '^[0-9a-f]{64}$' `
            -or [string]::IsNullOrWhiteSpace(
                [string]$receipt.verified_contract.native_performance_analyst))) {
        throw 'Native performance approval contract is missing structured WPA analysis.'
    }
    if ($id -eq 'lpwp-long-grid-e2e' `
        -and ([string]$receipt.verified_contract.protocol -ne 'long.plugin.ipc/1.0' `
            -or [string]$receipt.verified_contract.long_grid_commit -notmatch '^[0-9a-f]{40}$')) {
        throw 'Long Grid E2E approval contract is missing.'
    }
    if ($id -eq 'lpwp-signed-reference' `
        -and ([string]$receipt.verified_contract.publisher_key_id -eq '' `
            -or [string]$receipt.verified_contract.public_key_fingerprint -notmatch '^[0-9A-F]{64}$' `
            -or [string]$receipt.verified_contract.package_sha256 -notmatch '^[0-9a-f]{64}$')) {
        throw 'Signed reference approval contract is missing.'
    }
    [ordered]@{
        id = $id
        path = $_.FullName
        receipt = $receipt
    }
})

$resolvedOutput = Resolve-RepositoryPath $OutputPath
if (Test-Path -LiteralPath $resolvedOutput) {
    throw "Final product-acceptance summary already exists: $resolvedOutput"
}
$outputParent = Split-Path -Parent $resolvedOutput
[IO.Directory]::CreateDirectory($outputParent) | Out-Null
$sourceDirectoryName = [IO.Path]::GetFileNameWithoutExtension($resolvedOutput) + '.sources'
$sourceDirectory = Join-Path $outputParent $sourceDirectoryName
if (Test-Path -LiteralPath $sourceDirectory) {
    throw "Final product-acceptance source directory already exists: $sourceDirectory"
}
$stage = Join-Path $outputParent ('.product-acceptance-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
$sourceCommitted = $false
try {
    $approvalEntries = @($validated | ForEach-Object {
        $name = $_.id + '.json'
        $target = Join-Path $stage $name
        Copy-Item -LiteralPath $_.path -Destination $target
        [ordered]@{
            validation_id = $_.id
            source_commit = $sourceCommit
            source_manifest = [ordered]@{
                file = "$sourceDirectoryName/$name"
                sha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    })
    $portableMatrix = [ordered]@{
        schema_version = 1
        classification = 'plugin_positive_matrix'
        source_commit = $sourceCommit
        source_dirty = $false
        plugin_count = 25
        command_count = 42
        approval_receipt_count = 25
        contract_valid = $true
        release_eligible = $true
    }
    $matrixName = 'plugin-positive-matrix.json'
    $matrixPath = Join-Path $stage $matrixName
    [IO.File]::WriteAllText(
        $matrixPath,
        ($portableMatrix | ConvertTo-Json -Depth 6),
        [Text.UTF8Encoding]::new($false))
    $summary = [ordered]@{
        schema_version = 1
        generated_at = [DateTimeOffset]::UtcNow.ToString('O')
        classification = 'approved_final_product_acceptance'
        passed = $true
        source_commit = $sourceCommit
        plugin_count = 25
        command_count = 42
        plugin_approval_receipt_count = 25
        required_validation_ids = $requiredIds
        approved_validation_count = $approvalEntries.Count
        plugin_matrix = [ordered]@{
            file = "$sourceDirectoryName/$matrixName"
            sha256 = (Get-FileHash -LiteralPath $matrixPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        approvals = $approvalEntries
    }
    [IO.Directory]::Move($stage, $sourceDirectory)
    $sourceCommitted = $true
    Write-NewJsonFileAtomically -Value $summary -Path $resolvedOutput -Depth 10 -Label 'Final product-acceptance summary'
}
catch {
    if ($sourceCommitted -and (Test-Path -LiteralPath $sourceDirectory)) {
        Remove-Item -LiteralPath $sourceDirectory -Recurse -Force
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
Write-Host "Final product-acceptance gate passed: $resolvedOutput"
