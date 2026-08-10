#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet(
        'taskbar-visual-grouping',
        'native-performance',
        'lpwp-long-grid-e2e',
        'lpwp-widget-desktop',
        'lpwp-signed-reference')]
    [string] $ValidationId,
    [Parameter(Mandatory=$true)] [string] $Reviewer,
    [Parameter(Mandatory=$true)] [string] $Notes,
    [Parameter(Mandatory=$true)] [string[]] $EvidenceFiles,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [string] $SubjectExecutable =
        'src/LongBetterWindows.Host/bin/Release/net8.0-windows/LongBetterWindows.Host.exe',
    [string] $ExpectedPublisherKeyId,
    [string] $ExpectedPublicKeyFingerprint,
    [switch] $ConfirmPassed,
    [switch] $Replace
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

function Get-RepositoryRelativePath([string] $FullPath) {
    $prefix = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $FullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the repository: $FullPath"
    }
    return $FullPath.Substring($prefix.Length).Replace('\', '/')
}

if (-not $ConfirmPassed) {
    throw 'Final validation approval requires -ConfirmPassed after reviewing every evidence file.'
}
if ([string]::IsNullOrWhiteSpace($Reviewer) -or [string]::IsNullOrWhiteSpace($Notes)) {
    throw 'Reviewer and Notes are required.'
}
$trackedStatus = @(& git -C $PSScriptRoot status --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0 -or $trackedStatus.Count -ne 0) {
    throw 'Final validation approval requires a clean tracked worktree.'
}
$sourceCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit.'
}
& git -C $PSScriptRoot merge-base --is-ancestor $sourceCommit HEAD 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'ExpectedSourceCommit must be an ancestor of HEAD.'
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
$subjectPath = Resolve-RepositoryPath $SubjectExecutable
if (-not (Test-Path -LiteralPath $subjectPath -PathType Leaf)) {
    throw "Reviewed subject executable was not found: $subjectPath"
}

$qualityRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts\quality'))
$qualityPrefix = $qualityRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
$resolvedEvidence = @($EvidenceFiles | ForEach-Object {
    $path = Resolve-RepositoryPath $_
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Final validation evidence file was not found: $path"
    }
    if (-not $path.StartsWith($qualityPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Final validation evidence must be stored under artifacts/quality: $path"
    }
    [ordered]@{
        relative_path = Get-RepositoryRelativePath $path
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        size_bytes = (Get-Item -LiteralPath $path).Length
    }
})
if ($resolvedEvidence.Count -eq 0) { throw 'At least one evidence file is required.' }

