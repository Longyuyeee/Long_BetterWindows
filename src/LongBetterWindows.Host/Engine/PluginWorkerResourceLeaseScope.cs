namespace LongBetterWindows.Host.Engine;

internal sealed class PluginWorkerResourceLeaseScope : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Lease> _leases = new(StringComparer.Ordinal);
    private long _sequence;
    private bool _disposed;

    internal int Count
    {
        get { lock (_gate) return _leases.Count; }
    }

    internal string Acquire(IAsyncDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var id = Guid.NewGuid().ToString("N");
            _leases.Add(id, new Lease(++_sequence, resource));
            return id;
        }
    }

    internal async ValueTask<bool> ReleaseAsync(string leaseId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);
        Lease? lease;
        lock (_gate)
        {
            if (!_leases.Remove(leaseId, out lease)) return false;
        }
        await lease.Resource.DisposeAsync().ConfigureAwait(false);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        Lease[] leases;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            leases = _leases.Values.OrderByDescending(item => item.Sequence).ToArray();
            _leases.Clear();
        }

        List<Exception>? failures = null;
        foreach (var lease in leases)
        {
            try
            {
                await lease.Resource.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }
        if (failures is not null)
            throw new AggregateException("One or more plugin worker resources could not be released.", failures);
    }

    private sealed record Lease(long Sequence, IAsyncDisposable Resource);
}
