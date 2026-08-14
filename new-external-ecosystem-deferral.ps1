#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)] [string] $CandidateVersion,
    [Parameter(Mandatory=$true)] [string] $TargetVersion,
    [Parameter(Mandatory=$true)] [string] $AcceptedBy,
    [Parameter(Mandatory=$true)] [string] $OutputPath,
    [switch] $ConfirmDeferred
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'release-evidence-io.ps1')
. (Join-Path $PSScriptRoot 'external-ecosystem-deferral-policy.ps1')

if (-not $ConfirmDeferred) {
    throw 'ConfirmDeferred is required to record external ecosystem deferral.'
}
$sourceCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit.'
}
& git -C $PSScriptRoot merge-base --is-ancestor $sourceCommit HEAD 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'ExpectedSourceCommit must be an ancestor of HEAD.'
}
$document = [ordered]@{
    schema_version = 1
    classification = 'external_ecosystem_deferral'
    status = 'deferred'
    source_commit = $sourceCommit
    candidate_version = $CandidateVersion
    target_version = $TargetVersion
    default_feature_state = 'disabled'
    accepted_by = $AcceptedBy
    accepted_at = [DateTimeOffset]::UtcNow.ToString('O')
    items = @(
        [ordered]@{ id = 'lpwp-long-grid-e2e'; status = 'deferred'; reason = 'missing_long_grid_repository'; target_version = $TargetVersion; default_feature_state = 'disabled' }
        [ordered]@{ id = 'lpwp-signed-reference'; status = 'deferred'; reason = 'missing_approved_plugin_publisher_identity'; target_version = $TargetVersion; default_feature_state = 'disabled' }
        [ordered]@{ id = 'production-marketplace-rehearsal'; status = 'deferred'; reason = 'missing_production_registry_or_cdn_credentials'; target_version = $TargetVersion; default_feature_state = 'disabled' }
    )
}
Resolve-LongExternalEcosystemDeferral -Document ([pscustomobject]$document) `
    -ExpectedSourceCommit $sourceCommit -ExpectedCandidateVersion $CandidateVersion | Out-Null
Write-NewJsonFileAtomically -Value $document -Path $OutputPath -Depth 8 `
    -Label 'External ecosystem deferral'
Write-Output "External ecosystem deferral recorded: $([IO.Path]::GetFullPath($OutputPath))"
