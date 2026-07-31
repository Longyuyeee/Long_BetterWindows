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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        return new IpcEnvelope
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Kind = "request",
            Method = method,
            DeadlineMilliseconds = IpcProtocol.NormalizeDeadline(deadlineMilliseconds),
            Payload = JsonSerializer.SerializeToElement(payload, IpcJson.Options)
        };
    }
}

public sealed record IpcError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("retryable")] bool Retryable = false);
