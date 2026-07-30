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

        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;

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

        /// <summary>
        /// LPWP Web Widget definitions. Only webview plugins with min_api_version
        /// 1.1.0 or newer may declare widgets.
        /// </summary>
        [JsonPropertyName("widgets")]
        public List<PluginWidgetDefinition> Widgets { get; init; } = new();
    }

    public sealed class PluginWidgetDefinition
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;

        [JsonPropertyName("entry_point")]
        public string EntryPoint { get; init; } = string.Empty;

        [JsonPropertyName("icon")]
        public string? Icon { get; init; }

        [JsonPropertyName("multiple_instances")]
        public bool MultipleInstances { get; init; }

        [JsonPropertyName("default_size")]
        public PluginWidgetSize? DefaultSize { get; init; }

        [JsonPropertyName("min_size")]
        public PluginWidgetSize? MinSize { get; init; }

        [JsonPropertyName("max_size")]
        public PluginWidgetSize? MaxSize { get; init; }

        [JsonPropertyName("refresh")]
        public PluginWidgetRefreshPolicy? Refresh { get; init; }

        [JsonPropertyName("appearance")]
        public PluginWidgetAppearance? Appearance { get; init; }

        [JsonPropertyName("settings_schema")]
        public List<PluginWidgetSettingDeclaration> SettingsSchema { get; init; } = new();
    }

    public sealed class PluginWidgetSize
    {
        [JsonPropertyName("columns")]
        public int Columns { get; init; }

        [JsonPropertyName("rows")]
        public int Rows { get; init; }
    }

    public enum PluginWidgetRefreshMode
    {
        Event,
        Interval,
        Manual,
    }

    public enum PluginWidgetHiddenBehavior
    {
        Suspend,
        Throttle,
        Continue,
    }

    public sealed class PluginWidgetRefreshPolicy
    {
        [JsonPropertyName("mode")]
        public PluginWidgetRefreshMode Mode { get; init; } = PluginWidgetRefreshMode.Event;

        [JsonPropertyName("interval_seconds")]
        public int? IntervalSeconds { get; init; }

        [JsonPropertyName("hidden_behavior")]
        public PluginWidgetHiddenBehavior HiddenBehavior { get; init; } = PluginWidgetHiddenBehavior.Suspend;
    }

    public sealed class PluginWidgetAppearance
    {
        [JsonPropertyName("transparent")]
        public bool Transparent { get; init; }

        [JsonPropertyName("host_chrome")]
        public bool HostChrome { get; init; } = true;
    }

    public enum PluginWidgetSettingType
    {
        String,
        Number,
        Integer,
        Boolean,
        Enum,
    }

    public sealed class PluginWidgetSettingDeclaration
    {
        [JsonPropertyName("key")]
        public string Key { get; init; } = string.Empty;

        [JsonPropertyName("type")]
        public PluginWidgetSettingType Type { get; init; } = PluginWidgetSettingType.String;

        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;

        [JsonPropertyName("default")]
        public object? Default { get; init; }

        [JsonPropertyName("enum_values")]
        public List<object> EnumValues { get; init; } = new();
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
