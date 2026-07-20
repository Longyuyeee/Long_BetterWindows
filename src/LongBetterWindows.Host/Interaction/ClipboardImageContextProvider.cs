using System.IO;
using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Interaction
{
    public sealed class ClipboardImageContextProvider : IContextProvider
    {
        private readonly IClipboardService _clipboard;

        public ClipboardImageContextProvider(IClipboardService clipboard)
        {
            _clipboard = clipboard;
        }

        public string Id => "clipboard.image";
        public int Priority => 110;

        public async Task<IReadOnlyList<ContextItem>> CaptureAsync(
            ContextCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await _clipboard.GetImageAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (!response.IsSuccess || response.Data is not { } image)
                return Array.Empty<ContextItem>();

            return new[]
            {
                new ContextItem
                {
                    Id = "clipboard.image",
                    Source = ContextSource.Clipboard,
                    Label = $"剪贴板图片 · {image.PixelWidth}×{image.PixelHeight}",
                    ImagePng = EncodePng(image),
                    CompatibleInputTypes = new[] { AcceptedInputType.Image },
                    Sensitivity = ContextSensitivity.Sensitive,
                },
            };
        }

        private static byte[] EncodePng(BitmapSource image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
    }
}
