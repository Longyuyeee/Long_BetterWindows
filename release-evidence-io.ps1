function Get-FileHash {
    [CmdletBinding(DefaultParameterSetName='Path')]
    param(
        [Parameter(Mandatory=$true, Position=0, ParameterSetName='Path')]
        [string] $Path,
        [Parameter(Mandatory=$true, ParameterSetName='LiteralPath')]
        [string] $LiteralPath,
        [ValidateSet('SHA256')]
        [string] $Algorithm = 'SHA256'
    )

    $inputPath = if ($PSCmdlet.ParameterSetName -eq 'LiteralPath') {
        $LiteralPath
    }
    else {
        $Path
    }
    $resolvedPath = [IO.Path]::GetFullPath($inputPath)
    $stream = [IO.File]::OpenRead($resolvedPath)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = ([BitConverter]::ToString(
            $hasher.ComputeHash($stream))).Replace('-', '')
        return [pscustomobject]@{
            Algorithm = $Algorithm
            Hash = $hash
            Path = $resolvedPath
        }
    }
    finally {
        $hasher.Dispose()
        $stream.Dispose()
    }
}

function Get-NormalizedTextSha256 {
    param(
        [Parameter(Mandatory=$true)] [string] $Path
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $text = [IO.File]::ReadAllText($resolvedPath)
    $normalized = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($normalized)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $algorithm.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
    }
}

function Write-NewJsonFileAtomically {
    param(
        [Parameter(Mandatory=$true)] $Value,
        [Parameter(Mandatory=$true)] [string] $Path,
        [ValidateRange(2,100)] [int] $Depth = 8,
        [Parameter(Mandatory=$true)] [string] $Label
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (Test-Path -LiteralPath $resolvedPath) {
        throw "$Label already exists: $resolvedPath"
    }
    $parent = Split-Path -Parent $resolvedPath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        [IO.Directory]::CreateDirectory($parent) | Out-Null
    }
    $fileName = [IO.Path]::GetFileName($resolvedPath)
    $temporaryPath = Join-Path $parent (
        ".$fileName.$([Guid]::NewGuid().ToString('N')).tmp")
    try {
        $json = $Value | ConvertTo-Json -Depth $Depth
        [IO.File]::WriteAllText(
            $temporaryPath,
            $json,
            [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryPath, $resolvedPath)
    }
    catch {
        if (Test-Path -LiteralPath $resolvedPath) {
            throw "$Label already exists: $resolvedPath"
        }
        throw
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Write-NewTextFileAtomically {
    param(
        [Parameter(Mandatory=$true)] [string] $Value,
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [string] $Label
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (Test-Path -LiteralPath $resolvedPath) {
        throw "$Label already exists: $resolvedPath"
    }
    $parent = Split-Path -Parent $resolvedPath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        [IO.Directory]::CreateDirectory($parent) | Out-Null
    }
    $fileName = [IO.Path]::GetFileName($resolvedPath)
    $temporaryPath = Join-Path $parent (
        ".$fileName.$([Guid]::NewGuid().ToString('N')).tmp")
    try {
        [IO.File]::WriteAllText(
            $temporaryPath,
            $Value,
            [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryPath, $resolvedPath)
    }
    catch {
        if (Test-Path -LiteralPath $resolvedPath) {
            throw "$Label already exists: $resolvedPath"
        }
        throw
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Update-JsonFileAtomically {
    param(
        [Parameter(Mandatory=$true)] $Value,
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [string] $ExpectedSha256,
        [ValidateRange(2,100)] [int] $Depth = 8,
        [Parameter(Mandatory=$true)] [string] $Label
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if ($ExpectedSha256 -notmatch '^[0-9a-f]{64}$' `
        -or -not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "$Label update identity is invalid: $resolvedPath"
    }
    $parent = Split-Path -Parent $resolvedPath
    $fileName = [IO.Path]::GetFileName($resolvedPath)
    $temporaryPath = Join-Path $parent (
        ".$fileName.$([Guid]::NewGuid().ToString('N')).tmp")
    $backupPath = Join-Path $parent (
        ".$fileName.$([Guid]::NewGuid().ToString('N')).bak")
    $lockPath = Join-Path $parent ".$fileName.approval.lock"
    $lock = $null
    try {
        try {
            $lock = [IO.FileStream]::new(
                $lockPath,
                [IO.FileMode]::CreateNew,
                [IO.FileAccess]::Write,
                [IO.FileShare]::None,
                1,
                [IO.FileOptions]::DeleteOnClose)
        }
        catch [IO.IOException] {
            throw "$Label is already being updated: $resolvedPath"
        }
        $actualHash = (Get-FileHash -LiteralPath $resolvedPath -Algorithm SHA256).
            Hash.ToLowerInvariant()
        if ($actualHash -ne $ExpectedSha256) {
            throw "$Label changed after validation: $resolvedPath"
        }
        $json = $Value | ConvertTo-Json -Depth $Depth
        [IO.File]::WriteAllText(
            $temporaryPath,
            $json,
            [Text.UTF8Encoding]::new($false))
        [IO.File]::Replace($temporaryPath, $resolvedPath, $backupPath)
    }
    finally {
        if ($null -ne $lock) {
            $lock.Dispose()
        }
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
        if (Test-Path -LiteralPath $backupPath) {
            Remove-Item -LiteralPath $backupPath -Force
        }
    }
}

function Update-TextFileAtomically {
    param(
        [Parameter(Mandatory=$true)] [string] $Value,
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [string] $ExpectedSha256,
        [Parameter(Mandatory=$true)] [string] $Label
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if ($ExpectedSha256 -notmatch '^[0-9a-f]{64}$' `
        -or -not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "$Label update identity is invalid: $resolvedPath"
    }
    $parent = Split-Path -Parent $resolvedPath
    $fileName = [IO.Path]::GetFileName($resolvedPath)
    $temporaryPath = Join-Path $parent (
        ".$fileName.$([Guid]::NewGuid().ToString('N')).tmp")
    $backupPath = Join-Path $parent (
        ".$fileName.$([Guid]::NewGuid().ToString('N')).bak")
    $lockPath = Join-Path $parent ".$fileName.update.lock"
    $lock = $null
    try {
        try {
            $lock = [IO.FileStream]::new(
                $lockPath,
                [IO.FileMode]::CreateNew,
                [IO.FileAccess]::Write,
                [IO.FileShare]::None,
                1,
                [IO.FileOptions]::DeleteOnClose)
        }
        catch [IO.IOException] {
            throw "$Label is already being updated: $resolvedPath"
        }
        $actualHash = (Get-FileHash -LiteralPath $resolvedPath -Algorithm SHA256).
            Hash.ToLowerInvariant()
        if ($actualHash -ne $ExpectedSha256) {
            throw "$Label changed after validation: $resolvedPath"
        }
        [IO.File]::WriteAllText(
            $temporaryPath,
            $Value,
            [Text.UTF8Encoding]::new($false))
        [IO.File]::Replace($temporaryPath, $resolvedPath, $backupPath)
    }
    finally {
        if ($null -ne $lock) {
            $lock.Dispose()
        }
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
        if (Test-Path -LiteralPath $backupPath) {
            Remove-Item -LiteralPath $backupPath -Force
        }
    }
}