$contract = [ordered]@{}
if ($ValidationId -eq 'native-performance') {
    $manifestPath = @($EvidenceFiles | ForEach-Object { Resolve-RepositoryPath $_ } |
        Where-Object { [IO.Path]::GetFileName($_) -eq 'native-performance-evidence.json' })
    if ($manifestPath.Count -ne 1) {
        throw 'Native performance approval requires native-performance-evidence.json.'
    }
    $analysisPath = @($EvidenceFiles | ForEach-Object { Resolve-RepositoryPath $_ } |
        Where-Object { [IO.Path]::GetFileName($_) -eq 'native-performance-analysis.json' })
    if ($analysisPath.Count -ne 1) {
        throw 'Native performance approval requires native-performance-analysis.json.'
    }
    $evidenceRoot = Split-Path -Parent $manifestPath[0]
    if ((Split-Path -Parent $analysisPath[0]) -ne $evidenceRoot) {
        throw 'Native performance capture and analysis reports must share one evidence directory.'
    }
    $analysis = Get-Content -LiteralPath $analysisPath[0] -Raw -Encoding UTF8 |
        ConvertFrom-Json
    $exportManifestPath = [IO.Path]::GetFullPath((
        Join-Path $evidenceRoot ([string]$analysis.export_manifest_file)))
    & (Join-Path $PSScriptRoot 'verify-native-performance-analysis.ps1') `
        -EvidenceDirectory $evidenceRoot `
        -ExportDirectory (Split-Path -Parent $exportManifestPath) `
        -ExpectedCommit $sourceCommit
    if ([string]$analysis.reviewer -eq $Reviewer.Trim()) {
        throw 'Native performance final approver must differ from the WPA analyst.'
    }
    $requiredAnalysisPaths = @(
        @($analysis.evidence_files | ForEach-Object {
            Join-Path $evidenceRoot ([string]$_.relative_path)
        })
        $exportManifestPath
    )
    foreach ($path in $requiredAnalysisPaths) {
        $fullPath = [IO.Path]::GetFullPath($path)
        $relativePath = Get-RepositoryRelativePath $fullPath
        $match = @($resolvedEvidence | Where-Object {
            $_.relative_path -eq $relativePath
        })
        if ($match.Count -ne 1) {
            throw 'Native performance approval must include every hash-locked WPA analysis file.'
        }
    }
    $contract.native_performance_manifest_sha256 =
        (Get-FileHash -LiteralPath $manifestPath[0] -Algorithm SHA256).Hash.ToLowerInvariant()
    $contract.native_performance_analysis_sha256 =
        (Get-FileHash -LiteralPath $analysisPath[0] -Algorithm SHA256).Hash.ToLowerInvariant()
    $contract.native_performance_export_sha256 =
        ([string]$analysis.export_manifest_sha256).ToLowerInvariant()
    $contract.native_performance_analyst = ([string]$analysis.reviewer).Trim()
}
elseif ($ValidationId -eq 'lpwp-long-grid-e2e') {
    $documents = @($EvidenceFiles | ForEach-Object {
        try {
            Get-Content -LiteralPath (Resolve-RepositoryPath $_) -Raw -Encoding UTF8 | ConvertFrom-Json
        } catch { $null }
    } | Where-Object { $null -ne $_ -and $_.classification -eq 'lpwp_long_grid_e2e' })
    if ($documents.Count -ne 1) { throw 'Long Grid E2E approval requires one lpwp_long_grid_e2e document.' }
    $document = $documents[0]
    $requiredMethods = @(
        'host.hello', 'plugin.catalog.list', 'plugin.catalog.get',
        'command.invoke', 'command.cancel', 'plugin.open')
    $actualMethods = @($document.verified_methods | ForEach-Object { [string]$_ } | Sort-Object -Unique)
    if (-not [bool]$document.passed `
        -or [int]$document.schema_version -ne 1 `
        -or [string]::IsNullOrWhiteSpace([string]$document.operator) `
        -or [string]$document.operator -eq $Reviewer.Trim() `
        -or [string]$document.source_commit -ne $sourceCommit `
        -or [string]$document.protocol -ne 'long.plugin.ipc/1.0' `
        -or [string]$document.long_grid_commit -notmatch '^[0-9a-fA-F]{40}$' `
        -or $actualMethods.Count -ne $requiredMethods.Count `
        -or (Compare-Object ($requiredMethods | Sort-Object) $actualMethods).Count -ne 0) {
        throw 'Long Grid E2E evidence contract is incomplete.'
    }
    $reportEvidence = @($document.evidence_files)
    if ($reportEvidence.Count -eq 0) {
        throw 'Long Grid E2E report has no hash-locked raw evidence.'
    }
    foreach ($item in $reportEvidence) {
        $match = @($resolvedEvidence | Where-Object {
            $_.relative_path -eq [string]$item.relative_path `
                -and $_.sha256 -eq [string]$item.sha256 `
                -and $_.size_bytes -eq [long]$item.size_bytes
        })
        if ($match.Count -ne 1) {
            throw 'Long Grid E2E raw evidence is missing or does not match its report.'
        }
    }
    $contract.long_grid_commit = ([string]$document.long_grid_commit).ToLowerInvariant()
    $contract.protocol = 'long.plugin.ipc/1.0'
    $contract.operator = ([string]$document.operator).Trim()
}
elseif ($ValidationId -eq 'lpwp-signed-reference') {
    if ([string]::IsNullOrWhiteSpace($ExpectedPublisherKeyId) `
        -or $ExpectedPublicKeyFingerprint -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'Signed reference approval requires the independently recorded Key ID and fingerprint.'
    }
    $documents = @($EvidenceFiles | ForEach-Object {
        try {
            Get-Content -LiteralPath (Resolve-RepositoryPath $_) -Raw -Encoding UTF8 | ConvertFrom-Json
        } catch { $null }
    } | Where-Object { $null -ne $_ -and $_.classification -eq 'lpwp_signed_reference' })
    if ($documents.Count -ne 1) { throw 'Signed reference approval requires one lpwp_signed_reference report.' }
    $document = $documents[0]
    if (-not [bool]$document.passed `
        -or -not [bool]$document.signature_verified `
        -or [string]$document.source_commit -ne $sourceCommit `
        -or [string]$document.publisher_key_id -ne $ExpectedPublisherKeyId `
        -or [string]$document.public_key_fingerprint -ne $ExpectedPublicKeyFingerprint.ToUpperInvariant() `
        -or [string]$document.package_sha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'Signed reference evidence does not match the independently recorded publisher identity.'
    }
    $contract.publisher_key_id = $ExpectedPublisherKeyId
    $contract.public_key_fingerprint = $ExpectedPublicKeyFingerprint.ToUpperInvariant()
    $contract.package_sha256 = ([string]$document.package_sha256).ToLowerInvariant()
}

$receiptDirectory = Join-Path $PSScriptRoot 'docs\final-validation-approvals'
$receiptPath = Join-Path $receiptDirectory ($ValidationId + '.json')
$exists = Test-Path -LiteralPath $receiptPath -PathType Leaf
if ($exists -and -not $Replace) {
    throw "Approval receipt already exists. Use -Replace after a new complete review: $receiptPath"
}
if ($Replace -and -not $exists) {
    throw "Approval receipt does not exist. Omit -Replace for the first review: $receiptPath"
}
$existingHash = if ($Replace) {
    (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash.ToLowerInvariant()
} else { $null }
$receipt = [ordered]@{
    schema_version = 1
    classification = 'final_validation_approval'
    validation_id = $ValidationId
    status = 'passed'
    reviewer = $Reviewer.Trim()
    reviewed_at = [DateTimeOffset]::UtcNow.ToString('O')
    notes = $Notes.Trim()
    source_commit = $sourceCommit
    subject_executable = [IO.Path]::GetFileName($subjectPath)
    subject_executable_sha256 =
        (Get-FileHash -LiteralPath $subjectPath -Algorithm SHA256).Hash.ToLowerInvariant()
    verified_contract = $contract
    evidence_files = $resolvedEvidence
}
if ($Replace) {
    Update-JsonFileAtomically -Value $receipt -Path $receiptPath -ExpectedSha256 $existingHash -Depth 10 -Label 'Final validation approval receipt'
} else {
    Write-NewJsonFileAtomically -Value $receipt -Path $receiptPath -Depth 10 -Label 'Final validation approval receipt'
}
Write-Host "Final validation approval receipt created: $receiptPath"
Write-Host 'Original evidence remains local under artifacts/quality.'
