using LongBetterWindows.Host.Contracts;
using LongBetterWindows.PluginSdk.Testing;
using Xunit;

namespace HotkeyPlugin.Tests;

public sealed class PluginContractTests
{
    [Fact]
    public void Plugin_MatchesManifestAndMissingHotkeyIsDenied()
    {
        var plugin = new HotkeyPluginImpl();
        PluginContractHarness.VerifyIdentity(
            plugin,
            new PluginManifest
            {
                Id = "com.example.hotkey",
                Name = "热键插件模板",
                Version = "1.0.0",
                EntryPoint = "HotkeyPlugin.dll",
            });

        var host = new PluginTestHost();
        PluginContractHarness.VerifyCapabilityDenied(
            host,
            api => api.HotKey,
            "system.hotkey");
    }
}
