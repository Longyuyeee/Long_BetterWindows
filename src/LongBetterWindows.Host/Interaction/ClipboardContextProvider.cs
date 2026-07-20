using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Interaction
{
    public sealed class ClipboardContextProvider : IContextProvider
    {
        private readonly IClipboardService _clipboard;

        public ClipboardContextProvider(IClipboardService clipboard)
        {
            _clipboard = clipboard;
        }

        public string Id => "clipboard";
        public int Priority => 100;

        public async Task<IReadOnlyList<ContextItem>> CaptureAsync(
            ContextCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var textResponse = await _clipboard.GetTextAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var text = textResponse.IsSuccess ? textResponse.Data?.Trim() : null;
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<ContextItem>();

            var isUrl = Uri.TryCreate(text, UriKind.Absolute, out var uri)
                        && uri.Scheme is "http" or "https";
            var preview = text.Length > 24 ? text[..24] + "…" : text;
            return new[]
            {
                new ContextItem
                {
                    Id = "clipboard.text",
                    Source = ContextSource.Clipboard,
                    Label = isUrl ? $"剪贴板链接 · {preview}" : $"剪贴板文本 · {preview}",
                    Text = text,
                    CompatibleInputTypes = isUrl
                        ? new[] { AcceptedInputType.Url, AcceptedInputType.Clipboard, AcceptedInputType.Text }
                        : new[] { AcceptedInputType.Clipboard, AcceptedInputType.Text },
                    Sensitivity = ContextSensitivity.Personal,
                },
            };
        }
    }
}
