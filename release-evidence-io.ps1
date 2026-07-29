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
