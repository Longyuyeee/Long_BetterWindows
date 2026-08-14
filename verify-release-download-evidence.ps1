#!/usr/bin/env pwsh
<# .SYNOPSIS Verify immutable release-download evidence and its independent human approval. #>
param(
    [Parameter(Mandatory=$true)] [string] $EvidencePath,
    [Parameter(Mandatory=$true)] [string] $ApprovalPath,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)]
    [ValidateSet('unsigned','signed')] [string] $ExpectedDistributionChannel,
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-evidence-io.ps1')
. (Join-Path $PSScriptRoot 'release-review-policy.ps1')
$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
$resolvedEvidencePath = [IO.Path]::GetFullPath($EvidencePath)
$resolvedApprovalPath = [IO.Path]::GetFullPath($ApprovalPath)
foreach ($path in @($resolvedEvidencePath, $resolvedApprovalPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Release-download gate file was not found: $path" }
}

$evidence = Get-Content -LiteralPath $resolvedEvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
$approval = Get-Content -LiteralPath $resolvedApprovalPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($evidence.classification -ne 'verified_release_download_provenance' -or -not [bool]$evidence.passed) {
    throw 'Release-download provenance capture is not passing.'
}
if ($approval.classification -ne 'release_download_human_approval') {
    throw 'Release-download human approval has an unexpected classification.'
}
if ([string]$evidence.release.source_commit -ne $expectedCommit -or
    [string]$approval.source_commit -ne $expectedCommit) {
    throw 'Release-download gate source commit does not match ExpectedSourceCommit.'
}
if ([string]$evidence.release.distribution_channel -ne $ExpectedDistributionChannel -or
    [string]$approval.distribution_channel -ne $ExpectedDistributionChannel) {
    throw 'Release-download gate distribution channel does not match ExpectedDistributionChannel.'
}
if (-not [bool]$evidence.release.release_eligible -or
    ($ExpectedDistributionChannel -eq 'signed' -and -not [bool]$evidence.release.signed) -or
    ($ExpectedDistributionChannel -eq 'unsigned' -and [bool]$evidence.release.signed)) {
    throw 'Release-download evidence does not match the expected eligible distribution channel.'
}
if ([int]$evidence.windows_origin.zone_id -ne 3 -or
    [string]$evidence.windows_origin.host.scheme -ne 'https' -or
    [string]::IsNullOrWhiteSpace([string]$evidence.windows_origin.host.host) -or
    [bool]$evidence.windows_origin.query_parameters_recorded) {
    throw 'Release-download evidence does not contain a sanitized HTTPS Internet Zone origin.'
}

$actualEvidenceHash = (Get-FileHash -LiteralPath $resolvedEvidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualEvidenceHash -ne [string]$approval.evidence.sha256) {
    throw 'Release-download evidence changed after human approval.'
}
if ([string]$approval.evidence.file -ne [IO.Path]::GetFileName($resolvedEvidencePath)) {
    throw 'Release-download approval references a different evidence file.'
}
if ([string]$approval.package.file -ne [string]$evidence.package.file -or
    [string]$approval.package.sha256 -ne [string]$evidence.package.sha256) {
    throw 'Release-download approval package identity does not match the captured evidence.'
}
$reviewModel = Get-LongReviewModel $approval
$riskAcceptance = $approval.risk_acceptance
$reviewPolicy = Resolve-LongReleaseReviewPolicy `
    -ReviewModel $reviewModel `
    -CandidateVersion ([string]$evidence.release.version) `
    -Operator ([string]$approval.operator) `
    -Reviewer ([string]$approval.reviewer) `
    -RiskAcceptedBy ([string]$riskAcceptance.risk_accepted_by) `
    -RiskAcceptedAt ([string]$riskAcceptance.risk_accepted_at) `
    -RiskReason ([string]$riskAcceptance.risk_reason) `
    -RiskAcceptedVersion ([string]$riskAcceptance.risk_accepted_version)
foreach ($property in $approval.checklist.psobject.Properties) {
    if (-not [bool]$property.Value) { throw "Interactive release-download checklist is incomplete: $($property.Name)" }
}
foreach ($property in $approval.observations.psobject.Properties) {
    if ([string]::IsNullOrWhiteSpace([string]$property.Value)) {
        throw "Interactive release-download observation is missing: $($property.Name)"
    }
}

$summary = [ordered]@{
    schema_version = 3
    verified_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'approved_release_download_gate'
    passed = $true
    source_commit = $expectedCommit
    distribution_channel = $ExpectedDistributionChannel
    version = [string]$evidence.release.version
    review_model = $reviewPolicy.review_model
    independent_review = $reviewPolicy.independent_review
    package_file = [string]$evidence.package.file
    package_sha256 = [string]$evidence.package.sha256
    download_host = [string]$evidence.windows_origin.host.host
    operator = $reviewPolicy.operator
    reviewer = $reviewPolicy.reviewer
    risk_acceptance = $reviewPolicy.risk_acceptance
    evidence = [ordered]@{
        file = [IO.Path]::GetFileName($resolvedEvidencePath)
        sha256 = $actualEvidenceHash
    }
    approval = [ordered]@{
        file = [IO.Path]::GetFileName($resolvedApprovalPath)
        sha256 = (Get-FileHash -LiteralPath $resolvedApprovalPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
    $outputParent = Split-Path -Parent $resolvedOutputPath
    foreach ($sourcePath in @($resolvedEvidencePath, $resolvedApprovalPath)) {
        if (-not [string]::Equals(
            [IO.Path]::GetFullPath((Split-Path -Parent $sourcePath)).TrimEnd('\'),
            [IO.Path]::GetFullPath($outputParent).TrimEnd('\'),
            [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Release-download summary and source files must share one directory.'
        }
    }
    Write-NewJsonFileAtomically `
        -Value $summary `
        -Path $resolvedOutputPath `
        -Depth 5 `
        -Label 'Release-download gate summary'
    Write-Output "Release-download gate summary: $resolvedOutputPath"
}
Write-Output 'Approved release-download gate verified.'
