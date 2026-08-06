using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.PluginIpc.Client;

internal interface IExperimentalPluginWorkerHostBridge : IAsyncDisposable
{
    Task<IpcEnvelope> HandleRequestAsync(
        IpcEnvelope request,
        CancellationToken cancellationToken);
}
