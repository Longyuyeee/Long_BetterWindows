using System.Text.Json;
using System.Text.Json.Serialization;

namespace LongBetterWindows.PluginIpc.Contracts;

public sealed record IpcEnvelope
{
    [JsonPropertyName("protocol")]
    public string Protocol { get; init; } = IpcProtocol.Name;

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("method")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Method { get; init; }

    [JsonPropertyName("deadline_ms")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DeadlineMilliseconds { get; init; }

    [JsonPropertyName("payload")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Payload { get; init; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IpcError? Error { get; init; }

    public static IpcEnvelope Request<T>(
        string method,
        T payload,
        int? deadlineMilliseconds = null,
        string? id = null)
        => RequestForProtocol(
            IpcProtocol.Name,
            method,
            payload,
            deadlineMilliseconds,
            id);

    internal static IpcEnvelope RequestForProtocol<T>(
        string protocol,
        string method,
        T payload,
        int? deadlineMilliseconds = null,
        string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        return new IpcEnvelope
        {
            Protocol = protocol,
            Id = id ?? Guid.NewGuid().ToString(),
            Kind = "request",
            Method = method,
            DeadlineMilliseconds = IpcProtocol.NormalizeDeadline(deadlineMilliseconds),
            Payload = JsonSerializer.SerializeToElement(payload, IpcJson.Options)
        };
    }

    internal static IpcEnvelope Response<T>(
        string protocol,
        string id,
        T result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new IpcEnvelope
        {
            Protocol = protocol,
            Id = id,
            Kind = "response",
            Result = JsonSerializer.SerializeToElement(result, IpcJson.Options),
        };
    }

    internal static IpcEnvelope Failure(
        string protocol,
        string id,
        IpcError error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(error);
        return new IpcEnvelope
        {
            Protocol = protocol,
            Id = id,
            Kind = "response",
            Error = error,
        };
    }
}

public sealed record IpcError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("retryable")] bool Retryable = false);
