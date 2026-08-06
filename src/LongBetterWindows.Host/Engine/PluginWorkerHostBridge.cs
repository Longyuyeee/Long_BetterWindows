using System.Text.Json;
using System.Text.RegularExpressions;
using LongBetterWindows.PluginIpc.Client;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.Host.Engine;

internal sealed partial class PluginWorkerHostBridge : IExperimentalPluginWorkerHostBridge
{
    private readonly string _pluginId;
    private readonly Func<string, bool> _hasCapability;
    private readonly PluginWorkerResourceLeaseScope _leases = new();

    internal PluginWorkerHostBridge(string pluginId)
        : this(pluginId, HostProvider.Instance.HasCapability)
    {
    }

    internal PluginWorkerHostBridge(string pluginId, Func<string, bool> hasCapability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(hasCapability);
        _pluginId = pluginId;
        _hasCapability = hasCapability;
    }

    internal int LeaseCount => _leases.Count;

    internal string TrackResource(IAsyncDisposable resource) => _leases.Acquire(resource);

    internal ValueTask<bool> ReleaseResourceAsync(string leaseId) => _leases.ReleaseAsync(leaseId);

    public Task<IpcEnvelope> HandleRequestAsync(
        IpcEnvelope request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Protocol != PluginWorkerProtocol.Name
            || request.Kind != "request"
            || string.IsNullOrWhiteSpace(request.Id))
        {
            return Task.FromResult(Failure(
                request.Id,
                IpcErrorCodes.InvalidRequest,
                "Worker host request envelope is invalid."));
        }
        if (request.Method != PluginWorkerProtocol.HostCapabilityQuery)
        {
            return Task.FromResult(Failure(
                request.Id,
                IpcErrorCodes.SurfaceNotSupported,
                "Worker host method is not supported."));
        }

        PluginWorkerCapabilityQueryRequest? query;
        try
        {
            query = request.Payload?.Deserialize<PluginWorkerCapabilityQueryRequest>(IpcJson.Options);
        }
        catch (JsonException)
        {
            query = null;
        }
        if (query is null || !CapabilityNamePattern().IsMatch(query.Capability))
        {
            return Task.FromResult(Failure(
                request.Id,
                IpcErrorCodes.InvalidRequest,
                "Capability query is invalid."));
        }

        using (PluginAccessContext.Enter(_pluginId))
        {
            var granted = _hasCapability(query.Capability);
            return Task.FromResult(IpcEnvelope.Response(
                PluginWorkerProtocol.Name,
                request.Id,
                new PluginWorkerCapabilityQueryResponse(granted)));
        }
    }

    public ValueTask DisposeAsync() => _leases.DisposeAsync();

    private static IpcEnvelope Failure(string? id, string code, string message)
        => IpcEnvelope.Failure(
            PluginWorkerProtocol.Name,
            string.IsNullOrWhiteSpace(id) ? "invalid" : id,
            new IpcError(code, message));

    [GeneratedRegex("^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CapabilityNamePattern();
}
