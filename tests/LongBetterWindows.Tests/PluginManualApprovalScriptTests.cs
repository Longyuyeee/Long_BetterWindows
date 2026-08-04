using System.IO;

namespace LongBetterWindows.Tests;

public sealed class PluginManualApprovalScriptTests
{
    [Fact]
    public void Approver_RequiresExplicitReviewAndLocalQualityEvidence()
    {
        var source = Read("approve-plugin-manual-evidence.ps1");

        Assert.Contains("-ConfirmPassed", source);
        Assert.Contains("clean tracked worktree", source);
        Assert.Contains("artifacts\\quality", source);
        Assert.Contains("manifest_sha256", source);
        Assert.Contains("manifest_hash_format", source);
        Assert.Contains("utf8-lf-v1", source);
        Assert.Contains("Get-NormalizedTextSha256", source);
        Assert.Contains("subject_executable_sha256", source);
        Assert.Contains("source_commit", source);
        Assert.Contains("evidence_files", source);
        Assert.Contains("release-evidence-io.ps1", source);
        Assert.Contains("Write-NewJsonFileAtomically", source);
        Assert.Contains("Update-JsonFileAtomically", source);
        Assert.Contains("existingReceiptHash", source);
        Assert.Contains("Omit -Replace for the first complete review", source);
        Assert.DoesNotContain("[IO.File]::WriteAllText(", source);
        Assert.Contains("do not commit the original captures", source);
    }

    [Fact]
    public void MatrixVerifier_ConsumesPortableReceiptsAndInvalidatesSourceChanges()
    {
        var source = Read("verify-plugin-positive-matrix.ps1");

        Assert.Contains("plugin-manual-approvals", source);
        Assert.Contains("approval_receipt_count", source);
        Assert.DoesNotContain("Path]::GetRelativePath", source);
        Assert.Contains("manifest_sha256", source);
        Assert.Contains("manifest_hash_format", source);
        Assert.Contains("utf8-lf-v1", source);
        Assert.Contains("Get-NormalizedTextSha256", source);
        Assert.Contains("subject_executable_sha256", source);
        Assert.Contains("git -C $PSScriptRoot diff", source);
        Assert.Contains("-- src", source);
        Assert.Contains("-and -not $approvedByReceipt", source);
        Assert.Contains(
            "Manual approval receipt has no matching matrix check",
            source);
    }

    [Fact]
    public void EvidenceIo_NormalizesTextLineEndingsBeforeHashing()
    {
        var source = Read("release-evidence-io.ps1");

        Assert.Contains("function Get-NormalizedTextSha256", source);
        Assert.Contains("Replace(\"`r`n\", \"`n\")", source);
        Assert.Contains("Replace(\"`r\", \"`n\")", source);
        Assert.Contains("UTF8Encoding]::new($false)", source);
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
