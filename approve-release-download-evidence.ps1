#!/usr/bin/env pwsh
<# .SYNOPSIS Approve interactive extraction, security-prompt, and first-launch observations. #>
param(
    [Parameter(Mandatory=$true)] [string] $EvidencePath,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)]
    [ValidateSet('unsigned','signed')] [string] $ExpectedDistributionChannel,
    [Parameter(Mandatory=$true)] [string] $Operator,
    [Parameter(Mandatory=$true)] [string] $Reviewer,
    [ValidateSet('independent','single_maintainer')]
    [string] $ReviewModel = 'independent',
    [string] $RiskAcceptedBy,
    [string] $RiskAcceptedAt,
    [string] $RiskReason,
    [string] $RiskAcceptedVersion,
    [Parameter(Mandatory=$true)] [string] $ExtractionMethod,
    [Parameter(Mandatory=$true)] [string] $SmartScreenObservation,
    [Parameter(Mandatory=$true)] [string] $AntivirusObservation,
    [Parameter(Mandatory=$true)] [string] $FirstLaunchObservation,
    [Parameter(Mandatory=$true)] [string] $ReviewNotes,
    [Parameter(Mandatory=$true)] [switch] $ConfirmExtractionCompleted,
    [Parameter(Mandatory=$true)] [switch] $ConfirmExtractedExecutableOriginChecked,
    [Parameter(Mandatory=$true)] [switch] $ConfirmSmartScreenObserved,
    [Parameter(Mandatory=$true)] [switch] $ConfirmAntivirusObserved,
    [Parameter(Mandatory=$true)] [switch] $ConfirmFirstLaunchObserved,
    [Parameter(Mandatory=$true)] [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-evidence-io.ps1')
. (Join-Path $PSScriptRoot 'release-review-policy.ps1')

function Assert-MeaningfulText([string] $value, [string] $name, [int] $minimumLength) {
    if ([string]::IsNullOrWhiteSpace($value) -or $value.Trim().Length -lt $minimumLength) {
        throw "$name must contain at least $minimumLength characters."
    }
}

Assert-MeaningfulText $Operator 'Operator' 2
Assert-MeaningfulText $Reviewer 'Reviewer' 2
Assert-MeaningfulText $ExtractionMethod 'ExtractionMethod' 4
Assert-MeaningfulText $SmartScreenObservation 'SmartScreenObservation' 8
Assert-MeaningfulText $AntivirusObservation 'AntivirusObservation' 8
Assert-MeaningfulText $FirstLaunchObservation 'FirstLaunchObservation' 8
Assert-MeaningfulText $ReviewNotes 'ReviewNotes' 12
if (@(
    $ConfirmExtractionCompleted,
    $ConfirmExtractedExecutableOriginChecked,
    $ConfirmSmartScreenObserved,
    $ConfirmAntivirusObserved,
    $ConfirmFirstLaunchObserved
) -contains $false) {
    throw 'Every interactive release-download confirmation is required.'
}

$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
$resolvedEvidencePath = [IO.Path]::GetFullPath($EvidencePath)
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $resolvedEvidencePath -PathType Leaf)) {
    throw "Download evidence was not found: $resolvedEvidencePath"
}
if (Test-Path -LiteralPath $resolvedOutputPath) {
    throw "Download approval output already exists: $resolvedOutputPath"
}

$evidenceHashBeforeReview = (Get-FileHash -LiteralPath $resolvedEvidencePath -Algorithm SHA256).
    Hash.ToLowerInvariant()
$evidence = Get-Content -LiteralPath $resolvedEvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($evidence.classification -ne 'verified_release_download_provenance' -or -not [bool]$evidence.passed) {
    throw 'Release-download evidence is not a passing provenance capture.'
}
if ([string]$evidence.release.source_commit -ne $expectedCommit) {
    throw 'Release-download evidence source commit does not match ExpectedSourceCommit.'
}
if ([string]$evidence.release.distribution_channel -ne $ExpectedDistributionChannel) {
    throw 'Release-download evidence distribution channel does not match ExpectedDistributionChannel.'
}
if ([int]$evidence.windows_origin.zone_id -ne 3 -or [bool]$evidence.windows_origin.query_parameters_recorded) {
    throw 'Release-download evidence does not contain an eligible sanitized Internet Zone origin.'
}
$reviewPolicy = Resolve-LongReleaseReviewPolicy `
    -ReviewModel $ReviewModel `
    -CandidateVersion ([string]$evidence.release.version) `
    -Operator $Operator `
    -Reviewer $Reviewer `
    -RiskAcceptedBy $RiskAcceptedBy `
    -RiskAcceptedAt $RiskAcceptedAt `
    -RiskReason $RiskReason `
    -RiskAcceptedVersion $RiskAcceptedVersion

$approval = [ordered]@{
    schema_version = 2
    approved_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'release_download_human_approval'
    source_commit = $expectedCommit
    distribution_channel = $ExpectedDistributionChannel
    package = [ordered]@{
        file = [string]$evidence.package.file
        sha256 = [string]$evidence.package.sha256
    }
    evidence = [ordered]@{
        file = [IO.Path]::GetFileName($resolvedEvidencePath)
        sha256 = $evidenceHashBeforeReview
    }
    version = [string]$evidence.release.version
    review_model = $reviewPolicy.review_model
    independent_review = $reviewPolicy.independent_review
    operator = $reviewPolicy.operator
    reviewer = $reviewPolicy.reviewer
    risk_acceptance = $reviewPolicy.risk_acceptance
    observations = [ordered]@{
        extraction_method = $ExtractionMethod.Trim()
        smartscreen = $SmartScreenObservation.Trim()
        antivirus = $AntivirusObservation.Trim()
        first_launch = $FirstLaunchObservation.Trim()
        review_notes = $ReviewNotes.Trim()
    }
    checklist = [ordered]@{
        extraction_completed = $true
        extracted_executable_origin_checked = $true
        smartscreen_observed = $true
        antivirus_observed = $true
        first_launch_observed = $true
    }
}

if ((Get-FileHash -LiteralPath $resolvedEvidencePath -Algorithm SHA256).
    Hash.ToLowerInvariant() -ne $evidenceHashBeforeReview) {
    throw 'Release-download evidence changed during human approval.'
}
Write-NewJsonFileAtomically `
    -Value $approval `
    -Path $resolvedOutputPath `
    -Depth 7 `
    -Label 'Download approval output'
Write-Output "Interactive release-download evidence approved by $($Reviewer.Trim())."
Write-Output "Approval: $resolvedOutputPath"
