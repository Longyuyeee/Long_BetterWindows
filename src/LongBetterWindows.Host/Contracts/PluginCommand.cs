using System.Text.Json.Serialization;

namespace LongBetterWindows.Host.Contracts
{
    /// <summary>插件命令可以接收的上下文输入类型。</summary>
    public enum AcceptedInputType
    {
        None,
        Text,
        Url,
        Image,
        File,
        Files,
        Folder,
        Clipboard,
        ExplorerSelection,
    }

    /// <summary>插件命令打开后的标准交互模式。</summary>
    public enum PluginViewMode
    {
        None,
        List,
        Form,
        Document,
        Custom,
    }

    /// <summary>宿主为插件提供的标准窗口规格。</summary>
    public enum PluginWindowMode
    {
        Compact,
        Standard,
        Wide,
        Document,
        Overlay,
        FullScreen,
    }

    /// <summary>插件主窗口关闭后的默认行为。</summary>
    public enum PluginCloseBehavior
    {
        Stop,
        Background,
    }

    /// <summary>插件主界面的默认承载方式。</summary>
    public enum PluginPresentationMode
    {
        Embedded,
        Detached,
    }

    /// <summary>插件暴露给统一命令入口的功能指令。</summary>
    public sealed class PluginCommand
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;

        [JsonPropertyName("aliases")]
        public List<string> Aliases { get; init; } = new();

        [JsonPropertyName("accepted_inputs")]
        public List<AcceptedInputType> AcceptedInputs { get; init; } = new() { AcceptedInputType.None };

        [JsonPropertyName("view_mode")]
        public PluginViewMode ViewMode { get; init; } = PluginViewMode.Custom;

        [JsonPropertyName("keep_alive")]
        public bool KeepAlive { get; init; }

        /// <summary>插件声明的静态排序权重，建议范围 -100 到 100。</summary>
        [JsonPropertyName("priority")]
        public int Priority { get; init; }
    }

    /// <summary>插件窗口的建议尺寸。宿主仍负责最终的屏幕边界约束。</summary>
    public sealed class PluginWindowPreference
    {
        [JsonPropertyName("mode")]
        public PluginWindowMode Mode { get; init; } = PluginWindowMode.Standard;

        [JsonPropertyName("preferred_width")]
        public int? PreferredWidth { get; init; }

        [JsonPropertyName("preferred_height")]
        public int? PreferredHeight { get; init; }

        [JsonPropertyName("min_width")]
        public int? MinWidth { get; init; }

        [JsonPropertyName("min_height")]
        public int? MinHeight { get; init; }
    }

    /// <summary>由宿主统一执行的插件生命周期策略。</summary>
    public sealed class PluginLifecyclePreference
    {
        [JsonPropertyName("start_with_host")]
        public bool StartWithHost { get; init; } = true;

        [JsonPropertyName("close_behavior")]
        public PluginCloseBehavior CloseBehavior { get; init; } = PluginCloseBehavior.Stop;

        [JsonPropertyName("default_presentation")]
        public PluginPresentationMode DefaultPresentation { get; init; } = PluginPresentationMode.Detached;

        [JsonPropertyName("search_in_background")]
        public bool SearchInBackground { get; init; }
    }
}
