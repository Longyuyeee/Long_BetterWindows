using System.IO;
using System.IO.Pipes;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.PluginIpc.Client;
using Serilog;

namespace LongBetterWindows.Host.Broker;

internal sealed class LongPluginBrokerService : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly PluginCatalogProjection _catalog;
    private readonly PluginCommandEndpoint _commands;
    private readonly PluginOpenEndpoint _pluginOpen;
    private readonly BrokerDiagnostics _diagnostics = new();
    private readonly IBrokerClientIdentityProbe _identityProbe;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _connectionsLock = new();
    private readonly HashSet<Task> _connections = [];
    private Task? _acceptLoop;
    private readonly object _disposeLock = new();
    private Task? _disposeTask;

    public LongPluginBrokerService(
        PluginRegistry registry,
        string? pipeName = null,
        IBrokerClientIdentityProbe? identityProbe = null)
    {
        _pipeName = pipeName ?? BrokerPipeName.ForCurrentUser();
        _catalog = new PluginCatalogProjection(registry);
        _commands = new PluginCommandEndpoint(registry);
        _pluginOpen = new PluginOpenEndpoint(registry);
        _identityProbe = identityProbe ?? new WindowsBrokerClientIdentityProbe();
    }

    public void Start()
    {
        if (_acceptLoop is not null)
            throw new InvalidOperationException("Plugin broker has already started.");
        _acceptLoop = AcceptLoopAsync(_shutdown.Token);
    }

    public BrokerDiagnosticsSnapshot GetDiagnostics()
        => _diagnostics.Snapshot(_acceptLoop is { IsCompleted: false });

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    8,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                var accepted = pipe;
                pipe = null;
                var task = HandleConnectionAsync(accepted, cancellationToken);
                lock (_connectionsLock) _connections.Add(task);
                _ = task.ContinueWith(
                    completed => { lock (_connectionsLock) _connections.Remove(completed); },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (IOException ex)
            {
                Log.Warning(ex, "Plugin broker pipe is temporarily unavailable");
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        var accepted = false;
        await using (pipe)
        {
            try
            {
                var server = _identityProbe.GetServerIdentity();
                var client = _identityProbe.GetClientIdentity(pipe);
                if (!BrokerClientAuthentication.IsSameSecurityBoundary(server, client))
                {
                    _diagnostics.ConnectionRejected();
                    Log.Warning("Plugin broker rejected a client outside the host security boundary");
                    return;
                }

                _diagnostics.ConnectionAccepted();
                accepted = true;
                await new BrokerConnection(
                    pipe, _catalog, _commands, _pluginOpen, _diagnostics, App.ProductVersion)
                    .RunAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (!accepted && ex is UnauthorizedAccessException)
                    _diagnostics.ConnectionRejected();
                Log.Debug(ex, "Plugin broker connection closed");
            }
            finally
            {
                if (accepted)
                    _diagnostics.ConnectionClosed();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        _shutdown.Cancel();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        Task[] connections;
        lock (_connectionsLock) connections = _connections.ToArray();
        try { await Task.WhenAll(connections).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _shutdown.Dispose();
    }
}
