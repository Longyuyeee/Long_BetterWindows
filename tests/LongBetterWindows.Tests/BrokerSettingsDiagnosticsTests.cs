using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Broker;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.Tests;

public sealed class BrokerSettingsDiagnosticsTests
{
    [Fact]
    public async Task Settings_default_enabled_and_persist_atomically()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"long-broker-settings-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "broker.json");
        var store = new BrokerSettingsStore(path);

        Assert.True(store.Load().Enabled);
        await store.SaveAsync(new BrokerSettings(false));
        Assert.False(store.Load().Enabled);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void Settings_malformed_file_falls_back_to_enabled()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"long-broker-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "broker.json");
        File.WriteAllText(path, "not-json");

        Assert.True(new BrokerSettingsStore(path).Load().Enabled);
    }

    [Fact]
    public async Task Diagnostics_export_only_contains_sanitized_aggregates()
    {
        var diagnostics = new BrokerDiagnostics();
        diagnostics.ConnectionAccepted();
        diagnostics.Request(BrokerMethods.HealthPing);
        diagnostics.Request("secret.plugin.method");
        diagnostics.Error(IpcErrorCodes.PluginNotFound);
        diagnostics.Error("secret_error");
        diagnostics.ConnectionClosed();

        var snapshot = diagnostics.Snapshot(true);
        Assert.Equal(1, snapshot.AcceptedConnections);
        Assert.Equal(0, snapshot.ActiveConnections);
        Assert.Equal(1, snapshot.RequestsByMethod[BrokerMethods.HealthPing]);
        Assert.Equal(1, snapshot.RequestsByMethod["unsupported"]);
        Assert.Equal(1, snapshot.ErrorsByCode[IpcErrorCodes.PluginNotFound]);
        Assert.Equal(1, snapshot.ErrorsByCode[IpcErrorCodes.InternalError]);

        var directory = Path.Combine(Path.GetTempPath(), $"long-broker-diagnostics-{Guid.NewGuid():N}");
        var path = await BrokerDiagnosticsExporter.ExportAsync(snapshot, directory);
        var json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("secret.plugin.method", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret_error", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sid", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plugin_path", json, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("running").GetBoolean());
    }
}
