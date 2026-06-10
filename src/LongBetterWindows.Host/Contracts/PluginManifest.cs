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

        [JsonPropertyName("default_settings")]
        public Dictionary<string, object>? DefaultSettings { get; init; }
    }
}
