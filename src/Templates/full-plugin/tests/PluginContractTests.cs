using LongBetterWindows.Host.Contracts;
using LongBetterWindows.PluginSdk.Testing;
using Xunit;

namespace FullPlugin.Tests;

public sealed class PluginContractTests
{
    [Fact]
    public void Plugin_MatchesManifestAndMissingStorageIsDenied()
    {
        var plugin = new FullPluginImpl();
        PluginContractHarness.VerifyIdentity(
            plugin,
            new PluginManifest
            {
                Id = "com.example.full",
                Name = "全功能插件模板",
                Version = "1.0.0",
                EntryPoint = "FullPlugin.dll",
            });

        var host = new PluginTestHost();
        PluginContractHarness.VerifyCapabilityDenied(
            host,
            api => api.Storage,
            "storage.local");
    }
}
