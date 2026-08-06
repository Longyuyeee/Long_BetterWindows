using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using LongBetterWindows.PluginIpc.Contracts;
using LongBetterWindows.PluginIpc.Framing;

var options = WorkerOptions.Parse(args);
await using var pipe = new NamedPipeClientStream(
    ".",
    options.PipeName,
    PipeDirection.InOut,
    PipeOptions.Asynchronous);
await pipe.ConnectAsync(options.ConnectTimeoutMilliseconds);

var hello = IpcEnvelope.RequestForProtocol(
    PluginWorkerProtocol.Name,
    PluginWorkerProtocol.Hello,
    new PluginWorkerHelloRequest(
        options.PluginId,
        options.Nonce,
        Environment.ProcessId));
await LengthPrefixedJsonFraming.WriteAsync(pipe, hello);
var helloResponse = await LengthPrefixedJsonFraming.ReadAsync(pipe);
if (helloResponse.Protocol != PluginWorkerProtocol.Name
    || helloResponse.Kind != "response"
    || helloResponse.Id != hello.Id
    || helloResponse.Error is not null)
{
    return 4;
}

await new SyntheticWorkerServer(pipe).RunAsync();
return 0;

internal sealed class SyntheticWorkerServer(Stream stream)
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _commands =
        new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _stateLock = new();
    private string _state = "loaded";

    internal async Task RunAsync()
    {
        var requests = new HashSet<Task>();
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                IpcEnvelope request;
                try
                {
                    request = await LengthPrefixedJsonFraming.ReadAsync(
                        stream, _shutdown.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException)
                {
                    break;
                }

                if (!Validate(request))
                {
                    await WriteErrorAsync(
                        request.Id,
                        IpcErrorCodes.InvalidRequest,
                        "Worker request envelope is invalid.",
                        _shutdown.Token).ConfigureAwait(false);
                    continue;
                }

                if (request.Method == PluginWorkerProtocol.Shutdown)
                {
                    await WriteResultAsync(
                        request.Id,
                        new PluginWorkerShutdownResponse(true),
                        CancellationToken.None).ConfigureAwait(false);
                    _shutdown.Cancel();
                    break;
                }

                var task = ProcessAsync(request);
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
            _shutdown.Cancel();
            foreach (var command in _commands.Values)
                command.Cancel();
            Task[] pending;
            lock (requests) pending = requests.ToArray();
            try { await Task.WhenAll(pending).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
            foreach (var command in _commands.Values)
                command.Dispose();
            _commands.Clear();
            _writeGate.Dispose();
            _shutdown.Dispose();
        }
    }

    private async Task ProcessAsync(IpcEnvelope request)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        deadline.CancelAfter(IpcProtocol.NormalizeDeadline(request.DeadlineMilliseconds));
        try
        {
            switch (request.Method)
            {
                case PluginWorkerProtocol.LifecycleInvoke:
                    var lifecycle = Deserialize<PluginWorkerLifecycleRequest>(request);
                    var state = ApplyLifecycle(lifecycle);
                    await WriteResultAsync(
                        request.Id,
                        new PluginWorkerLifecycleResponse(state),
                        deadline.Token).ConfigureAwait(false);
                    break;
                case PluginWorkerProtocol.CommandInvoke:
                    await InvokeCommandAsync(request, deadline).ConfigureAwait(false);
                    break;
                case PluginWorkerProtocol.CommandCancel:
                    var cancel = Deserialize<PluginWorkerCancelRequest>(request);
                    var cancelled = _commands.TryGetValue(cancel.RequestId, out var invocation);
                    if (cancelled) invocation!.Cancel();
                    await WriteResultAsync(
                        request.Id,
                        new PluginWorkerCancelResponse(cancelled),
                        deadline.Token).ConfigureAwait(false);
                    break;
                default:
                    await WriteErrorAsync(
                        request.Id,
                        IpcErrorCodes.SurfaceNotSupported,
                        "Worker method is not supported.",
                        deadline.Token).ConfigureAwait(false);
                    break;
            }
        }
        catch (JsonException)
        {
            await TryWriteErrorAsync(
                request.Id,
                IpcErrorCodes.InvalidRequest,
                "Worker request payload is invalid.").ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!_shutdown.IsCancellationRequested)
        {
            await TryWriteErrorAsync(
                request.Id,
                IpcErrorCodes.Timeout,
                "Worker request deadline elapsed.").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not IOException)
        {
            await TryWriteErrorAsync(
                request.Id,
                IpcErrorCodes.InternalError,
                "Worker request failed.").ConfigureAwait(false);
        }
    }

    private async Task InvokeCommandAsync(
        IpcEnvelope request,
        CancellationTokenSource deadline)
    {
        var command = Deserialize<PluginWorkerCommandRequest>(request);
        if (!IsCommandStateValid())
        {
            await WriteErrorAsync(
                request.Id,
                IpcErrorCodes.InvalidRequest,
                "Worker must be running before commands can execute.",
                deadline.Token).ConfigureAwait(false);
            return;
        }

        using var invocation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        if (!_commands.TryAdd(request.Id, invocation))
        {
            await WriteErrorAsync(
                request.Id,
                IpcErrorCodes.InvalidRequest,
                "Worker command request id is already active.",
                deadline.Token).ConfigureAwait(false);
            return;
        }
        try
        {
            string? result;
            switch (command.Command)
            {
                case "echo":
                    result = command.Text;
                    break;
                case "delay":
                    ValidateDelay(command.DelayMilliseconds);
                    await Task.Delay(command.DelayMilliseconds, invocation.Token)
                        .ConfigureAwait(false);
                    result = command.Text;
                    break;
                case "burn":
                    ValidateDelay(command.DelayMilliseconds);
                    var stopwatch = Stopwatch.StartNew();
                    while (stopwatch.ElapsedMilliseconds < command.DelayMilliseconds)
                    {
                        invocation.Token.ThrowIfCancellationRequested();
                        Thread.SpinWait(10_000);
                    }
                    result = command.Text;
                    break;
                case "crash":
                    Environment.Exit(91);
                    return;
                default:
                    await WriteErrorAsync(
                        request.Id,
                        IpcErrorCodes.CommandNotFound,
                        "Synthetic worker command was not found.",
                        invocation.Token).ConfigureAwait(false);
                    return;
            }

            await WriteResultAsync(
                request.Id,
                new PluginWorkerCommandResponse(result, CurrentState()),
                invocation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var timedOut = deadline.IsCancellationRequested
                && !_shutdown.IsCancellationRequested;
            await TryWriteErrorAsync(
                request.Id,
                timedOut ? IpcErrorCodes.Timeout : IpcErrorCodes.Cancelled,
                timedOut
                    ? "Worker command deadline elapsed."
                    : "Worker command was cancelled.").ConfigureAwait(false);
        }
        finally
        {
            _commands.TryRemove(request.Id, out _);
        }
    }

    private string ApplyLifecycle(PluginWorkerLifecycleRequest request)
    {
        lock (_stateLock)
        {
            _state = request.Operation switch
            {
                PluginWorkerLifecycleOperation.Initialize when _state == "loaded" => "initialized",
                PluginWorkerLifecycleOperation.Start when _state is "initialized" or "stopped" => "running",
                PluginWorkerLifecycleOperation.Stop => "stopped",
                PluginWorkerLifecycleOperation.EnterBackground when _state == "running" => "background",
                PluginWorkerLifecycleOperation.Resume when _state == "background" => "running",
                PluginWorkerLifecycleOperation.ReleaseResources => "released",
                PluginWorkerLifecycleOperation.LanguageChanged when !string.IsNullOrWhiteSpace(request.Language) => _state,
                _ => throw new InvalidOperationException("Lifecycle operation is invalid for the current state."),
            };
            return _state;
        }
    }

    private bool IsCommandStateValid()
    {
        lock (_stateLock) return _state is "running" or "background";
    }

    private string CurrentState()
    {
        lock (_stateLock) return _state;
    }

    private async Task WriteResultAsync<T>(
        string id,
        T result,
        CancellationToken cancellationToken)
        => await WriteAsync(
            IpcEnvelope.Response(PluginWorkerProtocol.Name, id, result),
            cancellationToken).ConfigureAwait(false);

    private async Task WriteErrorAsync(
        string id,
        string code,
        string message,
        CancellationToken cancellationToken)
        => await WriteAsync(
            IpcEnvelope.Failure(
                PluginWorkerProtocol.Name,
                id,
                new IpcError(code, message)),
            cancellationToken).ConfigureAwait(false);

    private async Task TryWriteErrorAsync(string id, string code, string message)
    {
        try
        {
            await WriteErrorAsync(id, code, message, _shutdown.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
    }

    private async Task WriteAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LengthPrefixedJsonFraming.WriteAsync(
                stream, envelope, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static T Deserialize<T>(IpcEnvelope request)
    {
        if (request.Payload is not JsonElement payload)
            throw new JsonException("Worker request payload is missing.");
        return payload.Deserialize<T>(IpcJson.Options)
            ?? throw new JsonException("Worker request payload is empty.");
    }

    private static bool Validate(IpcEnvelope request)
        => request.Protocol == PluginWorkerProtocol.Name
            && request.Kind == "request"
            && !string.IsNullOrWhiteSpace(request.Id)
            && !string.IsNullOrWhiteSpace(request.Method);

    private static void ValidateDelay(int delayMilliseconds)
    {
        if (delayMilliseconds is < 0 or > 60_000)
            throw new ArgumentOutOfRangeException(nameof(delayMilliseconds));
    }
}

internal sealed record WorkerOptions(
    string PipeName,
    string Nonce,
    string PluginId,
    int ConnectTimeoutMilliseconds)
{
    internal static WorkerOptions Parse(string[] args)
    {
        string Read(string name)
        {
            var index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                throw new ArgumentException($"Missing required worker option: {name}");
            return args[index + 1];
        }

        return new WorkerOptions(
            Read("--pipe"),
            Read("--nonce"),
            Read("--plugin-id"),
            5_000);
    }
}
