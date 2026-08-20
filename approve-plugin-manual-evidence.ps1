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
    [Parameter(Mandatory = $true)]
    [string]$CandidateDirectory,
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

function Get-RepositoryRelativePath([string]$FullPath) {
    $prefix = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $FullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the repository: $FullPath"
    }
    return $FullPath.Substring($prefix.Length).Replace("\", "/")
}

function Get-StreamSha256([System.IO.Stream]$Stream) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $algorithm.ComputeHash($Stream))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
    }
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
$manualCheck = @($plugin[0].acceptance_scenarios | Where-Object {
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
$candidateRoot = Resolve-RepositoryPath $CandidateDirectory
$releaseRoot = Resolve-RepositoryPath "artifacts/releases"
$releasePrefix = $releaseRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $candidateRoot.StartsWith(
        $releasePrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Plugin approval candidate must be stored under artifacts/releases."
}
$candidatePrefix = $candidateRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $subjectPath.StartsWith(
        $candidatePrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Reviewed subject executable must belong to the frozen candidate."
}
$releaseManifestPath = Join-Path $candidateRoot "release-manifest.json"
if (-not (Test-Path -LiteralPath $releaseManifestPath -PathType Leaf)) {
    throw "Candidate release manifest was not found: $releaseManifestPath"
}
$releaseManifest = Get-Content -LiteralPath $releaseManifestPath `
    -Raw -Encoding UTF8 | ConvertFrom-Json
$candidateCommit = [string]$releaseManifest.commit
if ([int]$releaseManifest.schema_version -ne 1 `
    -or $candidateCommit -notmatch "^[a-fA-F0-9]{40}$" `
    -or [bool]$releaseManifest.source_dirty `
    -or -not [bool]$releaseManifest.release_eligible) {
    throw "Candidate release manifest is not an eligible clean candidate."
}
& git -C $PSScriptRoot merge-base --is-ancestor $candidateCommit HEAD 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "Candidate commit is not an ancestor of HEAD: $candidateCommit"
}
$unexpectedChanges = @(& git -C $PSScriptRoot diff --name-only `
    $candidateCommit HEAD 2>$null | Where-Object {
        $_ -notmatch '^docs/plugin-manual-approvals/[^/]+\.json$'
    })
if ($LASTEXITCODE -ne 0 -or $unexpectedChanges.Count -gt 0) {
    throw "Candidate is stale because tracked product files changed after it."
}
$selfContainedPackages = @($releaseManifest.packages | Where-Object {
    [string]$_.kind -eq "self-contained"
})
if ($selfContainedPackages.Count -ne 1) {
    throw "Candidate manifest must contain exactly one self-contained package."
}
$packageEntry = $selfContainedPackages[0]
$packagePath = [IO.Path]::GetFullPath((Join-Path `
    $candidateRoot ([string]$packageEntry.file)))
if (-not $packagePath.StartsWith(
        $candidatePrefix,
        [StringComparison]::OrdinalIgnoreCase) `
    -or -not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
    throw "Candidate self-contained package is missing or outside the candidate."
}
$packageHash = (Get-FileHash -LiteralPath $packagePath `
    -Algorithm SHA256).Hash.ToLowerInvariant()
if ($packageHash -ne [string]$packageEntry.sha256) {
    throw "Candidate self-contained package SHA-256 does not match its manifest."
}
$subjectHash = (Get-FileHash -LiteralPath $subjectPath `
    -Algorithm SHA256).Hash.ToLowerInvariant()
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $subjectEntries = @($archive.Entries | Where-Object {
        $_.FullName.Replace("\", "/") -match `
            '(^|/)LongBetterWindows\.Host\.exe$'
    })
    if ($subjectEntries.Count -ne 1) {
        throw "Candidate ZIP must contain exactly one LongBetterWindows.Host.exe."
    }
    $entryStream = $subjectEntries[0].Open()
    try {
        $archivedSubjectHash = Get-StreamSha256 $entryStream
    }
    finally {
        $entryStream.Dispose()
    }
}
finally {
    $archive.Dispose()
}
if ($archivedSubjectHash -ne $subjectHash) {
    throw "Reviewed subject executable does not match the verified candidate ZIP."
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
        relative_path = Get-RepositoryRelativePath $path
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
    schema_version = 2
    plugin_id = $PluginId
    manual_check_id = $ManualCheckId
    status = "passed"
    reviewer = $Reviewer.Trim()
    reviewed_at = [DateTimeOffset]::UtcNow.ToString("O")
    notes = $Notes.Trim()
    source_commit = $sourceCommit
    candidate_version = [string]$releaseManifest.version
    candidate_commit = $candidateCommit.ToLowerInvariant()
    candidate_directory = Get-RepositoryRelativePath $candidateRoot
    release_manifest_sha256 = (Get-FileHash -LiteralPath `
        $releaseManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    self_contained_package = Get-RepositoryRelativePath $packagePath
    self_contained_package_sha256 = $packageHash
    plugin_version = [string]$manifest.version
    manifest_hash_format = "utf8-lf-v1"
    manifest_sha256 = Get-NormalizedTextSha256 $manifestFile.FullName
    subject_executable = [IO.Path]::GetFileName($subjectPath)
    subject_executable_sha256 = $subjectHash
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
