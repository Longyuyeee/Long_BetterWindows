using System.Collections.Concurrent;
using System.Text;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.Host.Broker;

internal sealed class PluginCommandEndpoint
{
    private const int MaximumConcurrentCommandsPerPlugin = 4;
    private const int MaximumOutputBytes = 512 * 1024;
    private readonly PluginRegistry _registry;
    private readonly CommandExecutor _executor;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _pluginGates =
        new(StringComparer.OrdinalIgnoreCase);

    public PluginCommandEndpoint(PluginRegistry registry)
    {
        _registry = registry;
        _executor = new CommandExecutor(registry);
    }

    public async Task<CommandInvocationOutcome> InvokeAsync(
        CommandInvokeRequest request,
        CancellationToken cancellationToken)
    {
        var entry = _registry.Get(request.PluginId);
        if (entry is null)
            return CommandInvocationOutcome.Failure(IpcErrorCodes.PluginNotFound, "The requested plugin is not installed.");

        var commandKey = request.PluginId + ":" + request.CommandId;
        if (_registry.Commands.Get(commandKey) is null)
            return CommandInvocationOutcome.Failure(IpcErrorCodes.CommandNotFound, "The requested command is not registered.");

        if (!Enum.TryParse<AcceptedInputType>(request.InputType, true, out var inputType)
            || !Enum.IsDefined(inputType))
            return CommandInvocationOutcome.Failure(IpcErrorCodes.InvalidRequest, "The input_type value is invalid.");

        var gate = _pluginGates.GetOrAdd(
            request.PluginId,
            _ => new SemaphoreSlim(MaximumConcurrentCommandsPerPlugin, MaximumConcurrentCommandsPerPlugin));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _executor.ExecuteAsync(commandKey, new PluginCommandInvocation
            {
                CommandId = request.CommandId,
                InputType = inputType,
                Text = request.Text,
                Paths = request.Paths ?? Array.Empty<string>(),
                ImagePng = request.ImagePng,
                Arguments = request.Arguments ?? new Dictionary<string, string>(),
            }, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var outputSize = result.Outputs.Sum(item =>
                Encoding.UTF8.GetByteCount(item.Key) + Encoding.UTF8.GetByteCount(item.Value.Value));
            if (outputSize > MaximumOutputBytes)
                return CommandInvocationOutcome.Failure(IpcErrorCodes.InvalidRequest, "The command result exceeds the broker output limit.");

            return CommandInvocationOutcome.Success(new CommandInvokeResponse(
                result.IsSuccess ? "completed" : "failed",
                result.Message,
                result.KeepPaletteOpen,
                result.Outputs.ToDictionary(
                    item => item.Key,
                    item => new CommandOutput(
                        item.Value.Type.ToString().ToLowerInvariant(),
                        item.Value.Value),
                    StringComparer.Ordinal)));
        }
        finally
        {
            gate.Release();
        }
    }
}

internal sealed record CommandInvocationOutcome(
    CommandInvokeResponse? Result,
    IpcError? Error)
{
    public static CommandInvocationOutcome Success(CommandInvokeResponse result) => new(result, null);
    public static CommandInvocationOutcome Failure(string code, string message) =>
        new(null, new IpcError(code, message));
}
