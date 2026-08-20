function Get-AutomatedReleaseEligibility {
    param(
        [int]$AutomatedGateCount,
        [int]$PassedGateCount,
        [int]$FailedGateCount,
        [int]$EnvironmentBlockedGateCount,
        [int]$NotRunGateCount,
        [int]$NotApplicableGateCount,
        [bool]$ContractValid,
        [bool]$SourceDirty
    )

    $counts = @(
        $AutomatedGateCount,
        $PassedGateCount,
        $FailedGateCount,
        $EnvironmentBlockedGateCount,
        $NotRunGateCount,
        $NotApplicableGateCount)
    if (@($counts | Where-Object { $_ -lt 0 }).Count -gt 0) {
        throw "Automated acceptance gate counts cannot be negative."
    }

    $classifiedCount = $PassedGateCount +
        $FailedGateCount +
        $EnvironmentBlockedGateCount +
        $NotRunGateCount +
        $NotApplicableGateCount
    if ($classifiedCount -ne $AutomatedGateCount) {
        throw "Automated acceptance gate counts are inconsistent."
    }

    return $ContractValid `
        -and -not $SourceDirty `
        -and $AutomatedGateCount -gt 0 `
        -and $FailedGateCount -eq 0 `
        -and $EnvironmentBlockedGateCount -eq 0 `
        -and $NotRunGateCount -eq 0 `
        -and ($PassedGateCount + $NotApplicableGateCount) -eq `
            $AutomatedGateCount
}
