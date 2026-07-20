using System.Text.Json.Serialization;

namespace LongBetterWindows.Host.Contracts
{
    /// <summary>宿主传给插件命令的标准化输入。</summary>
    public sealed class PluginCommandInvocation
    {
        [JsonPropertyName("command_id")]
        public string CommandId { get; init; } = string.Empty;

        [JsonPropertyName("input_type")]
        public AcceptedInputType InputType { get; init; } = AcceptedInputType.None;

        [JsonPropertyName("text")]
        public string? Text { get; init; }

        [JsonPropertyName("paths")]
        public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();

        /// <summary>仅当命令声明接收 Image 时提供；序列化为 Base64 PNG。</summary>
        [JsonPropertyName("image_png")]
        public byte[]? ImagePng { get; init; }

        /// <summary>命令或动态结果提供的非敏感结构化参数。</summary>
        [JsonPropertyName("arguments")]
        public IReadOnlyDictionary<string, string> Arguments { get; init; } =
            new Dictionary<string, string>();
    }

    /// <summary>插件执行完成后返回给宿主的统一结果。</summary>
    public sealed class PluginCommandResult
    {
        public bool IsSuccess { get; init; }
        public string? Message { get; init; }
        public bool KeepPaletteOpen { get; init; }

        public static PluginCommandResult Success(
            string? message = null,
            bool keepPaletteOpen = false)
            => new()
            {
                IsSuccess = true,
                Message = message,
                KeepPaletteOpen = keepPaletteOpen,
            };

        public static PluginCommandResult Failure(string message)
            => new() { IsSuccess = false, Message = message, KeepPaletteOpen = true };
    }
}
