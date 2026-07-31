using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.Host.Broker;

internal sealed record BrokerDiagnosticsSnapshot(
    DateTimeOffset StartedUtc,
    DateTimeOffset CapturedUtc,
    bool Running,
    long AcceptedConnections,
    long RejectedConnections,
    long ActiveConnections,
    IReadOnlyDictionary<string, long> RequestsByMethod,
    IReadOnlyDictionary<string, long> ErrorsByCode);

internal sealed class BrokerDiagnostics
{
    private static readonly HashSet<string> KnownMethods =
    [
        BrokerMethods.HostHello,
        BrokerMethods.HealthPing,
        BrokerMethods.PluginCatalogList,
        BrokerMethods.PluginCatalogGet,
        BrokerMethods.CommandInvoke,
        BrokerMethods.CommandCancel,
        BrokerMethods.PluginOpen,
    ];
    private readonly ConcurrentDictionary<string, long> _methods = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _errors = new(StringComparer.Ordinal);
    private long _accepted;
    private long _rejected;
    private long _active;

    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;

    public void ConnectionAccepted()
    {
        Interlocked.Increment(ref _accepted);
        Interlocked.Increment(ref _active);
    }

    public void ConnectionRejected() => Interlocked.Increment(ref _rejected);
    public void ConnectionClosed() => Interlocked.Decrement(ref _active);

    public void Request(string? method)
        => _methods.AddOrUpdate(
            method is not null && KnownMethods.Contains(method) ? method : "unsupported",
            1,
            (_, count) => count + 1);

    public void Error(string code)
        => _errors.AddOrUpdate(IpcErrorCodes.Normalize(code), 1, (_, count) => count + 1);

    public BrokerDiagnosticsSnapshot Snapshot(bool running) => new(
        StartedUtc,
        DateTimeOffset.UtcNow,
        running,
        Interlocked.Read(ref _accepted),
        Interlocked.Read(ref _rejected),
        Interlocked.Read(ref _active),
        new SortedDictionary<string, long>(_methods, StringComparer.Ordinal),
        new SortedDictionary<string, long>(_errors, StringComparer.Ordinal));
}

internal static class BrokerDiagnosticsExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static async Task<string> ExportAsync(
        BrokerDiagnosticsSnapshot snapshot,
        string? directory = null,
        CancellationToken cancellationToken = default)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LongBetterWindows",
            "Diagnostics");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"plugin-broker-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(snapshot, JsonOptions),
            cancellationToken).ConfigureAwait(false);
        return path;
    }
}
