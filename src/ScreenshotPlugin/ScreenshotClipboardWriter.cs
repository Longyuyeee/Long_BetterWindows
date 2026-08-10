using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Contracts;

namespace ScreenshotPlugin;

public sealed class ScreenshotClipboardWriter
{
    private static readonly TimeSpan[] DefaultRetryDelays =
    [
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
    ];

    private readonly IReadOnlyList<TimeSpan> _retryDelays;

    public ScreenshotClipboardWriter(IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        _retryDelays = retryDelays?.ToArray() ?? DefaultRetryDelays;
        if (_retryDelays.Any(delay => delay < TimeSpan.Zero))
            throw new ArgumentOutOfRangeException(nameof(retryDelays));
    }

    public async Task WriteAsync(
        BitmapSource bitmap,
        Func<BitmapSource, Task<HostApiResponse>> writeAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(writeAsync);

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await writeAsync(bitmap);
            if (result.IsSuccess)
                return;

            if (attempt >= _retryDelays.Count)
            {
                throw new ScreenshotClipboardDeliveryException(
                    result.ErrorMessage ?? "Clipboard image write failed.");
            }

            await Task.Delay(_retryDelays[attempt], cancellationToken);
        }
    }
}

public sealed class ScreenshotCaptureException : InvalidOperationException
{
    public ScreenshotCaptureException(string message)
        : base(message)
    {
    }
}

public sealed class ScreenshotClipboardDeliveryException : InvalidOperationException
{
    public ScreenshotClipboardDeliveryException(string message)
        : base(message)
    {
    }
}
