using System.IO;

namespace LongBetterWindows.Tests;

public sealed class PluginManualValidationCompletionScriptTests
{
    [Fact]
    public void Completion_BindsApprovalToTheCurrentStartedSession()
    {
        var source = Read("complete-plugin-validation.ps1");

        Assert.Contains("-ConfirmPassed", source);
        Assert.Contains("plan-next-plugin-validation.ps1", source);
        Assert.Contains("plugin_manual_validation_session", source);
        Assert.Contains("launch_status -ne \"started\"", source);
        Assert.Contains("pending_human_observation", source);
        Assert.Contains("candidate_commit", source);
        Assert.Contains("subject_executable_sha256", source);
        Assert.Contains("approve-plugin-manual-evidence.ps1", source);
        Assert.Contains("ConfirmPassed = $true", source);
        Assert.Contains("receipt_created_pending_commit", source);
    }

    [Fact]
    public void Completion_RequiresObservedEvidenceFromTheSessionDirectory()
    {
        var source = Read("complete-plugin-validation.ps1");

        Assert.Contains("Evidence must belong to the selected validation session", source);
        Assert.Contains("session file cannot replace observed UI evidence", source);
        Assert.Contains("evidence is empty", source);
        Assert.Contains("At least one observed UI evidence file is required", source);
        Assert.Contains("EvidenceFiles = @($reviewEvidence) + @($sessionFile)", source);
        Assert.DoesNotContain("review_status = \"passed\"", source);
    }

    private static string Read(string relativePath)
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
