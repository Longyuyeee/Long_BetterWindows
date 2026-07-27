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
