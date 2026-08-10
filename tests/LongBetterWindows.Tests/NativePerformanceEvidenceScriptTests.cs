using System.IO;

namespace LongBetterWindows.Tests;

public sealed class NativePerformanceEvidenceScriptTests
{
    [Fact]
    public void CaptureScript_RequiresAdminAndKeepsRawTracePending()
    {
        var source = ReadRepositoryFile(
            "capture-native-performance-evidence.ps1");

        Assert.Contains("Test-IsAdministrator", source);
        Assert.Contains("CPU.Light", source);
        Assert.Contains("DesktopComposition.Verbose", source);
        Assert.Contains("--quality-plugin-page-performance-report", source);
        Assert.Contains("source_dirty = $false", source);
        Assert.Contains("analysis_status = \"pending_analysis\"", source);
        Assert.Contains("release_gate_passed = $false", source);
        Assert.Contains("Get-FileHash", source);
        Assert.Contains("-cancel", source);
        Assert.Contains("release-evidence-io.ps1", source);
        Assert.Contains("Write-NewJsonFileAtomically", source);
        Assert.DoesNotContain("Set-Content -LiteralPath $manifestPath", source);
    }

    [Fact]
    public void VerifyScript_RejectsHashOrPrematureApprovalChanges()
    {
        var source = ReadRepositoryFile(
            "verify-native-performance-evidence.ps1");

        Assert.Contains("Source commit does not match", source);
        Assert.Contains("Capture was not produced by an elevated", source);
        Assert.Contains("Required CPU and DesktopComposition", source);
        Assert.Contains("Unapproved evidence must remain pending_analysis", source);
        Assert.Contains("Raw WPR capture cannot mark", source);
        Assert.Contains("Evidence hash mismatch", source);
        Assert.Contains("$profiles.Count -ne 2 `", source);
        Assert.Contains("@($performance.samples).Count `", source);
        Assert.DoesNotContain("exit 1", source);
    }

    [Fact]
    public void WpaExport_IsImmutableAndCannotApproveRawTrace()
    {
        var source = ReadRepositoryFile(
            "export-native-performance-tables.ps1");

        Assert.Contains("wpaexporter.exe", source);
        Assert.Contains("-outputformat CSV", source);
        Assert.Contains("native-performance-export.json", source);
        Assert.Contains("analysis_status = 'pending_review'", source);
        Assert.Contains("release_gate_passed = $false", source);
        Assert.Contains("Write-NewJsonFileAtomically", source);
        Assert.Contains("Directory]::Move", source);

        var verifier = ReadRepositoryFile(
            "verify-native-performance-export.ps1");
        Assert.Contains("raw manifest binding does not match", verifier);
        Assert.Contains("trace binding does not match", verifier);
        Assert.Contains("export table changed", verifier);
        Assert.Contains("cannot pass the release gate", verifier);
    }

    [Fact]
    public void Analysis_RequiresBothWpaViewsAndIndependentFinalApproval()
    {
        var source = ReadRepositoryFile(
            "new-native-performance-analysis-evidence.ps1");

        Assert.Contains("ConfirmCpuSampledReviewed", source);
        Assert.Contains("ConfirmDesktopCompositionReviewed", source);
        Assert.Contains("ConfirmTimelineCorrelated", source);
        Assert.Contains("ConfirmNoUnresolvedProductHotspot", source);
        Assert.Contains("CPU and Desktop Composition require separate", source);
        Assert.Contains("classification = 'native_performance_analysis'", source);
        Assert.Contains("release_gate_passed = $false", source);
        Assert.Contains("Write-NewJsonFileAtomically", source);

        var verifier = ReadRepositoryFile(
            "verify-native-performance-analysis.ps1");
        Assert.Contains("Separate CPU and Desktop Composition evidence", verifier);
        Assert.Contains("analysis WPA export binding does not match", verifier);
        Assert.Contains("Independent final approval is still required", verifier);

        var approval = ReadRepositoryFile(
            "approve-final-validation-evidence.ps1");
        Assert.Contains("verify-native-performance-analysis.ps1", approval);
        Assert.Contains("final approver must differ from the WPA analyst", approval);
        Assert.Contains("every hash-locked WPA analysis file", approval);
        Assert.Contains("native_performance_analysis_sha256", approval);
    }

    [Fact]
    public void MemoryProbe_BindsCleanCommitAndHostExecutable()
    {
        var source = ReadRepositoryFile("measure-plugin-memory.ps1");

        Assert.Contains("--untracked-files=no", source);
        Assert.Contains("Formal memory evidence requires a clean", source);
        Assert.Contains("source_commit = $sourceCommit", source);
        Assert.Contains("source_dirty = $sourceDirty", source);
        Assert.Contains("host_executable_sha256", source);
        Assert.Contains("maximum_working_set_mb", source);
        Assert.Contains("$maximum -lt $WorkingSetLimitMB", source);
        Assert.Contains("release-evidence-io.ps1", source);
        Assert.Contains("Write-NewJsonFileAtomically", source);
        Assert.DoesNotContain(
            "Set-Content -LiteralPath $reportPath",
            source);

        var verifier = ReadRepositoryFile(
            "verify-plugin-memory-evidence.ps1");
        Assert.Contains("Memory evidence has too few samples", verifier);
        Assert.Contains("idle interval is too short", verifier);
        Assert.Contains("strictly below the limit", verifier);
        Assert.Contains("host_executable_sha256", verifier);
    }

    private static string ReadRepositoryFile(string relativePath)
        => File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            relativePath));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "LongBetterWindows.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Repository root was not found.");
    }
}
