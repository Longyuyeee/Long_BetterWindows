using System.Security.Cryptography;
using System.Text;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.PluginWorker.Reference;

internal sealed class ReferenceWorkload : IPluginWorkerWorkload
{
    public string PluginId => "reference.headless.native";

    public IReadOnlySet<string> Commands { get; } = new HashSet<string>(
        ["reference.sha256", "reference.delay"],
        StringComparer.Ordinal);

    public IReadOnlySet<string> RequiredHostMethods { get; } = new HashSet<string>(
        StringComparer.Ordinal);

    public ValueTask InvokeLifecycleAsync(
        PluginWorkerLifecycleOperation operation,
        string? language,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public async ValueTask<string?> InvokeCommandAsync(
        PluginWorkerCommandRequest request,
        CancellationToken cancellationToken)
    {
        return request.Command switch
        {
            "reference.sha256" => Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(request.Text ?? string.Empty))),
            "reference.delay" => await DelayAsync(request, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException("Reference command is not declared."),
        };
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<string?> DelayAsync(
        PluginWorkerCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DelayMilliseconds is < 0 or > 60_000)
            throw new ArgumentOutOfRangeException(nameof(request.DelayMilliseconds));
        await Task.Delay(request.DelayMilliseconds, cancellationToken).ConfigureAwait(false);
        return request.Text;
    }
}
