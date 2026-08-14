function Get-LongReviewModel {
    param([Parameter(Mandatory)] $Document)

    $property = $Document.PSObject.Properties['review_model']
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        return 'independent'
    }
    return ([string]$property.Value).Trim()
}

function Resolve-LongReleaseReviewPolicy {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('independent', 'single_maintainer')]
        [string] $ReviewModel,
        [Parameter(Mandatory)] [string] $CandidateVersion,
        [Parameter(Mandatory)] [string] $Operator,
        [Parameter(Mandatory)] [string] $Reviewer,
        [string] $RiskAcceptedBy,
        [string] $RiskAcceptedAt,
        [string] $RiskReason,
        [string] $RiskAcceptedVersion
    )

    Set-StrictMode -Version Latest

    $operatorName = $Operator.Trim()
    $reviewerName = $Reviewer.Trim()
    if ($operatorName.Length -lt 2 -or $reviewerName.Length -lt 2) {
        throw 'Release review operator and reviewer identities must each contain at least two characters.'
    }

    if ($ReviewModel -eq 'independent') {
        if ([string]::Equals($operatorName, $reviewerName, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Independent review requires distinct operator and reviewer identities.'
        }
        if (@($RiskAcceptedBy, $RiskAcceptedAt, $RiskReason, $RiskAcceptedVersion) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) {
            throw 'Independent review must not include single-maintainer risk acceptance fields.'
        }
        return [pscustomobject][ordered]@{
            review_model = 'independent'
            independent_review = $true
            operator = $operatorName
            reviewer = $reviewerName
            risk_acceptance = $null
        }
    }

    if ($CandidateVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw 'Single-maintainer risk acceptance is allowed only for a stable semantic version.'
    }
    if (-not [string]::Equals(
        $operatorName,
        $reviewerName,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Single-maintainer review requires one consistent operator and reviewer identity.'
    }

    $acceptedBy = ([string]$RiskAcceptedBy).Trim()
    if ($acceptedBy.Length -lt 2 -or -not [string]::Equals(
        $acceptedBy,
        $operatorName,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Single-maintainer risk acceptance identity must match the operator and reviewer.'
    }
    if ($RiskReason -ne 'no_second_machine_or_independent_reviewer') {
        throw 'Single-maintainer risk reason must be no_second_machine_or_independent_reviewer.'
    }
    if ($RiskAcceptedVersion -ne $CandidateVersion) {
        throw 'Single-maintainer risk acceptance version does not match the candidate version.'
    }

    $acceptedAt = [DateTimeOffset]::MinValue
    if ([string]::IsNullOrWhiteSpace($RiskAcceptedAt) `
        -or -not $RiskAcceptedAt.EndsWith('Z', [StringComparison]::OrdinalIgnoreCase) `
        -or -not [DateTimeOffset]::TryParse($RiskAcceptedAt, [ref]$acceptedAt) `
        -or $acceptedAt.Offset -ne [TimeSpan]::Zero) {
        throw 'Single-maintainer risk acceptance time must be an explicit UTC timestamp.'
    }

    return [pscustomobject][ordered]@{
        review_model = 'single_maintainer'
        independent_review = $false
        operator = $operatorName
        reviewer = $reviewerName
        risk_acceptance = [pscustomobject][ordered]@{
            risk_accepted_by = $acceptedBy
            risk_accepted_at = $acceptedAt.UtcDateTime.ToString('O')
            risk_reason = $RiskReason
            risk_accepted_version = $RiskAcceptedVersion
        }
    }
}
