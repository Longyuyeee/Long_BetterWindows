using System.IO;

namespace LongBetterWindows.Tests;

public sealed class PluginManualValidationPlannerScriptTests
{
    [Fact]
    public void Planner_BindsTheNextCheckToTheFrozenCandidate()
    {
        var source = Read("plan-next-plugin-validation.ps1");

        Assert.Contains("release-manifest.json", source);
        Assert.Contains("SelectSingleNode", source);
        Assert.Contains("source_dirty", source);
        Assert.Contains("release_eligible", source);
        Assert.Contains("merge-base --is-ancestor", source);
        Assert.Contains("docs/plugin-manual-approvals/", source);
        Assert.Contains("Get-FileHash", source);
        Assert.Contains("ZipFile]::OpenRead", source);
        Assert.Contains("archivedSubjectHash", source);
        Assert.Contains("verify-plugin-positive-matrix.ps1", source);
        Assert.Contains("Sort-Object RiskRank, PluginIndex, ManualIndex", source);
        Assert.Contains("approval_command", source);
        Assert.Contains("-SubjectExecutable", source);
        Assert.Contains(
            "$staleApprovalReceipts.Add(",
            source);
        Assert.Contains("stale_approval_receipt_count", source);
        Assert.Contains("continue", source);
        Assert.Contains("Write-NewJsonFileAtomically", source);
        Assert.DoesNotContain("ConfirmPassed = $true", source);
        Assert.DoesNotContain("approve-plugin-manual-evidence.ps1'", source);
    }

    [Fact]
    public void Planner_UsesTheDocumentedRiskOrder()
    {
        var source = Read("plan-next-plugin-validation.ps1");

        Assert.Contains(
            "$riskOrder = @{ low = 0; medium = 1; high = 2; critical = 3 }",
            source);
        Assert.Contains("required_for_release", source);
        Assert.Contains("pending_manual_check_count", source);
        Assert.Contains("selected_plugin_id", source);
        Assert.Contains("selected_scope_complete", source);
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
