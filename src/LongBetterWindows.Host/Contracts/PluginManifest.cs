using System.Text.Json.Serialization;

namespace LongBetterWindows.Host.Contracts
{
    public class PluginManifest
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("author")]
        public string Author { get; init; } = string.Empty;

        [JsonPropertyName("entry_point")]
        public string EntryPoint { get; init; } = string.Empty;

        [JsonPropertyName("capabilities")]
        public List<string> Capabilities { get; init; } = new();

        [JsonPropertyName("runtime")]
        public string? Runtime { get; init; }

        [JsonPropertyName("min_api_version")]
        public string? MinApiVersion { get; init; }

        [JsonPropertyName("default_settings")]
        public Dictionary<string, object>? DefaultSettings { get; init; }
    }
}
