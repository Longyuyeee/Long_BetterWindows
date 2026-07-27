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

        [JsonPropertyName("min_host_version")]
        public string? MinHostVersion { get; init; }

        [JsonPropertyName("min_ui_kit_version")]
        public string? MinUiKitVersion { get; init; }

        [JsonPropertyName("default_settings")]
        public Dictionary<string, object>? DefaultSettings { get; init; }

        [JsonPropertyName("dependencies")]
        public List<PluginDependency> Dependencies { get; init; } = new();

        /// <summary>插件暴露给统一入口的功能指令；旧插件缺省为空。</summary>
        [JsonPropertyName("commands")]
        public List<PluginCommand> Commands { get; init; } = new();

        /// <summary>插件主界面的窗口模式和建议尺寸。</summary>
        [JsonPropertyName("window")]
        public PluginWindowPreference? Window { get; init; }

        /// <summary>启动、关闭、后台和窗口承载策略；旧插件缺省使用安全默认值。</summary>
        [JsonPropertyName("lifecycle")]
        public PluginLifecyclePreference? Lifecycle { get; init; }

        /// <summary>可选的插件语言资源声明；未声明时保留 Manifest 原文和旧运行时行为。</summary>
        [JsonPropertyName("localization")]
        public PluginLocalizationPreference? Localization { get; init; }

        /// <summary>
        /// Web 插件可选的原生后台伴生组件。后台组件实现 ILongPlugin，
        /// 与按需 Web UI 共用一个插件生命周期。
        /// </summary>
        [JsonPropertyName("background")]
        public PluginBackgroundPreference? Background { get; init; }
    }

    public sealed class PluginBackgroundPreference
    {
        [JsonPropertyName("entry_point")]
        public string EntryPoint { get; init; } = string.Empty;
    }

    public class PluginDependency
    {
        [JsonPropertyName("plugin_id")]
        public string PluginId { get; set; } = string.Empty;

        [JsonPropertyName("min_version")]
        public string MinVersion { get; set; } = string.Empty;
    }
}
