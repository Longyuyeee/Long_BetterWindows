using System.Windows;
using System.Windows.Threading;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.Host.Broker;

internal sealed class PluginOpenEndpoint
{
    private readonly PluginRegistry _registry;
    private readonly Func<string, CancellationToken, Task<PluginMainUiOpenStatus>> _open;

    public PluginOpenEndpoint(
        PluginRegistry registry,
        Func<string, CancellationToken, Task<PluginMainUiOpenStatus>>? open = null)
    {
        _registry = registry;
        _open = open ?? OpenOnUiThreadAsync;
    }

    public async Task<PluginOpenOutcome> OpenAsync(
        PluginOpenRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PluginId))
            return PluginOpenOutcome.Failure(IpcErrorCodes.InvalidRequest, "plugin_id is required.");

        var status = await _open(request.PluginId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return status switch
        {
            PluginMainUiOpenStatus.Opened =>
                PluginOpenOutcome.Success(new PluginOpenResponse("opened")),
            PluginMainUiOpenStatus.PluginMissing =>
                PluginOpenOutcome.Failure(IpcErrorCodes.PluginNotFound, "The requested plugin is not installed."),
            PluginMainUiOpenStatus.MainUiUnavailable =>
                PluginOpenOutcome.Failure(IpcErrorCodes.SurfaceNotSupported, "The plugin does not expose a main UI."),
            PluginMainUiOpenStatus.StartFailed =>
                PluginOpenOutcome.Failure(IpcErrorCodes.PluginCrashed, "The plugin could not be started."),
            _ => PluginOpenOutcome.Failure(IpcErrorCodes.InternalError, "The plugin UI could not be opened."),
        };
    }

    private async Task<PluginMainUiOpenStatus> OpenOnUiThreadAsync(
        string pluginId,
        CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return await PluginMainUiLauncher.OpenAsync(_registry, pluginId).ConfigureAwait(false);

        var operation = dispatcher.InvokeAsync(
            () => PluginMainUiLauncher.OpenAsync(_registry, pluginId),
            DispatcherPriority.Normal,
            cancellationToken);
        return await operation.Task.Unwrap().ConfigureAwait(false);
    }
}

internal sealed record PluginOpenOutcome(PluginOpenResponse? Result, IpcError? Error)
{
    public static PluginOpenOutcome Success(PluginOpenResponse result) => new(result, null);
    public static PluginOpenOutcome Failure(string code, string message) =>
        new(null, new IpcError(code, message));
}
