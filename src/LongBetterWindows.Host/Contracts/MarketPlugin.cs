using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LongBetterWindows.Host.Contracts
{
    /// <summary>
    /// 插件市场插件信息
    /// </summary>
    public class MarketPlugin
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; set; }

        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("downloads")]
        public int Downloads { get; set; }

        [JsonPropertyName("rating")]
        public double Rating { get; set; }

        [JsonPropertyName("capabilities")]
        public List<string> Capabilities { get; set; } = new();

        [JsonPropertyName("dependencies")]
        public List<PluginDependency> Dependencies { get; set; } = new();

        [JsonPropertyName("screenshots")]
        public List<string> Screenshots { get; set; } = new();

        [JsonPropertyName("changelog")]
        public string? Changelog { get; set; }

        [JsonPropertyName("size_bytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = string.Empty;

        [JsonPropertyName("runtime")]
        public string? Runtime { get; set; }

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("min_api_version")]
        public string? MinApiVersion { get; set; }
    }

    /// <summary>
    /// 插件市场数据容器
    /// </summary>
    public class MarketData
    {
        [JsonPropertyName("plugins")]
        public List<MarketPlugin> Plugins { get; set; } = new();

        [JsonPropertyName("categories")]
        public List<string> Categories { get; set; } = new();

        [JsonPropertyName("featured")]
        public List<string> Featured { get; set; } = new();
    }
}
