using System.Text.Json.Serialization;

namespace LongBetterWindows.PluginIpc.Contracts;

public sealed record HostHelloRequest(
    [property: JsonPropertyName("client_name")] string ClientName,
    [property: JsonPropertyName("client_version")] string ClientVersion,
    [property: JsonPropertyName("protocols")] IReadOnlyList<string> Protocols,
    [property: JsonPropertyName("features")] IReadOnlyList<string> Features);

public sealed record HostHelloResponse(
    [property: JsonPropertyName("host_name")] string HostName,
    [property: JsonPropertyName("host_version")] string HostVersion,
    [property: JsonPropertyName("protocol")] string Protocol,
    [property: JsonPropertyName("features")] IReadOnlyList<string> Features,
    [property: JsonPropertyName("maximum_frame_bytes")] int MaximumFrameBytes,
    [property: JsonPropertyName("maximum_deadline_ms")] int MaximumDeadlineMilliseconds);
