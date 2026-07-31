using System.Text.Json.Serialization;

namespace LongBetterWindows.PluginIpc.Contracts;

public static class BrokerMethods
{
    public const string HostHello = "host.hello";
    public const string HealthPing = "health.ping";
    public const string PluginCatalogList = "plugin.catalog.list";
    public const string PluginCatalogGet = "plugin.catalog.get";
    public const string CommandInvoke = "command.invoke";
    public const string CommandCancel = "command.cancel";
    public const string PluginOpen = "plugin.open";
}

public sealed record HealthPingRequest(
    [property: JsonPropertyName("nonce")] string? Nonce = null);

public sealed record HealthPingResponse(
    [property: JsonPropertyName("nonce")] string? Nonce,
    [property: JsonPropertyName("timestamp_utc")] DateTimeOffset TimestampUtc);

public sealed record PluginCatalogListRequest(
    [property: JsonPropertyName("if_revision")] long? IfRevision = null);

public sealed record PluginCatalogGetRequest(
    [property: JsonPropertyName("plugin_id")] string PluginId);

public sealed record PluginCatalogListResponse(
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("not_modified")] bool NotModified,
    [property: JsonPropertyName("plugins")] IReadOnlyList<PluginCatalogItem> Plugins);

public sealed record PluginCatalogGetResponse(
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("plugin")] PluginCatalogItem Plugin);

public sealed record PluginCatalogItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("runtime")] string Runtime,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("min_api_version")] string? MinApiVersion,
    [property: JsonPropertyName("min_host_version")] string? MinHostVersion,
    [property: JsonPropertyName("commands")] IReadOnlyList<PluginCatalogCommand> Commands,
    [property: JsonPropertyName("widgets")] IReadOnlyList<PluginCatalogWidget> Widgets);

public sealed record PluginCatalogCommand(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("accepted_inputs")] IReadOnlyList<string> AcceptedInputs);

public sealed record PluginCatalogWidget(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("multiple_instances")] bool MultipleInstances);

public sealed record CommandInvokeRequest(
    [property: JsonPropertyName("plugin_id")] string PluginId,
    [property: JsonPropertyName("command_id")] string CommandId,
    [property: JsonPropertyName("arguments")] IReadOnlyDictionary<string, string> Arguments,
    [property: JsonPropertyName("input_type")] string InputType = "none",
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("paths")] IReadOnlyList<string>? Paths = null,
    [property: JsonPropertyName("image_png")] byte[]? ImagePng = null);

public sealed record CommandInvokeResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("keep_palette_open")] bool KeepPaletteOpen,
    [property: JsonPropertyName("outputs")] IReadOnlyDictionary<string, CommandOutput> Outputs);

public sealed record CommandOutput(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] string Value);

public sealed record CommandCancelRequest(
    [property: JsonPropertyName("request_id")] string RequestId);

public sealed record CommandCancelResponse(
    [property: JsonPropertyName("accepted")] bool Accepted);

public sealed record PluginOpenRequest(
    [property: JsonPropertyName("plugin_id")] string PluginId);

public sealed record PluginOpenResponse(
    [property: JsonPropertyName("status")] string Status);
