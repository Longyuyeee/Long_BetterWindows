function New-LongUnsignedReleasePolicy {
    param(
        [Parameter(Mandatory)]
        [string] $Version
    )

    Set-StrictMode -Version Latest

    $normalizedVersion = $Version.Trim()
    if ($normalizedVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
        throw "Release version must be a semantic version: $Version"
    }

    [pscustomobject][ordered]@{
        version = $normalizedVersion
        release_channel = if ($normalizedVersion.Contains('-')) { 'prerelease' } else { 'stable' }
        distribution_channel = 'unsigned'
        signed = $false
        publisher_identity = 'unverified'
        authenticode_status = 'not_signed'
        installer_privileges = 'lowest'
        smartscreen_disclosure_required = $true
        sha256_verification_required = $true
        update_manifest_signature_required = $true
        update_manifest_signature_algorithm = 'RSA-SHA256'
        security_notice = 'Windows publisher identity is not verified; SmartScreen may warn. Validate SHA-256 checksums before running.'
    }
}
