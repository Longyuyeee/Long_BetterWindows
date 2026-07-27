namespace LongBetterWindows.Host.Services;

/// <summary>
/// Prevents an asynchronously produced payload from being delivered after its
/// owning UI or plugin lifetime has been cancelled.
/// </summary>
public static class AsyncDeliveryBoundary
{
    public static async Task<T> RunAsync<T>(
        Func<Task<T>> produceAsync,
        Func<T, Task> deliverAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(produceAsync);
        ArgumentNullException.ThrowIfNull(deliverAsync);

        cancellationToken.ThrowIfCancellationRequested();
        var payload = await produceAsync().ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        await deliverAsync(payload).ConfigureAwait(true);
        return payload;
    }
}
