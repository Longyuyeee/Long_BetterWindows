using System.Text.Json.Serialization;

namespace LongBetterWindows.PluginIpc.Contracts;

internal static class PluginWorkerProtocol
{
    public const string Name = "long.plugin.worker/experimental-1";
    public const string Hello = "worker.hello";
    public const string LifecycleInvoke = "worker.lifecycle.invoke";
    public const string CommandInvoke = "worker.command.invoke";
    public const string CommandCancel = "worker.command.cancel";
    public const string Shutdown = "worker.shutdown";

    public static IReadOnlyList<string> Features { get; } =
    [
        LifecycleInvoke,
        CommandInvoke,
        CommandCancel,
        Shutdown,
    ];
}

internal enum PluginWorkerLifecycleOperation
{
    Initialize,
    Start,
    Stop,
    EnterBackground,
    Resume,
    ReleaseResources,
    LanguageChanged,
}

internal sealed record PluginWorkerHelloRequest(
    [property: JsonPropertyName("plugin_id")] string PluginId,
    [property: JsonPropertyName("nonce")] string Nonce,
    [property: JsonPropertyName("process_id")] int ProcessId);

internal sealed record PluginWorkerHelloResponse(
    [property: JsonPropertyName("protocol")] string Protocol,
    [property: JsonPropertyName("plugin_id")] string PluginId,
    [property: JsonPropertyName("features")] IReadOnlyList<string> Features,
    [property: JsonPropertyName("maximum_frame_bytes")] int MaximumFrameBytes,
    [property: JsonPropertyName("maximum_deadline_ms")] int MaximumDeadlineMilliseconds);

internal sealed record PluginWorkerLifecycleRequest(
    [property: JsonPropertyName("operation")] PluginWorkerLifecycleOperation Operation,
    [property: JsonPropertyName("language")] string? Language = null);

internal sealed record PluginWorkerLifecycleResponse(
    [property: JsonPropertyName("state")] string State);

internal sealed record PluginWorkerCommandRequest(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("delay_ms")] int DelayMilliseconds = 0);

internal sealed record PluginWorkerCommandResponse(
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("state")] string State);

internal sealed record PluginWorkerCancelRequest(
    [property: JsonPropertyName("request_id")] string RequestId);

internal sealed record PluginWorkerCancelResponse(
    [property: JsonPropertyName("cancelled")] bool Cancelled);

internal sealed record PluginWorkerShutdownRequest;

internal sealed record PluginWorkerShutdownResponse(
    [property: JsonPropertyName("accepted")] bool Accepted);

internal sealed record PluginWorkerResourceSnapshot(
    int ProcessId,
    bool HasExited,
    long WorkingSetBytes,
    double TotalProcessorMilliseconds,
    DateTimeOffset ObservedAt);
