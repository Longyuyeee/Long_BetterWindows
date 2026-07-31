using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using LongBetterWindows.PluginIpc.Contracts;
using LongBetterWindows.PluginIpc.Framing;
using Serilog;

namespace LongBetterWindows.Host.Broker;

internal sealed class BrokerConnection(
    Stream stream,
    PluginCatalogProjection catalog,
    PluginCommandEndpoint commands,
    PluginOpenEndpoint pluginOpen,
    string hostVersion)
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _invocations =
        new(StringComparer.Ordinal);

    internal static readonly string[] Features =
    [
        BrokerMethods.HealthPing,
        BrokerMethods.PluginCatalogList,
        BrokerMethods.PluginCatalogGet,
        BrokerMethods.CommandInvoke,
        BrokerMethods.CommandCancel,
        BrokerMethods.PluginOpen,
    ];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var disconnected = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var requests = new HashSet<Task>();
        try
        {
            var hello = await ReadAsync(disconnected.Token).ConfigureAwait(false);
            if (hello is null || !await CompleteHelloAsync(hello, disconnected.Token).ConfigureAwait(false))
                return;

            while (!disconnected.IsCancellationRequested)
            {
                var request = await ReadAsync(disconnected.Token).ConfigureAwait(false);
                if (request is null)
                    break;

                if (!ValidateEnvelope(request, out var errorCode, out var error))
                {
                    await WriteErrorAsync(request.Id, errorCode, error, disconnected.Token).ConfigureAwait(false);
                    if (errorCode == IpcErrorCodes.IncompatibleProtocol)
                        break;
                    continue;
                }

                if (request.Method == BrokerMethods.HostHello)
                {
                    await WriteErrorAsync(request.Id, IpcErrorCodes.InvalidRequest, "host.hello was already completed.", disconnected.Token).ConfigureAwait(false);
                    continue;
                }

                if (request.Method == BrokerMethods.CommandCancel)
                {
                    await CancelAsync(request, disconnected.Token).ConfigureAwait(false);
                    continue;
                }

                var task = ProcessAsync(request, disconnected.Token);
                lock (requests) requests.Add(task);
                _ = task.ContinueWith(
                    completed => { lock (requests) requests.Remove(completed); },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        finally
        {
            disconnected.Cancel();
            foreach (var invocation in _invocations.Values)
                invocation.Cancel();
            Task[] pending;
            lock (requests) pending = requests.ToArray();
            try { await Task.WhenAll(pending).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
            foreach (var invocation in _invocations.Values)
                invocation.Dispose();
            _invocations.Clear();
            _writeGate.Dispose();
        }
    }

    private async Task<IpcEnvelope?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await LengthPrefixedJsonFraming.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<bool> CompleteHelloAsync(IpcEnvelope request, CancellationToken cancellationToken)
    {
        if (!ValidateEnvelope(request, out var errorCode, out var error))
        {
            await WriteErrorAsync(request.Id, errorCode, error, cancellationToken).ConfigureAwait(false);
            return false;
        }
        if (request.Method != BrokerMethods.HostHello)
        {
            await WriteErrorAsync(request.Id, IpcErrorCodes.Unauthenticated, "host.hello must be the first request.", cancellationToken).ConfigureAwait(false);
            return false;
        }

        try
        {
            var hello = Deserialize<HostHelloRequest>(request);
            if (!hello.Protocols.Contains(IpcProtocol.Name, StringComparer.Ordinal))
            {
                await WriteErrorAsync(request.Id, IpcErrorCodes.IncompatibleProtocol, "No compatible IPC protocol was offered.", cancellationToken).ConfigureAwait(false);
                return false;
            }
            await WriteResultAsync(request.Id, new HostHelloResponse(
                "Long助手", hostVersion, IpcProtocol.Name, Features,
                IpcProtocol.MaximumFrameBytes,
                IpcProtocol.MaximumDeadlineMilliseconds), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (JsonException)
        {
            await WriteErrorAsync(request.Id, IpcErrorCodes.InvalidRequest, "The request payload has an invalid shape.", cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    private async Task ProcessAsync(IpcEnvelope request, CancellationToken connectionToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(connectionToken);
        deadline.CancelAfter(IpcProtocol.NormalizeDeadline(request.DeadlineMilliseconds));
        try
        {
            switch (request.Method)
            {
                case BrokerMethods.HealthPing:
                    var ping = Deserialize<HealthPingRequest>(request);
                    await WriteResultAsync(request.Id, new HealthPingResponse(ping.Nonce, DateTimeOffset.UtcNow), deadline.Token).ConfigureAwait(false);
                    break;
                case BrokerMethods.PluginCatalogList:
                    await WriteResultAsync(request.Id, catalog.List(Deserialize<PluginCatalogListRequest>(request)), deadline.Token).ConfigureAwait(false);
                    break;
                case BrokerMethods.PluginCatalogGet:
                    var catalogResult = catalog.Get(Deserialize<PluginCatalogGetRequest>(request));
                    if (catalogResult is null)
                        await WriteErrorAsync(request.Id, IpcErrorCodes.PluginNotFound, "The requested plugin is not installed.", deadline.Token).ConfigureAwait(false);
                    else
                        await WriteResultAsync(request.Id, catalogResult, deadline.Token).ConfigureAwait(false);
                    break;
                case BrokerMethods.CommandInvoke:
                    await InvokeCommandAsync(request, deadline, connectionToken).ConfigureAwait(false);
                    break;
                case BrokerMethods.PluginOpen:
                    var open = await pluginOpen.OpenAsync(
                        Deserialize<PluginOpenRequest>(request), deadline.Token).ConfigureAwait(false);
                    if (open.Error is not null)
                        await WriteErrorAsync(request.Id, open.Error.Code, open.Error.Message, deadline.Token).ConfigureAwait(false);
                    else
                        await WriteResultAsync(request.Id, open.Result!, deadline.Token).ConfigureAwait(false);
                    break;
                default:
                    await WriteErrorAsync(request.Id, IpcErrorCodes.SurfaceNotSupported, "The requested broker surface is not available.", deadline.Token).ConfigureAwait(false);
                    break;
            }
        }
        catch (JsonException)
        {
            await TryWriteErrorAsync(request.Id, IpcErrorCodes.InvalidRequest, "The request payload has an invalid shape.", connectionToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!connectionToken.IsCancellationRequested)
        {
            await TryWriteErrorAsync(request.Id, IpcErrorCodes.Timeout, "The request deadline elapsed.", connectionToken).ConfigureAwait(false);
        }
        catch (IOException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "Plugin broker request {RequestId} failed", request.Id);
            await TryWriteErrorAsync(request.Id, IpcErrorCodes.InternalError, "The broker request failed.", connectionToken).ConfigureAwait(false);
        }
    }

    private async Task InvokeCommandAsync(
        IpcEnvelope request,
        CancellationTokenSource deadline,
        CancellationToken connectionToken)
    {
        using var invocation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        if (!_invocations.TryAdd(request.Id, invocation))
        {
            await WriteErrorAsync(request.Id, IpcErrorCodes.InvalidRequest, "A request with this id is already active.", connectionToken).ConfigureAwait(false);
            return;
        }
        try
        {
            var outcome = await commands.InvokeAsync(
                Deserialize<CommandInvokeRequest>(request), invocation.Token).ConfigureAwait(false);
            if (outcome.Error is not null)
                await WriteErrorAsync(request.Id, outcome.Error.Code, outcome.Error.Message, invocation.Token).ConfigureAwait(false);
            else
                await WriteResultAsync(request.Id, outcome.Result!, invocation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!connectionToken.IsCancellationRequested)
        {
            var code = deadline.IsCancellationRequested
                ? IpcErrorCodes.Timeout
                : IpcErrorCodes.Cancelled;
            var message = code == IpcErrorCodes.Timeout
                ? "The request deadline elapsed."
                : "The command was cancelled.";
            await TryWriteErrorAsync(request.Id, code, message, connectionToken).ConfigureAwait(false);
        }
        finally
        {
            _invocations.TryRemove(request.Id, out _);
        }
    }

    private async Task CancelAsync(IpcEnvelope request, CancellationToken cancellationToken)
    {
        try
        {
            var cancel = Deserialize<CommandCancelRequest>(request);
            var accepted = _invocations.TryGetValue(cancel.RequestId, out var invocation);
            if (accepted) invocation!.Cancel();
            await WriteResultAsync(request.Id, new CommandCancelResponse(accepted), cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            await WriteErrorAsync(request.Id, IpcErrorCodes.InvalidRequest, "The request payload has an invalid shape.", cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool ValidateEnvelope(IpcEnvelope request, out string errorCode, out string error)
    {
        if (request.Protocol != IpcProtocol.Name)
        {
            errorCode = IpcErrorCodes.IncompatibleProtocol;
            error = "Unsupported IPC protocol.";
            return false;
        }
        if (request.Kind != "request" || !Guid.TryParse(request.Id, out _) || string.IsNullOrWhiteSpace(request.Method))
        {
            errorCode = IpcErrorCodes.InvalidRequest;
            error = "The request envelope is invalid.";
            return false;
        }
        try { _ = IpcProtocol.NormalizeDeadline(request.DeadlineMilliseconds); }
        catch (ArgumentOutOfRangeException)
        {
            errorCode = IpcErrorCodes.InvalidRequest;
            error = "The request deadline is outside the allowed range.";
            return false;
        }
        errorCode = string.Empty;
        error = string.Empty;
        return true;
    }

    private static T Deserialize<T>(IpcEnvelope envelope)
    {
        if (envelope.Payload is not JsonElement payload)
            throw new JsonException("Request payload is missing.");
        return payload.Deserialize<T>(IpcJson.Options)
               ?? throw new JsonException("Request payload is empty.");
    }

    private async Task WriteResultAsync<T>(string id, T result, CancellationToken cancellationToken)
        => await WriteAsync(new IpcEnvelope
        {
            Id = id,
            Kind = "response",
            Result = JsonSerializer.SerializeToElement(result, IpcJson.Options),
        }, cancellationToken).ConfigureAwait(false);

    private async Task WriteErrorAsync(string id, string code, string message, CancellationToken cancellationToken)
        => await WriteAsync(new IpcEnvelope
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id,
            Kind = "response",
            Error = new IpcError(code, message),
        }, cancellationToken).ConfigureAwait(false);

    private async Task TryWriteErrorAsync(string id, string code, string message, CancellationToken cancellationToken)
    {
        try { await WriteErrorAsync(id, code, message, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
    }

    private async Task WriteAsync(IpcEnvelope envelope, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LengthPrefixedJsonFraming.WriteAsync(stream, envelope, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
