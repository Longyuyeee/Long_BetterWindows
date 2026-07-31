using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using LongBetterWindows.PluginIpc.Contracts;
using LongBetterWindows.PluginIpc.Framing;

namespace LongBetterWindows.PluginIpc.Client;

public sealed class LongPluginBrokerClient : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcEnvelope>> _pending =
        new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private NamedPipeClientStream? _pipe;
    private Task? _responseLoop;
    private bool _helloCompleted;

    public LongPluginBrokerClient(string? pipeName = null)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? BrokerPipeName.ForCurrentUser()
            : pipeName;
    }

    public bool IsConnected => _pipe?.IsConnected == true && _helloCompleted;

    public async Task<HostHelloResponse> ConnectAsync(
        HostHelloRequest hello,
        int timeoutMilliseconds = 5_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hello);
        if (_pipe is not null)
            throw new InvalidOperationException("The broker client has already been connected.");

        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(timeoutMilliseconds, cancellationToken).ConfigureAwait(false);
            _pipe = pipe;
            _responseLoop = ReadResponsesAsync(_shutdown.Token);
            var response = await SendCoreAsync(IpcEnvelope.Request(BrokerMethods.HostHello, hello), cancellationToken).ConfigureAwait(false);
            var negotiated = DeserializeResult<HostHelloResponse>(response);
            if (!string.Equals(negotiated.Protocol, IpcProtocol.Name, StringComparison.Ordinal))
                throw new IpcRemoteException(IpcErrorCodes.IncompatibleProtocol, $"Broker selected unsupported protocol '{negotiated.Protocol}'.", false);
            _helloCompleted = true;
            return negotiated;
        }
        catch
        {
            _shutdown.Cancel();
            pipe.Dispose();
            _pipe = null;
            throw;
        }
    }

    public Task<TResponse> RequestAsync<TRequest, TResponse>(
        string method,
        TRequest payload,
        int? deadlineMilliseconds = null,
        CancellationToken cancellationToken = default)
        => RequestWithIdAsync<TRequest, TResponse>(
            Guid.NewGuid().ToString(), method, payload, deadlineMilliseconds, cancellationToken);

    public async Task<TResponse> RequestWithIdAsync<TRequest, TResponse>(
        string requestId,
        string method,
        TRequest payload,
        int? deadlineMilliseconds = null,
        CancellationToken cancellationToken = default)
    {
        if (!_helloCompleted)
            throw new InvalidOperationException("host.hello must complete before broker requests.");
        if (!Guid.TryParse(requestId, out _))
            throw new ArgumentException("Request id must be a GUID.", nameof(requestId));

        var response = await SendCoreAsync(
            IpcEnvelope.Request(method, payload, deadlineMilliseconds, requestId),
            cancellationToken).ConfigureAwait(false);
        return DeserializeResult<TResponse>(response);
    }

    public Task<CommandCancelResponse> CancelCommandAsync(
        string requestId,
        CancellationToken cancellationToken = default)
        => RequestAsync<CommandCancelRequest, CommandCancelResponse>(
            BrokerMethods.CommandCancel,
            new CommandCancelRequest(requestId),
            cancellationToken: cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _helloCompleted = false;
        _shutdown.Cancel();
        if (_pipe is not null)
        {
            await _pipe.DisposeAsync().ConfigureAwait(false);
            _pipe = null;
        }
        if (_responseLoop is not null)
        {
            try { await _responseLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        FailPending(new ObjectDisposedException(nameof(LongPluginBrokerClient)));
        _writeGate.Dispose();
        _shutdown.Dispose();
    }

    private async Task<IpcEnvelope> SendCoreAsync(IpcEnvelope request, CancellationToken cancellationToken)
    {
        var pipe = _pipe ?? throw new InvalidOperationException("Broker pipe is not connected.");
        var completion = new TaskCompletionSource<IpcEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(request.Id, completion))
            throw new InvalidOperationException("A request with this id is already pending.");

        var sent = false;
        try
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await LengthPrefixedJsonFraming.WriteAsync(pipe, request, cancellationToken).ConfigureAwait(false);
                sent = true;
            }
            finally
            {
                _writeGate.Release();
            }

            var response = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (response.Error is not null)
                throw new IpcRemoteException(IpcErrorCodes.Normalize(response.Error.Code), response.Error.Message, response.Error.Retryable);
            return response;
        }
        finally
        {
            if (!sent || completion.Task.IsCompleted)
                _pending.TryRemove(request.Id, out _);
        }
    }

    private async Task ReadResponsesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var response = await LengthPrefixedJsonFraming.ReadAsync(
                    _pipe ?? throw new EndOfStreamException(), cancellationToken).ConfigureAwait(false);
                if (!string.Equals(response.Protocol, IpcProtocol.Name, StringComparison.Ordinal)
                    || !string.Equals(response.Kind, "response", StringComparison.Ordinal)
                    || !_pending.TryRemove(response.Id, out var completion))
                    throw new InvalidDataException("Broker returned an unmatched IPC response envelope.");
                completion.TrySetResult(response);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            FailPending(ex);
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var completion in _pending.Values)
            completion.TrySetException(exception);
    }

    private static T DeserializeResult<T>(IpcEnvelope response)
    {
        if (response.Result is not JsonElement result)
            throw new InvalidDataException("Broker response does not contain a result.");
        try
        {
            return result.Deserialize<T>(IpcJson.Options)
                ?? throw new InvalidDataException("Broker response result is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Broker response result has an invalid shape.", ex);
        }
    }
}

public sealed class IpcRemoteException(string code, string message, bool retryable) : Exception(message)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}
