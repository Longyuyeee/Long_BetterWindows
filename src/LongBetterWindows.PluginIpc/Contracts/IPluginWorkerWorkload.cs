namespace LongBetterWindows.PluginIpc.Contracts;

internal interface IPluginWorkerWorkload : IAsyncDisposable
{
    string PluginId { get; }
    IReadOnlySet<string> Commands { get; }
    IReadOnlySet<string> RequiredHostMethods { get; }

    ValueTask InvokeLifecycleAsync(
        PluginWorkerLifecycleOperation operation,
        string? language,
        CancellationToken cancellationToken);

    ValueTask<string?> InvokeCommandAsync(
        PluginWorkerCommandRequest request,
        CancellationToken cancellationToken);
}
