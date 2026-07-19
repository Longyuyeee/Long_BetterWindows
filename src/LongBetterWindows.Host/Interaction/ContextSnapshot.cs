using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Interaction
{
    public enum ContextSource
    {
        Clipboard,
        ExplorerSelection,
        ActiveExplorerFolder,
        ForegroundWindow,
    }

    public enum ContextSensitivity
    {
        Public,
        Personal,
        Sensitive,
    }

    public sealed record ContextCaptureRequest(
        IntPtr ForegroundWindowHandle,
        DateTimeOffset CapturedAt);

    public sealed record ContextItem
    {
        public required string Id { get; init; }
        public required ContextSource Source { get; init; }
        public required string Label { get; init; }
        public string? Text { get; init; }
        public byte[]? ImagePng { get; init; }
        public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();
        public IReadOnlyList<AcceptedInputType> CompatibleInputTypes { get; init; } =
            Array.Empty<AcceptedInputType>();
        public ContextSensitivity Sensitivity { get; init; } = ContextSensitivity.Personal;
    }

    public sealed record ContextSelection(ContextItem Item, AcceptedInputType InputType);

    public sealed class ContextSnapshot
    {
        public static ContextSnapshot Empty { get; } =
            new(DateTimeOffset.MinValue, Array.Empty<ContextItem>());

        public ContextSnapshot(DateTimeOffset capturedAt, IEnumerable<ContextItem> items)
        {
            CapturedAt = capturedAt;
            Items = items
                .Where(item => item.CompatibleInputTypes.Count > 0)
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        public DateTimeOffset CapturedAt { get; }
        public IReadOnlyList<ContextItem> Items { get; }

        public IReadOnlyList<AcceptedInputType> AvailableInputTypes
            => Items.SelectMany(item => item.CompatibleInputTypes).Distinct().ToList();

        public ContextSelection? SelectBest(IReadOnlyCollection<AcceptedInputType> acceptedInputs)
        {
            foreach (var item in Items)
            {
                foreach (var inputType in item.CompatibleInputTypes)
                {
                    if (acceptedInputs.Contains(inputType))
                        return new ContextSelection(item, inputType);
                }
            }

            return null;
        }

        public ContextSnapshot Without(string itemId)
            => new(CapturedAt, Items.Where(item =>
                !string.Equals(item.Id, itemId, StringComparison.OrdinalIgnoreCase)));

        /// <summary>
        /// 搜索插件默认只获知上下文类型，不接收文本、路径、图片或可识别标签。
        /// 后续若开放内容搜索，必须按 Manifest 能力进行单独投影。
        /// </summary>
        public ContextSnapshot MetadataOnly()
            => new(CapturedAt, Items.Select(item => item with
            {
                Label = $"{item.Source} context",
                Text = null,
                ImagePng = null,
                Paths = Array.Empty<string>(),
            }));
    }
}
