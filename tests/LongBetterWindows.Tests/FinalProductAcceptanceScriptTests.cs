using System.IO;

namespace LongBetterWindows.Tests;

public sealed class FinalProductAcceptanceScriptTests
{
    [Fact]
    public void Approval_requires_real_evidence_clean_source_and_contract_specific_identity()
    {
        var source = Read("approve-final-validation-evidence.ps1");

        Assert.Contains("-ConfirmPassed", source);
        Assert.Contains("clean tracked worktree", source);
        Assert.Contains("artifacts/quality", source);
        Assert.Contains("verify-native-performance-evidence.ps1", source);
        Assert.Contains("lpwp_long_grid_e2e", source);
        Assert.Contains("long.plugin.ipc/1.0", source);
        Assert.Contains("lpwp_signed_reference", source);
        Assert.Contains("ExpectedPublicKeyFingerprint", source);
        Assert.Contains("[Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit", source);
        Assert.Contains("merge-base --is-ancestor $sourceCommit HEAD", source);
        Assert.Contains("Product files changed after ExpectedSourceCommit", source);
        Assert.Contains("Write-NewJsonFileAtomically", source);
        Assert.DoesNotContain("ExportPkcs8PrivateKey", source);
        Assert.DoesNotContain("Path]::GetRelativePath", source);
    }

    [Fact]
    public void Aggregator_requires_exact_committed_receipts_and_release_eligible_plugin_matrix()
    {
        var source = Read("verify-final-product-acceptance.ps1");

        Assert.Contains("-RequireReleaseEligible", source);
        Assert.Contains("[Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit", source);
        Assert.Contains("merge-base --is-ancestor $sourceCommit HEAD", source);
        Assert.Contains("plugin-manual-approvals|final-validation-approvals", source);
        Assert.Contains("[string]$matrix.source_commit -ne $currentHead", source);
        Assert.Contains("ls-files --error-unmatch", source);
        Assert.Contains("Exactly $($requiredIds.Count)", source);
        Assert.Contains("plugin_approval_receipt_count = 25", source);
        Assert.Contains("approved_final_product_acceptance", source);
        Assert.Contains(".sources", source);
        Assert.Contains("Write-NewJsonFileAtomically", source);
        Assert.Contains("Remove-Item -LiteralPath $sourceDirectory", source);
    }

    [Fact]
    public void External_gate_consumes_product_acceptance_as_a_mandatory_hashed_input()
    {
        var source = Read("verify-external-release-gate.ps1");

        Assert.Contains("[Parameter(Mandatory=$true)] [string] $ProductAcceptanceGatePath", source);
        Assert.Contains("Assert-ProductAcceptanceContract", source);
        Assert.Contains("product_acceptance_sha256", source);
        Assert.Contains("Final product-acceptance portable approval content is invalid", source);
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(FindRoot(), name));

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
