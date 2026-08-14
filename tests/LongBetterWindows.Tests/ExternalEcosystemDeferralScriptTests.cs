using System.IO;

namespace LongBetterWindows.Tests;

public sealed class ExternalEcosystemDeferralScriptTests
{
    [Fact]
    public void Producer_requires_explicit_confirmation_and_records_exact_deferred_scope()
    {
        var source = Read("new-external-ecosystem-deferral.ps1");

        Assert.Contains("ConfirmDeferred", source);
        Assert.Contains("external_ecosystem_deferral", source);
        Assert.Contains("lpwp-long-grid-e2e", source);
        Assert.Contains("lpwp-signed-reference", source);
        Assert.Contains("production-marketplace-rehearsal", source);
        Assert.Contains("default_feature_state = 'disabled'", source);
        Assert.Contains("Write-NewJsonFileAtomically", source);
    }

    [Fact]
    public void Policy_rejects_false_passes_cross_version_reuse_and_incomplete_items()
    {
        var source = Read("external-ecosystem-deferral-policy.ps1");

        Assert.Contains("[string]$item.status -ne 'deferred'", source);
        Assert.Contains("$candidateVersion -ne $ExpectedCandidateVersion", source);
        Assert.Contains("exactly three deferred items", source);
        Assert.Contains("[version]$targetVersion -le [version]$candidateVersion", source);
        Assert.Contains("default_feature_state -ne 'disabled'", source);
        Assert.Contains("accepted_at", source);
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
