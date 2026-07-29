param(
    [Parameter(Mandatory = $true)]
    [string]$PluginId,
    [Parameter(Mandatory = $true)]
    [string]$ManualCheckId,
    [Parameter(Mandatory = $true)]
    [string]$Reviewer,
    [Parameter(Mandatory = $true)]
    [string]$Notes,
    [Parameter(Mandatory = $true)]
    [string[]]$EvidenceFiles,
    [string]$MatrixPath = "docs/plugin-positive-function-matrix.json",
    [string]$SubjectExecutable =
        "src/LongBetterWindows.Host/bin/Release/net8.0-windows/LongBetterWindows.Host.exe",
    [switch]$ConfirmPassed,
    [switch]$Replace
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "release-evidence-io.ps1")

function Resolve-RepositoryPath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

if (-not $ConfirmPassed) {
    throw "Manual approval requires -ConfirmPassed after the reviewer inspects every evidence file."
}
if ([string]::IsNullOrWhiteSpace($Reviewer) `
    -or [string]::IsNullOrWhiteSpace($Notes)) {
    throw "Reviewer and Notes are required."
}
$trackedStatus = ((& git -C $PSScriptRoot status `
    --porcelain --untracked-files=no) -join "`n")
if (-not [string]::IsNullOrWhiteSpace($trackedStatus)) {
    throw "Manual approval requires a clean tracked worktree."
}
$sourceCommit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
$matrixFile = Resolve-RepositoryPath $MatrixPath
$matrix = Get-Content -LiteralPath $matrixFile -Raw -Encoding UTF8 |
    ConvertFrom-Json
$plugin = @($matrix.plugins | Where-Object {
    [string]$_.id -eq $PluginId
})
if ($plugin.Count -ne 1) {
    throw "Plugin manual matrix entry was not found or is ambiguous: $PluginId"
}
$manualCheck = @($plugin[0].manual_checks | Where-Object {
    [string]$_.id -eq $ManualCheckId
})
if ($manualCheck.Count -ne 1) {
    throw "Manual check was not found or is ambiguous: $PluginId/$ManualCheckId"
}

$manifestFile = Get-ChildItem -LiteralPath (
    Join-Path $PSScriptRoot "src") -Directory |
    ForEach-Object {
        $candidate = Join-Path $_.FullName "manifest.json"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $manifest = Get-Content -LiteralPath $candidate `
                -Raw -Encoding UTF8 | ConvertFrom-Json
            if ([string]$manifest.id -eq $PluginId) {
                Get-Item -LiteralPath $candidate
            }
        }
    }
if (@($manifestFile).Count -ne 1) {
    throw "Plugin source manifest was not found or is ambiguous: $PluginId"
}
$manifest = Get-Content -LiteralPath $manifestFile.FullName `
    -Raw -Encoding UTF8 | ConvertFrom-Json
$subjectPath = Resolve-RepositoryPath $SubjectExecutable
if (-not (Test-Path -LiteralPath $subjectPath -PathType Leaf)) {
    throw "Reviewed subject executable was not found: $subjectPath"
}

$qualityRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "artifacts\quality"))
$qualityPrefix = $qualityRoot + [IO.Path]::DirectorySeparatorChar
$evidence = @($EvidenceFiles | ForEach-Object {
    $path = Resolve-RepositoryPath $_
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Manual evidence file was not found: $path"
    }
    if (-not $path.StartsWith(
            $qualityPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Manual evidence must be stored under artifacts/quality: $path"
    }
    [ordered]@{
        relative_path = [IO.Path]::GetRelativePath($PSScriptRoot, $path).
            Replace("\", "/")
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).
            Hash.ToLowerInvariant()
        size_bytes = (Get-Item -LiteralPath $path).Length
    }
})
if ($evidence.Count -eq 0) {
    throw "At least one manual evidence file is required."
}

$safeName = ($PluginId + "--" + $ManualCheckId) `
    -replace "[^A-Za-z0-9._-]", "_"
$receiptDirectory = Join-Path $PSScriptRoot "docs\plugin-manual-approvals"
$receiptPath = Join-Path $receiptDirectory ($safeName + ".json")
$receiptExists = Test-Path -LiteralPath $receiptPath -PathType Leaf
if ($receiptExists -and -not $Replace) {
    throw "Approval receipt already exists. Use -Replace only after a new complete review: $receiptPath"
}
if ($Replace -and -not $receiptExists) {
    throw "Approval receipt does not exist. Omit -Replace for the first complete review: $receiptPath"
}
$existingReceiptHash = if ($Replace) {
    (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).
        Hash.ToLowerInvariant()
} else {
    $null
}
New-Item -ItemType Directory -Path $receiptDirectory -Force | Out-Null
$receipt = [ordered]@{
    schema_version = 1
    plugin_id = $PluginId
    manual_check_id = $ManualCheckId
    status = "passed"
    reviewer = $Reviewer.Trim()
    reviewed_at = [DateTimeOffset]::UtcNow.ToString("O")
    notes = $Notes.Trim()
    source_commit = $sourceCommit
    plugin_version = [string]$manifest.version
    manifest_sha256 = (
        Get-FileHash -LiteralPath $manifestFile.FullName -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    subject_executable = [IO.Path]::GetFileName($subjectPath)
    subject_executable_sha256 = (
        Get-FileHash -LiteralPath $subjectPath -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    commands = @($manualCheck[0].commands)
    evidence_files = $evidence
}
if ($Replace) {
    Update-JsonFileAtomically `
        -Value $receipt `
        -Path $receiptPath `
        -ExpectedSha256 $existingReceiptHash `
        -Depth 8 `
        -Label "Plugin manual approval receipt"
} else {
    Write-NewJsonFileAtomically `
        -Value $receipt `
        -Path $receiptPath `
        -Depth 8 `
        -Label "Plugin manual approval receipt"
}

Write-Host "Manual approval receipt created: $receiptPath"
Write-Host "Original evidence remains local under artifacts/quality."
Write-Host "Review and commit only the receipt; do not commit the original captures."
