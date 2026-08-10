using LongBetterWindows.Host.Contracts;

namespace ColorPickerPlugin;

public sealed class ColorPickerClipboardWriter
{
    private static readonly TimeSpan[] DefaultRetryDelays =
    [
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
    ];

    private readonly IReadOnlyList<TimeSpan> _retryDelays;

    public ColorPickerClipboardWriter(IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        _retryDelays = retryDelays ?? DefaultRetryDelays;
        if (_retryDelays.Any(delay => delay < TimeSpan.Zero))
            throw new ArgumentOutOfRangeException(nameof(retryDelays));
    }

    public async Task WriteAsync(
        string value,
        Func<string, Task<HostApiResponse>> writeAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(writeAsync);

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await writeAsync(value);
            if (result.IsSuccess)
                return;

            if (attempt >= _retryDelays.Count)
            {
                throw new InvalidOperationException(
                    result.ErrorMessage ?? "Clipboard write failed.");
            }

            await Task.Delay(_retryDelays[attempt], cancellationToken);
        }
    }
}
