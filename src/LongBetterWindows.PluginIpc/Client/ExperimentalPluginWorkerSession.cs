using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LongBetterWindows.PluginIpc.Contracts;
using LongBetterWindows.PluginIpc.Framing;
using Microsoft.Win32.SafeHandles;

namespace LongBetterWindows.PluginIpc.Client;

internal sealed class ExperimentalPluginWorkerSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly NamedPipeServerStream _pipe;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcEnvelope>> _pending =
        new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _responseLoop;
    private bool _disposed;

    private ExperimentalPluginWorkerSession(
        string pluginId,
        Process process,
        NamedPipeServerStream pipe)
    {
        PluginId = pluginId;
        _process = process;
        _pipe = pipe;
    }

    public string PluginId { get; }
    public int ProcessId => _process.Id;
    public bool HasExited => _process.HasExited;

    public static async Task<ExperimentalPluginWorkerSession> StartAsync(
        string workerPath,
        string pluginId = "synthetic.headless.native",
        int connectTimeoutMilliseconds = 5_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        if (!File.Exists(workerPath))
            throw new FileNotFoundException("Plugin worker executable was not found.", workerPath);
        if (connectTimeoutMilliseconds is < 100 or > 30_000)
            throw new ArgumentOutOfRangeException(nameof(connectTimeoutMilliseconds));

        var pipeName = $"long-plugin-worker-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Process? process = null;
        try
        {
            process = Process.Start(CreateStartInfo(workerPath, pipeName, nonce, pluginId))
                ?? throw new InvalidOperationException("Plugin worker process could not be started.");
            var session = new ExperimentalPluginWorkerSession(pluginId, process, pipe);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(connectTimeoutMilliseconds);
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (process.HasExited)
                throw new PluginWorkerExitedException(process.ExitCode);

            var clientProcessId = PluginWorkerProcessIdentity.GetClientProcessId(pipe);
            var hello = await LengthPrefixedJsonFraming.ReadAsync(pipe, timeout.Token)
                .ConfigureAwait(false);
            if (!TryReadHello(hello, out var request)
                || !PluginWorkerHandshake.IsValid(
                    pluginId,
                    nonce,
                    process.Id,
                    request!,
                    clientProcessId))
            {
                throw new UnauthorizedAccessException(
                    "Plugin worker process identity or nonce did not match the spawned worker.");
            }

            await LengthPrefixedJsonFraming.WriteAsync(
                pipe,
                IpcEnvelope.Response(
                    PluginWorkerProtocol.Name,
                    hello.Id,
                    new PluginWorkerHelloResponse(
                        PluginWorkerProtocol.Name,
                        pluginId,
                        PluginWorkerProtocol.Features,
                        IpcProtocol.MaximumFrameBytes,
                        IpcProtocol.MaximumDeadlineMilliseconds)),
                timeout.Token).ConfigureAwait(false);
            session._responseLoop = session.ReadResponsesAsync(session._shutdown.Token);
            return session;
        }
        catch
        {
            pipe.Dispose();
            if (process is not null)
            {
                TryTerminate(process);
                process.Dispose();
            }
            throw;
        }
    }

    public Task<PluginWorkerLifecycleResponse> InvokeLifecycleAsync(
        PluginWorkerLifecycleOperation operation,
        string? language = null,
        int? deadlineMilliseconds = null,
        CancellationToken cancellationToken = default)
        => RequestAsync<PluginWorkerLifecycleRequest, PluginWorkerLifecycleResponse>(
            PluginWorkerProtocol.LifecycleInvoke,
            new PluginWorkerLifecycleRequest(operation, language),
            deadlineMilliseconds,
            cancellationToken: cancellationToken);

    public Task<PluginWorkerCommandResponse> InvokeCommandAsync(
        PluginWorkerCommandRequest request,
        int? deadlineMilliseconds = null,
        CancellationToken cancellationToken = default)
        => InvokeCommandWithIdAsync(
            Guid.NewGuid().ToString(),
            request,
            deadlineMilliseconds,
            cancellationToken);

    public Task<PluginWorkerCommandResponse> InvokeCommandWithIdAsync(
        string requestId,
        PluginWorkerCommandRequest request,
        int? deadlineMilliseconds = null,
        CancellationToken cancellationToken = default)
        => RequestAsync<PluginWorkerCommandRequest, PluginWorkerCommandResponse>(
            PluginWorkerProtocol.CommandInvoke,
            request,
            deadlineMilliseconds,
            requestId,
            cancellationToken);

    public Task<PluginWorkerCancelResponse> CancelCommandAsync(
        string requestId,
        CancellationToken cancellationToken = default)
        => RequestAsync<PluginWorkerCancelRequest, PluginWorkerCancelResponse>(
            PluginWorkerProtocol.CommandCancel,
            new PluginWorkerCancelRequest(requestId),
            cancellationToken: cancellationToken);

    public PluginWorkerResourceSnapshot CaptureResourceSnapshot()
    {
        try
        {
            _process.Refresh();
            if (_process.HasExited)
                return new PluginWorkerResourceSnapshot(
                    _process.Id, true, 0, 0, DateTimeOffset.UtcNow);
            return new PluginWorkerResourceSnapshot(
                _process.Id,
                false,
                _process.WorkingSet64,
                _process.TotalProcessorTime.TotalMilliseconds,
                DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException)
        {
            return new PluginWorkerResourceSnapshot(
                _process.Id, true, 0, 0, DateTimeOffset.UtcNow);
        }
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken = default)
        => await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (!_process.HasExited && _pipe.IsConnected)
        {
            using var timeout = new CancellationTokenSource(2_000);
            try
            {
                await RequestAsync<PluginWorkerShutdownRequest, PluginWorkerShutdownResponse>(
                    PluginWorkerProtocol.Shutdown,
                    new PluginWorkerShutdownRequest(),
                    1_000,
                    cancellationToken: timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException
                or OperationCanceledException
                or PluginWorkerExitedException) { }
        }

        _disposed = true;
        _shutdown.Cancel();
        await _pipe.DisposeAsync().ConfigureAwait(false);
        if (_responseLoop is not null)
        {
            try { await _responseLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        if (!_process.HasExited)
        {
            using var timeout = new CancellationTokenSource(2_000);
            try { await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { TryTerminate(_process); }
        }
        FailPending(new ObjectDisposedException(nameof(ExperimentalPluginWorkerSession)));
        _process.Dispose();
        _writeGate.Dispose();
        _shutdown.Dispose();
    }

    private async Task<TResponse> RequestAsync<TRequest, TResponse>(
        string method,
        TRequest payload,
        int? deadlineMilliseconds = null,
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        var request = IpcEnvelope.RequestForProtocol(
            PluginWorkerProtocol.Name,
            method,
            payload,
            deadlineMilliseconds,
            requestId);
        var completion = new TaskCompletionSource<IpcEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(request.Id, completion))
            throw new InvalidOperationException("A plugin worker request with this id is already pending.");
        var sent = false;
        try
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await LengthPrefixedJsonFraming.WriteAsync(
                    _pipe, request, cancellationToken).ConfigureAwait(false);
                sent = true;
            }
            finally
            {
                _writeGate.Release();
            }

            var response = await completion.Task.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (response.Error is not null)
            {
                throw new IpcRemoteException(
                    IpcErrorCodes.Normalize(response.Error.Code),
                    response.Error.Message,
                    response.Error.Retryable);
            }
            if (response.Result is not JsonElement result)
                throw new InvalidDataException("Plugin worker response does not contain a result.");
            return result.Deserialize<TResponse>(IpcJson.Options)
                ?? throw new InvalidDataException("Plugin worker response result is empty.");
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
                    _pipe, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(
                        response.Protocol,
                        PluginWorkerProtocol.Name,
                        StringComparison.Ordinal)
                    || response.Kind != "response"
                    || !_pending.TryRemove(response.Id, out var completion))
                {
                    throw new InvalidDataException(
                        "Plugin worker returned an unmatched response envelope.");
                }
                completion.TrySetResult(response);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
        {
            try { _process.WaitForExit(1_000); }
            catch (InvalidOperationException) { }
            FailPending(new PluginWorkerExitedException(
                _process.HasExited ? _process.ExitCode : null, ex));
        }
        catch (Exception ex)
        {
            FailPending(ex);
        }
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process.HasExited)
            throw new PluginWorkerExitedException(_process.ExitCode);
        if (!_pipe.IsConnected)
            throw new IOException("Plugin worker pipe is disconnected.");
    }

    private void FailPending(Exception exception)
    {
        foreach (var completion in _pending.Values)
            completion.TrySetException(exception);
        _pending.Clear();
    }

    private static bool TryReadHello(
        IpcEnvelope envelope,
        out PluginWorkerHelloRequest? request)
    {
        request = null;
        if (envelope.Protocol != PluginWorkerProtocol.Name
            || envelope.Kind != "request"
            || envelope.Method != PluginWorkerProtocol.Hello
            || envelope.Payload is not JsonElement payload)
            return false;
        try
        {
            request = payload.Deserialize<PluginWorkerHelloRequest>(IpcJson.Options);
            return request is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        string workerPath,
        string pipeName,
        string nonce,
        string pluginId)
    {
        var isDll = string.Equals(Path.GetExtension(workerPath), ".dll", StringComparison.OrdinalIgnoreCase);
        var start = new ProcessStartInfo
        {
            FileName = isDll
                ? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet"
                : Path.GetFullPath(workerPath),
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(workerPath))!,
        };
        if (isDll) start.ArgumentList.Add(Path.GetFullPath(workerPath));
        start.ArgumentList.Add("--pipe");
        start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add("--nonce");
        start.ArgumentList.Add(nonce);
        start.ArgumentList.Add("--plugin-id");
        start.ArgumentList.Add(pluginId);
        return start;
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}

internal static class PluginWorkerHandshake
{
    public static bool IsValid(
        string expectedPluginId,
        string expectedNonce,
        int expectedProcessId,
        PluginWorkerHelloRequest request,
        int actualProcessId)
    {
        ArgumentNullException.ThrowIfNull(request);
        return expectedProcessId == actualProcessId
            && request.ProcessId == expectedProcessId
            && string.Equals(request.PluginId, expectedPluginId, StringComparison.Ordinal)
            && FixedTimeEquals(request.Nonce, expectedNonce);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

internal sealed class PluginWorkerExitedException : IOException
{
    public PluginWorkerExitedException(int? exitCode, Exception? innerException = null)
        : base(
            exitCode is null
                ? "Plugin worker process disconnected unexpectedly."
                : $"Plugin worker process exited with code {exitCode}.",
            innerException)
    {
        ExitCode = exitCode;
    }

    public int? ExitCode { get; }
}

internal static class PluginWorkerProcessIdentity
{
    internal static int GetClientProcessId(NamedPipeServerStream pipe)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var processId))
            throw new UnauthorizedAccessException("Plugin worker process identity is unavailable.");
        return checked((int)processId);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);
}
