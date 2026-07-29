using LongBetterWindows.Host.Contracts;
using LongBetterWindows.PluginSdk.Testing;
using Xunit;

namespace EmptyPlugin.Tests;

public sealed class PluginContractTests
{
    [Fact]
    public async Task Plugin_MatchesManifestAndCompletesLifecycle()
    {
        var plugin = new EmptyPluginImpl();
        PluginContractHarness.VerifyIdentity(
            plugin,
            new PluginManifest
            {
                Id = "com.example.empty",
                Name = "空插件模板",
                Version = "1.0.0",
                EntryPoint = "EmptyPlugin.dll",
            });

        await PluginContractHarness.VerifyLifecycleAsync(
            plugin,
            new PluginTestHost());
    }
}
