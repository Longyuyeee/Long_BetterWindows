#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $FinalClosureReportPath,
    [string] $SubjectExecutable =
        'src/LongBetterWindows.Host/bin/Release/net8.0-windows/LongBetterWindows.Host.exe',
    [Parameter(Mandatory=$true)] [string] $OutputPath,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [switch] $AllowDirty,
    [switch] $RequireReleaseEligible
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'release-evidence-io.ps1')
. (Join-Path $PSScriptRoot 'automated-acceptance-policy.ps1')

function Resolve-RepositoryPath([string] $PathValue) {
    if ([IO.Path]::IsPathRooted($PathValue)) {
        return [IO.Path]::GetFullPath($PathValue)
    }
    return [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

function Assert-CountConsistency($Acceptance) {
    $total = [int]$Acceptance.automated_gate_count
    $classified = [int]$Acceptance.passed_gate_count +
        [int]$Acceptance.failed_gate_count +
        [int]$Acceptance.environment_blocked_gate_count +
        [int]$Acceptance.not_run_gate_count +
        [int]$Acceptance.not_applicable_gate_count
    if ($total -ne 94 -or $classified -ne $total `
        -or @($Acceptance.gates).Count -ne $total) {
        throw 'Final closure automated gate counts are incomplete or inconsistent.'
    }
}

function Assert-GateContract($Acceptance) {
    $allowedStatuses = @(
        'not_run',
        'passed',
        'failed',
        'blocked_environment',
        'not_applicable')
    $gateIds = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($gate in @($Acceptance.gates)) {
        $id = [string]$gate.id
        $status = [string]$gate.status
        if ($id -notmatch '^[a-z0-9][a-z0-9._-]{0,191}$' `
            -or -not $gateIds.Add($id) `
            -or $status -notin $allowedStatuses `
            -or [string]::IsNullOrWhiteSpace([string]$gate.summary)) {
            throw 'Final closure automated gate identity is invalid.'
        }
        $evidenceIds = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        $evidence = @($gate.evidence)
        if ($status -in @('passed','failed') -and $evidence.Count -eq 0) {
            throw "Final closure gate has no hashed evidence: $id"
        }
        foreach ($item in $evidence) {
            if ([string]$item.id -notmatch '^[a-z0-9][a-z0-9._-]{0,127}$' `
                -or -not $evidenceIds.Add([string]$item.id) `
                -or [string]::IsNullOrWhiteSpace([string]$item.path) `
                -or [string]$item.sha256 -notmatch '^[0-9a-f]{64}$') {
                throw "Final closure gate evidence is invalid: $id"
            }
        }
        if ($status -eq 'blocked_environment' `
            -and [string]::IsNullOrWhiteSpace([string]$gate.environment_blocker)) {
            throw "Final closure environment blocker is missing: $id"
        }
        if ($status -eq 'not_applicable' `
            -and [string]::IsNullOrWhiteSpace([string]$gate.not_applicable_reason)) {
            throw "Final closure not-applicable reason is missing: $id"
        }
    }
}

$currentHead = (& git -C $PSScriptRoot rev-parse HEAD).Trim().ToLowerInvariant()
$sourceCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($sourceCommit -notmatch '^[0-9a-f]{40}$' -or $sourceCommit -ne $currentHead) {
    throw 'ExpectedSourceCommit must exactly match the current 40-character HEAD.'
}
$trackedStatus = @(& git -C $PSScriptRoot status --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect the tracked worktree.'
}
$sourceDirty = $trackedStatus.Count -ne 0
if ($sourceDirty -and -not $AllowDirty) {
    throw 'Final product acceptance requires a clean tracked worktree.'
}

$closurePath = Resolve-RepositoryPath $FinalClosureReportPath
if (-not (Test-Path -LiteralPath $closurePath -PathType Leaf)) {
    throw "Final closure report was not found: $closurePath"
}
$closureJson = Get-Content -LiteralPath $closurePath -Raw -Encoding UTF8
try {
    $closure = $closureJson | ConvertFrom-Json
}
catch {
    throw 'Final closure report is not valid JSON.'
}
$acceptance = $closure.automated_acceptance
if ([int]$closure.schema_version -ne 2 `
    -or [string]$closure.classification -ne 'final_closure' `
    -or [string]$closure.source_commit -ne $sourceCommit `
    -or [bool]$closure.source_dirty -ne $sourceDirty `
    -or $null -eq $acceptance `
    -or -not [bool]$acceptance.contract_valid `
    -or @($acceptance.errors).Count -ne 0) {
    throw 'Final closure report identity or contract is invalid.'
}
Assert-CountConsistency $acceptance
Assert-GateContract $acceptance
if ([bool]$closure.checks_skipped) {
    throw 'Final closure contains automated gates that were not run.'
}

$matrix = $closure.plugin_matrix
if ($null -eq $matrix `
    -or [int]$matrix.schema_version -ne 2 `
    -or [string]$matrix.source_commit -ne $sourceCommit `
    -or [bool]$matrix.source_dirty -ne $sourceDirty `
    -or [int]$matrix.plugin_count -ne 25 `
    -or [int]$matrix.command_count -ne 42 `
    -or [int]$matrix.acceptance_scenario_count -ne 25 `
    -or [int]$matrix.automated_gate_count -ne 87 `
    -or [int]$matrix.failed_gate_count -ne 0 `
    -or [int]$matrix.not_run_gate_count -ne 0 `
    -or -not [bool]$matrix.contract_valid `
    -or [string]$matrix.report_sha256 -notmatch '^[0-9a-f]{64}$') {
    throw 'Plugin matrix summary in final closure is incomplete or invalid.'
}
if ([int]$acceptance.failed_gate_count -ne 0) {
    throw 'Final closure contains failed automated gates.'
}
if ([int]$acceptance.not_run_gate_count -ne 0) {
    throw 'Final closure contains automated gates that were not run.'
}

$hostPath = Resolve-RepositoryPath $SubjectExecutable
if (-not (Test-Path -LiteralPath $hostPath -PathType Leaf)) {
    throw "Release host executable was not found: $hostPath"
}
$hostHash = (Get-FileHash -LiteralPath $hostPath -Algorithm SHA256).
    Hash.ToLowerInvariant()
if (-not [bool]$closure.release_host.exists `
    -or [string]$closure.release_host.sha256 -ne $hostHash) {
    throw 'Final closure Release host identity does not match the current executable.'
}

$calculatedEligibility = Get-AutomatedReleaseEligibility `
    -AutomatedGateCount ([int]$acceptance.automated_gate_count) `
    -PassedGateCount ([int]$acceptance.passed_gate_count) `
    -FailedGateCount ([int]$acceptance.failed_gate_count) `
    -EnvironmentBlockedGateCount ([int]$acceptance.environment_blocked_gate_count) `
    -NotRunGateCount ([int]$acceptance.not_run_gate_count) `
    -NotApplicableGateCount ([int]$acceptance.not_applicable_gate_count) `
    -ContractValid ([bool]$acceptance.contract_valid) `
    -SourceDirty $sourceDirty
if ([bool]$closure.release_eligible -ne [bool]$calculatedEligibility) {
    throw 'Final closure release eligibility does not match its automated gate counts.'
}

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
$stage = Join-Path $outputParent (
    '.product-acceptance-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
$sourceCommitted = $false
try {
    $closureName = 'final-closure.json'
    $portableClosure = Join-Path $stage $closureName
    Copy-Item -LiteralPath $closurePath -Destination $portableClosure
    $closureHash = (Get-FileHash -LiteralPath $portableClosure -Algorithm SHA256).
        Hash.ToLowerInvariant()
    $blockers = @($acceptance.gates | Where-Object {
        [string]$_.status -eq 'blocked_environment'
    } | ForEach-Object {
        [ordered]@{
            gate_id = [string]$_.id
            reason = [string]$_.environment_blocker
        }
    })
    $summary = [ordered]@{
        '$schema' = 'https://long-assistant.local/schemas/final-product-acceptance-report.schema.json'
        schema_version = 3
        generated_at_utc = [DateTimeOffset]::UtcNow.ToString('O')
        classification = 'automated_final_product_acceptance'
        source_commit = $sourceCommit
        source_dirty = $sourceDirty
        acceptance_status = if ($calculatedEligibility) {
            'passed'
        } elseif ($blockers.Count -gt 0) {
            'blocked_environment'
        } else {
            'not_eligible'
        }
        plugin_count = [int]$matrix.plugin_count
        command_count = [int]$matrix.command_count
        automated_gate_count = [int]$acceptance.automated_gate_count
        passed_gate_count = [int]$acceptance.passed_gate_count
        failed_gate_count = [int]$acceptance.failed_gate_count
        environment_blocked_gate_count =
            [int]$acceptance.environment_blocked_gate_count
        not_run_gate_count = [int]$acceptance.not_run_gate_count
        not_applicable_gate_count = [int]$acceptance.not_applicable_gate_count
        contract_valid = $true
        release_eligible = [bool]$calculatedEligibility
        release_host = [ordered]@{
            path = $hostPath
            sha256 = $hostHash
        }
        final_closure = [ordered]@{
            file = "$sourceDirectoryName/$closureName"
            sha256 = $closureHash
        }
        environment_blockers = $blockers
    }
    [IO.Directory]::Move($stage, $sourceDirectory)
    $sourceCommitted = $true
    Write-NewJsonFileAtomically `
        -Value $summary `
        -Path $resolvedOutput `
        -Depth 10 `
        -Label 'Final product-acceptance summary'
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

$summary | ConvertTo-Json -Depth 10
if ($RequireReleaseEligible -and -not $calculatedEligibility) {
    exit 2
}
