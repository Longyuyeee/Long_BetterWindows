using System.IO.Pipes;
using System.Text.Json;
using LongBetterWindows.PluginIpc.Contracts;
using LongBetterWindows.PluginIpc.Framing;

namespace LongBetterWindows.PluginIpc.Client;

public sealed class LongPluginBrokerClient : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private NamedPipeClientStream? _pipe;
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
        {
            throw new InvalidOperationException("The broker client has already been connected.");
        }

        var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(timeoutMilliseconds, cancellationToken).ConfigureAwait(false);
            _pipe = pipe;
            var response = await SendCoreAsync(
                IpcEnvelope.Request("host.hello", hello),
                cancellationToken).ConfigureAwait(false);
            var negotiated = DeserializeResult<HostHelloResponse>(response);
            if (!string.Equals(negotiated.Protocol, IpcProtocol.Name, StringComparison.Ordinal))
            {
                throw new IpcRemoteException(
                    IpcErrorCodes.IncompatibleProtocol,
                    $"Broker selected unsupported protocol '{negotiated.Protocol}'.",
                    false);
            }

            _helloCompleted = true;
            return negotiated;
        }
        catch
        {
            pipe.Dispose();
            _pipe = null;
            throw;
        }
    }

    public async Task<TResponse> RequestAsync<TRequest, TResponse>(
        string method,
        TRequest payload,
        int? deadlineMilliseconds = null,
        CancellationToken cancellationToken = default)
    {
        if (!_helloCompleted)
        {
            throw new InvalidOperationException("host.hello must complete before broker requests.");
        }

        var response = await SendCoreAsync(
            IpcEnvelope.Request(method, payload, deadlineMilliseconds),
            cancellationToken).ConfigureAwait(false);
        return DeserializeResult<TResponse>(response);
    }

    public async ValueTask DisposeAsync()
    {
        _helloCompleted = false;
        if (_pipe is not null)
        {
            await _pipe.DisposeAsync().ConfigureAwait(false);
            _pipe = null;
        }

        _requestGate.Dispose();
    }

    private async Task<IpcEnvelope> SendCoreAsync(
        IpcEnvelope request,
        CancellationToken cancellationToken)
    {
        var pipe = _pipe ?? throw new InvalidOperationException("Broker pipe is not connected.");
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LengthPrefixedJsonFraming.WriteAsync(pipe, request, cancellationToken)
                .ConfigureAwait(false);
            var response = await LengthPrefixedJsonFraming.ReadAsync(pipe, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(response.Protocol, IpcProtocol.Name, StringComparison.Ordinal)
                || !string.Equals(response.Kind, "response", StringComparison.Ordinal)
                || !string.Equals(response.Id, request.Id, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Broker returned a mismatched IPC response envelope.");
            }

            if (response.Error is not null)
            {
                throw new IpcRemoteException(
                    IpcErrorCodes.Normalize(response.Error.Code),
                    response.Error.Message,
                    response.Error.Retryable);
            }

            return response;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static T DeserializeResult<T>(IpcEnvelope response)
    {
        if (response.Result is not JsonElement result)
        {
            throw new InvalidDataException("Broker response does not contain a result.");
        }

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

public sealed class IpcRemoteException(
    string code,
    string message,
    bool retryable) : Exception(message)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}
