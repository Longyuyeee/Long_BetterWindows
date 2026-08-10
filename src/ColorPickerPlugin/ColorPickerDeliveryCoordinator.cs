using System.Text.RegularExpressions;
using LongBetterWindows.Host.Core;

namespace ColorPickerPlugin;

public sealed class ColorPickerDeliveryCoordinator
{
    private static readonly Regex HexColorPattern = new(
        "^#[0-9A-F]{6}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private int _state;

    public async Task<bool> TryDeliverAsync(
        string hex,
        Func<string, Task> deliverAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deliverAsync);
        if (!HexColorPattern.IsMatch(hex))
            throw new ArgumentException("A canonical RGB hex value is required.", nameof(hex));

        if (cancellationToken.IsCancellationRequested)
        {
            Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            return false;

        await AsyncDeliveryBoundary.RunAsync(
            () => Task.FromResult(hex),
            deliverAsync,
            cancellationToken);
        return true;
    }

    public bool Cancel()
        => Interlocked.CompareExchange(ref _state, 2, 0) == 0;
}
