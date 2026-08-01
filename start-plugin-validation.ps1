param(
    [string]$PluginId,
    [string]$EvidenceDirectory,
    [string]$CandidateDirectory,
    [switch]$PrepareOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "release-evidence-io.ps1")

function Resolve-RepositoryPath([string]$PathValue) {
    if ([IO.Path]::IsPathRooted($PathValue)) {
        return [IO.Path]::GetFullPath($PathValue)
    }
    return [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

function Get-RepositoryRelativePath([string]$FullPath) {
    $root = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $FullPath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the repository: $FullPath"
    }
    return $FullPath.Substring($root.Length).Replace("\", "/")
}

$plannerParameters = @{}
if (-not [string]::IsNullOrWhiteSpace($PluginId)) {
    $plannerParameters.PluginId = $PluginId
}
if (-not [string]::IsNullOrWhiteSpace($CandidateDirectory)) {
    $plannerParameters.CandidateDirectory = $CandidateDirectory
}
$plannerOutput = @(& (Join-Path $PSScriptRoot `
    "plan-next-plugin-validation.ps1") @plannerParameters)
$plan = ($plannerOutput -join [Environment]::NewLine) | ConvertFrom-Json
if ($null -eq $plan.next) {
    if ([bool]$plan.complete) {
        throw "All required plugin manual checks already have valid receipts."
    }
    throw "The selected plugin has no pending required manual check."
}

if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = [string]$plan.next.evidence_directory
}
$evidenceRoot = Resolve-RepositoryPath "artifacts/quality"
$evidencePath = Resolve-RepositoryPath $EvidenceDirectory
$qualityPrefix = $evidenceRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $evidencePath.StartsWith(
        $qualityPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Plugin validation evidence must be stored under artifacts/quality."
}
$subjectPath = Resolve-RepositoryPath ([string]$plan.next.subject_executable)
if (-not (Test-Path -LiteralPath $subjectPath -PathType Leaf)) {
    throw "Frozen candidate executable was not found: $subjectPath"
}
$actualSubjectHash = (Get-FileHash -LiteralPath $subjectPath `
    -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSubjectHash -ne [string]$plan.next.subject_executable_sha256) {
    throw "Frozen candidate executable changed after planning."
}

$sessionPath = Join-Path $evidencePath "validation-session.json"
$resumePreparedSession = $false
$existingSessionHash = $null
$preparedAt = [DateTimeOffset]::Now.ToString("o")
if (Test-Path -LiteralPath $evidencePath) {
    if ($PrepareOnly) {
        throw (
            "Evidence directory already exists. Preserve it and choose a new " +
            "-EvidenceDirectory for another attempt: $evidencePath")
    }
    if (-not (Test-Path -LiteralPath $sessionPath -PathType Leaf)) {
        throw "Existing evidence directory has no validation session: $evidencePath"
    }
    $existingSession = Get-Content -LiteralPath $sessionPath `
        -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([int]$existingSession.schema_version -ne 1 `
        -or [string]$existingSession.classification -ne `
            "plugin_manual_validation_session" `
        -or [string]$existingSession.candidate_commit -ne `
            [string]$plan.candidate_commit `
        -or [string]$existingSession.plugin_id -ne `
            [string]$plan.next.plugin_id `
        -or [string]$existingSession.manual_check_id -ne `
            [string]$plan.next.manual_check_id `
        -or [string]$existingSession.subject_executable_sha256 -ne `
            $actualSubjectHash `
        -or [string]$existingSession.launch_status -ne "prepared_only" `
        -or [string]$existingSession.review_status -ne `
            "pending_human_observation") {
        throw "Existing validation session cannot be resumed safely: $sessionPath"
    }
    $resumePreparedSession = $true
    $existingSessionHash = (Get-FileHash -LiteralPath $sessionPath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    $preparedAt = [string]$existingSession.prepared_at
} else {
    [IO.Directory]::CreateDirectory($evidencePath) | Out-Null
}

$runningHosts = @()
if (-not $PrepareOnly) {
    $runningHosts = @(Get-Process -Name "LongBetterWindows.Host" `
        -ErrorAction SilentlyContinue)
    if ($runningHosts.Count -gt 0) {
        throw (
            "Close every running Long Assistant host before starting a " +
            "frozen validation session. Running process IDs: " +
            (($runningHosts | ForEach-Object { $_.Id }) -join ", "))
    }
}

$session = [ordered]@{
    schema_version = 1
    classification = "plugin_manual_validation_session"
    prepared_at = $preparedAt
    candidate_version = [string]$plan.version
    candidate_commit = [string]$plan.candidate_commit
    plugin_id = [string]$plan.next.plugin_id
    manual_check_id = [string]$plan.next.manual_check_id
    risk = [string]$plan.next.risk
    runtime = [string]$plan.next.runtime
    commands = @($plan.next.commands | ForEach-Object { [string]$_ })
    description = [string]$plan.next.description
    evidence_directory = Get-RepositoryRelativePath $evidencePath
    subject_executable = Get-RepositoryRelativePath $subjectPath
    subject_executable_sha256 = $actualSubjectHash
    launch_arguments = @("--open-plugin", [string]$plan.next.plugin_id)
    launch_status = if ($PrepareOnly) { "prepared_only" } else { "launching" }
    launched_at = $null
    process_id = $null
    launch_error = $null
    review_status = "pending_human_observation"
    approval_command_template = [string]$plan.next.approval_command
}

if ($PrepareOnly) {
    Write-NewJsonFileAtomically `
        -Value $session `
        -Path $sessionPath `
        -Depth 8 `
        -Label "Plugin validation session"
} else {
    if ($resumePreparedSession) {
        Update-JsonFileAtomically `
            -Value $session `
            -Path $sessionPath `
            -ExpectedSha256 $existingSessionHash `
            -Depth 8 `
            -Label "Plugin validation session"
    } else {
        Write-NewJsonFileAtomically `
            -Value $session `
            -Path $sessionPath `
            -Depth 8 `
            -Label "Plugin validation session"
    }
    $launchingSessionHash = (Get-FileHash -LiteralPath $sessionPath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    try {
        $process = Start-Process `
            -FilePath $subjectPath `
            -ArgumentList @($session.launch_arguments) `
            -WorkingDirectory (Split-Path -Parent $subjectPath) `
            -PassThru
        $session.launch_status = "started"
        $session.launched_at = [DateTimeOffset]::Now.ToString("o")
        $session.process_id = $process.Id
    }
    catch {
        $session.launch_status = "failed"
        $session.launch_error = $_.Exception.Message
        Update-JsonFileAtomically `
            -Value $session `
            -Path $sessionPath `
            -ExpectedSha256 $launchingSessionHash `
            -Depth 8 `
            -Label "Plugin validation session"
        throw
    }
    Update-JsonFileAtomically `
        -Value $session `
        -Path $sessionPath `
        -ExpectedSha256 $launchingSessionHash `
        -Depth 8 `
        -Label "Plugin validation session"
}
$json = $session | ConvertTo-Json -Depth 8
Write-Host "Plugin validation session: $sessionPath"
if ($PrepareOnly) {
    Write-Host "Candidate launch skipped because -PrepareOnly was supplied."
} else {
    Write-Host "Frozen candidate started. Human observation is still required."
}
$json
