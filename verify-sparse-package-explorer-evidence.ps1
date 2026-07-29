#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $EvidenceDirectory,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)] [string] $ExpectedCertificateThumbprint,
    [Parameter(Mandatory=$true)] [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-evidence-io.ps1')
$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
$expectedThumbprint = $ExpectedCertificateThumbprint.Replace(' ', '').ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
if ($expectedThumbprint -notmatch '^[0-9a-f]{40,128}$') {
    throw 'ExpectedCertificateThumbprint is invalid.'
}
$root = [IO.Path]::GetFullPath($EvidenceDirectory)
$evidencePath = Join-Path $root 'sparse-package-explorer-evidence.json'
$summaryPath = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $summaryPath) {
    throw "Sparse Package Explorer verification output already exists: $summaryPath"
}
if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) {
    throw "Sparse Package Explorer evidence was not found: $evidencePath"
}
$evidence = Get-Content -LiteralPath $evidencePath -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ($evidence.classification -ne 'sparse_package_explorer_evidence' -or
    $evidence.human_review.status -ne 'approved') {
    throw 'Sparse Package Explorer evidence is not independently approved.'
}
if ([string]$evidence.candidate.source_commit -ne $expectedCommit -or
    [string]$evidence.candidate.certificate_thumbprint -ne $expectedThumbprint) {
    throw 'Sparse Package Explorer evidence candidate does not match verification inputs.'
}
if (-not [bool]$evidence.automated_checks.passed -or
    -not [bool]$evidence.automated_checks.signed_package_valid -or
    -not [bool]$evidence.automated_checks.clean_build_chain_valid -or
    -not [bool]$evidence.automated_checks.package_removed_after_capture -or
    -not [bool]$evidence.automated_checks.legacy_menu_state_unchanged) {
    throw 'Sparse Package Explorer automated checks are not all passing.'
}
foreach ($property in $evidence.human_review.checklist.psobject.Properties) {
    if (-not [bool]$property.Value) {
        throw "Sparse Package Explorer review item is not approved: $($property.Name)"
    }
}
foreach ($entry in @($evidence.files)) {
    $path = Join-Path $root ([string]$entry.file)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne
            [string]$entry.sha256) {
        throw "Sparse Package Explorer evidence file is missing or changed: $($entry.file)"
    }
}

$summary = [ordered]@{
    schema_version = 1
    generated_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'sparse_package_explorer_verification'
    source_commit = $expectedCommit
    certificate_thumbprint = $expectedThumbprint
    environment_label = [string]$evidence.environment.label
    operator = [string]$evidence.environment.user
    reviewer = [string]$evidence.human_review.reviewer
    evidence = [ordered]@{
        file = [IO.Path]::GetFileName($evidencePath)
        sha256 = (Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    package_removed_after_capture = $true
    legacy_menu_state_unchanged = $true
    passed = $true
}
Write-NewJsonFileAtomically `
    -Value $summary `
    -Path $summaryPath `
    -Depth 5 `
    -Label 'Sparse Package Explorer verification output'
Write-Output 'Sparse Package Explorer evidence verification passed.'
Write-Output "Summary: $summaryPath"
