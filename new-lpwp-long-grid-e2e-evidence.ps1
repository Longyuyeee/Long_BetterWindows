#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)] [string] $LongGridCommit,
    [Parameter(Mandatory=$true)] [string] $Operator,
    [Parameter(Mandatory=$true)] [string] $Notes,
    [Parameter(Mandatory=$true)] [string[]] $EvidenceFiles,
    [Parameter(Mandatory=$true)] [string] $OutputPath,
    [switch] $ConfirmHostHello,
    [switch] $ConfirmCatalogList,
    [switch] $ConfirmCatalogGet,
    [switch] $ConfirmCommandInvoke,
    [switch] $ConfirmCommandCancel,
    [switch] $ConfirmPluginOpen
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

$sourceCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
$gridCommit = $LongGridCommit.Trim().ToLowerInvariant()
if ($sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit.'
}
if ($gridCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'LongGridCommit must be a full 40-character Git commit.'
}
if ([string]::IsNullOrWhiteSpace($Operator) -or [string]::IsNullOrWhiteSpace($Notes)) {
    throw 'Operator and Notes are required.'
}
$trackedStatus = @(& git -C $PSScriptRoot status --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0 -or $trackedStatus.Count -ne 0) {
    throw 'LPWP E2E evidence generation requires a clean tracked worktree.'
}
& git -C $PSScriptRoot merge-base --is-ancestor $sourceCommit HEAD 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'ExpectedSourceCommit must be an ancestor of HEAD.'
}
$postCandidateChanges = @(& git -C $PSScriptRoot diff --name-only $sourceCommit HEAD --)
$unexpectedChanges = @($postCandidateChanges | Where-Object {
    $_ -notmatch '^docs/(plugin-manual-approvals|final-validation-approvals)/[^/]+\.json$'
})
if ($LASTEXITCODE -ne 0 -or $unexpectedChanges.Count -ne 0) {
    throw 'Product files changed after ExpectedSourceCommit.'
}

$confirmations = [ordered]@{
    'host.hello' = [bool]$ConfirmHostHello
    'plugin.catalog.list' = [bool]$ConfirmCatalogList
    'plugin.catalog.get' = [bool]$ConfirmCatalogGet
    'command.invoke' = [bool]$ConfirmCommandInvoke
    'command.cancel' = [bool]$ConfirmCommandCancel
    'plugin.open' = [bool]$ConfirmPluginOpen
}
$missing = @($confirmations.Keys | Where-Object { -not $confirmations[$_] })
if ($missing.Count -ne 0) {
    throw ('Every LPWP core method must be explicitly confirmed: ' + ($missing -join ', '))
}

$qualityRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts\quality'))
$qualityPrefix = $qualityRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
$output = Resolve-RepositoryPath $OutputPath
if (-not $output.StartsWith($qualityPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'LPWP E2E summary must be written under artifacts/quality.'
}
$evidence = @($EvidenceFiles | ForEach-Object {
    $path = Resolve-RepositoryPath $_
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "LPWP E2E evidence file was not found: $path"
    }
    if (-not $path.StartsWith($qualityPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "LPWP E2E evidence must be stored under artifacts/quality: $path"
    }
    if ($path.Equals($output, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputPath cannot also be an input evidence file.'
    }
    [ordered]@{
        relative_path = Get-RepositoryRelativePath $path
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        size_bytes = (Get-Item -LiteralPath $path).Length
    }
})
if ($evidence.Count -eq 0) { throw 'At least one raw Long Grid E2E evidence file is required.' }

$summary = [ordered]@{
    schema_version = 1
    classification = 'lpwp_long_grid_e2e'
    passed = $true
    tested_at = [DateTimeOffset]::UtcNow.ToString('O')
    operator = $Operator.Trim()
    notes = $Notes.Trim()
    source_commit = $sourceCommit
    long_grid_commit = $gridCommit
    protocol = 'long.plugin.ipc/1.0'
    verified_methods = @($confirmations.Keys)
    evidence_files = $evidence
}
Write-NewJsonFileAtomically -Value $summary -Path $output -Depth 8 -Label 'LPWP Long Grid E2E evidence'
Write-Host "LPWP Long Grid E2E evidence created: $output"
