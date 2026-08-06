using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.Loader;
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

var workload = options.WorkloadPath is null
    ? null
    : WorkerWorkloadLoader.Load(options.WorkloadPath, options.PluginId);
await new WorkerServer(pipe, workload).RunAsync();
return 0;

internal sealed class WorkerServer(
    Stream stream,
    IPluginWorkerWorkload? workload)
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _commands =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcEnvelope>> _hostRequests =
        new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
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

                if (request.Protocol == PluginWorkerProtocol.Name
                    && request.Kind == "response"
                    && _hostRequests.TryRemove(request.Id, out var hostRequest))
                {
                    hostRequest.TrySetResult(request);
                    continue;
                }

                if (!ValidateRequest(request))
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
            foreach (var request in _hostRequests.Values)
                request.TrySetCanceled();
            Task[] pending;
            lock (requests) pending = requests.ToArray();
            try { await Task.WhenAll(pending).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
            foreach (var command in _commands.Values)
                command.Dispose();
            _commands.Clear();
            if (workload is not null)
                await workload.DisposeAsync().ConfigureAwait(false);
            _lifecycleGate.Dispose();
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
                    var state = await ApplyLifecycleAsync(lifecycle, deadline.Token)
                        .ConfigureAwait(false);
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
            if (workload is not null)
            {
                if (!workload.Commands.Contains(command.Command))
                {
                    await WriteErrorAsync(
                        request.Id,
                        IpcErrorCodes.CommandNotFound,
                        "Loaded workload command was not found.",
                        invocation.Token).ConfigureAwait(false);
                    return;
                }
                result = await workload.InvokeCommandAsync(command, invocation.Token)
                    .ConfigureAwait(false);
            }
            else
            {
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
                    case "query-capability":
                        if (string.IsNullOrWhiteSpace(command.Text))
                            throw new ArgumentException("Capability name is required.");
                        var capability = await QueryHostCapabilityAsync(
                            command.Text,
                            command.DelayMilliseconds > 0 ? command.DelayMilliseconds : null,
                            invocation.Token).ConfigureAwait(false);
                        result = capability.Granted ? "granted" : "denied";
                        break;
                    default:
                        await WriteErrorAsync(
                            request.Id,
                            IpcErrorCodes.CommandNotFound,
                            "Synthetic worker command was not found.",
                            invocation.Token).ConfigureAwait(false);
                        return;
                }
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
        catch (WorkerHostRequestException ex)
        {
            await TryWriteErrorAsync(
                request.Id,
                IpcErrorCodes.Normalize(ex.Code),
                "Worker host request failed.").ConfigureAwait(false);
        }
        finally
        {
            _commands.TryRemove(request.Id, out _);
        }
    }

    private async Task<string> ApplyLifecycleAsync(
        PluginWorkerLifecycleRequest request,
        CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string nextState;
            lock (_stateLock)
            {
                nextState = request.Operation switch
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
            }

            if (workload is not null)
                await workload.InvokeLifecycleAsync(
                    request.Operation, request.Language, cancellationToken).ConfigureAwait(false);
            lock (_stateLock) _state = nextState;
            return nextState;
        }
        finally
        {
            _lifecycleGate.Release();
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

    private async Task<PluginWorkerCapabilityQueryResponse> QueryHostCapabilityAsync(
        string capability,
        int? deadlineMilliseconds,
        CancellationToken cancellationToken)
    {
        var request = IpcEnvelope.RequestForProtocol(
            PluginWorkerProtocol.Name,
            PluginWorkerProtocol.HostCapabilityQuery,
            new PluginWorkerCapabilityQueryRequest(capability),
            deadlineMilliseconds);
        var completion = new TaskCompletionSource<IpcEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_hostRequests.TryAdd(request.Id, completion))
            throw new InvalidOperationException("A duplicate host request id was generated.");
        try
        {
            await WriteAsync(request, cancellationToken).ConfigureAwait(false);
            var response = await completion.Task.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (response.Error is not null)
                throw new WorkerHostRequestException(response.Error.Code);
            if (response.Result is not JsonElement result)
                throw new InvalidDataException("Host capability response is missing.");
            return result.Deserialize<PluginWorkerCapabilityQueryResponse>(IpcJson.Options)
                ?? throw new InvalidDataException("Host capability response is empty.");
        }
        finally
        {
            _hostRequests.TryRemove(request.Id, out _);
        }
    }

    private static bool ValidateRequest(IpcEnvelope request)
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

internal sealed class WorkerHostRequestException(string code) : Exception
{
    internal string Code { get; } = code;
}

internal sealed record WorkerOptions(
    string PipeName,
    string Nonce,
    string PluginId,
    string? WorkloadPath,
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
            ReadOptional("--workload"),
            5_000);

        string? ReadOptional(string name)
        {
            var index = Array.IndexOf(args, name);
            if (index < 0) return null;
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                throw new ArgumentException($"Missing worker option value: {name}");
            return args[index + 1];
        }
    }
}

internal static class WorkerWorkloadLoader
{
    internal static IPluginWorkerWorkload Load(string assemblyPath, string pluginId)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Worker workload assembly was not found.", fullPath);

        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
        var types = assembly.GetTypes()
            .Where(type => typeof(IPluginWorkerWorkload).IsAssignableFrom(type)
                && type is { IsAbstract: false, IsInterface: false })
            .ToArray();
        if (types.Length != 1)
            throw new InvalidDataException(
                "Worker workload assembly must contain exactly one workload implementation.");

        var workload = Activator.CreateInstance(types[0], nonPublic: true)
            as IPluginWorkerWorkload
            ?? throw new InvalidDataException("Worker workload could not be created.");
        if (!string.Equals(workload.PluginId, pluginId, StringComparison.Ordinal))
        {
            workload.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw new InvalidDataException("Worker workload identity does not match the session.");
        }
        if (workload.Commands.Count == 0
            || workload.Commands.Any(string.IsNullOrWhiteSpace))
        {
            workload.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw new InvalidDataException("Worker workload command set is invalid.");
        }
        return workload;
    }
}
