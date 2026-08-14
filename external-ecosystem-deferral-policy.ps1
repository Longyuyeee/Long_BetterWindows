function Resolve-LongExternalEcosystemDeferral {
    param(
        [Parameter(Mandatory)] $Document,
        [Parameter(Mandatory)] [string] $ExpectedSourceCommit,
        [string] $ExpectedCandidateVersion
    )

    Set-StrictMode -Version Latest

    $required = [ordered]@{
        'lpwp-long-grid-e2e' = 'missing_long_grid_repository'
        'lpwp-signed-reference' = 'missing_approved_plugin_publisher_identity'
        'production-marketplace-rehearsal' = 'missing_production_registry_or_cdn_credentials'
    }
    $candidateVersion = [string]$Document.candidate_version
    $targetVersion = [string]$Document.target_version
    $acceptedAt = [DateTimeOffset]::MinValue
    if ([int]$Document.schema_version -ne 1 `
        -or [string]$Document.classification -ne 'external_ecosystem_deferral' `
        -or [string]$Document.status -ne 'deferred' `
        -or [string]$Document.source_commit -ne $ExpectedSourceCommit `
        -or $candidateVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' `
        -or $targetVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' `
        -or (-not [string]::IsNullOrWhiteSpace($ExpectedCandidateVersion) `
            -and $candidateVersion -ne $ExpectedCandidateVersion) `
        -or ([version]$targetVersion -le [version]$candidateVersion) `
        -or [string]$Document.default_feature_state -ne 'disabled' `
        -or ([string]$Document.accepted_by).Trim().Length -lt 2 `
        -or -not ([string]$Document.accepted_at).EndsWith('Z', [StringComparison]::OrdinalIgnoreCase) `
        -or -not [DateTimeOffset]::TryParse([string]$Document.accepted_at, [ref]$acceptedAt) `
        -or $acceptedAt.Offset -ne [TimeSpan]::Zero) {
        throw 'External ecosystem deferral identity is incomplete or does not match the candidate.'
    }

    $items = @($Document.items)
    if ($items.Count -ne $required.Count) {
        throw 'External ecosystem deferral must contain exactly three deferred items.'
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in $items) {
        $id = [string]$item.id
        if (-not $required.Contains($id) `
            -or -not $seen.Add($id) `
            -or [string]$item.status -ne 'deferred' `
            -or [string]$item.reason -ne [string]$required[$id] `
            -or [string]$item.target_version -ne $targetVersion `
            -or [string]$item.default_feature_state -ne 'disabled') {
            throw "External ecosystem deferral item is invalid: $id"
        }
    }
    if ($seen.Count -ne $required.Count) {
        throw 'External ecosystem deferral item set is incomplete.'
    }

    return [pscustomobject][ordered]@{
        source_commit = $ExpectedSourceCommit
        candidate_version = $candidateVersion
        target_version = $targetVersion
        accepted_by = ([string]$Document.accepted_by).Trim()
        accepted_at = $acceptedAt.UtcDateTime.ToString('O')
        items = @($items | ForEach-Object {
            [pscustomobject][ordered]@{
                id = [string]$_.id
                status = 'deferred'
                reason = [string]$_.reason
                target_version = $targetVersion
                default_feature_state = 'disabled'
            }
        })
    }
}
