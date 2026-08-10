using System.Globalization;

namespace MacroPlugin;

internal static class MacroLoopIntervalPolicy
{
    internal const int DefaultMilliseconds = 100;
    internal const int MinimumMilliseconds = 50;
    internal const int MaximumMilliseconds = 10_000;

    internal static bool IsValid(int milliseconds)
        => milliseconds is >= MinimumMilliseconds and <= MaximumMilliseconds;

    internal static bool TryParse(string? value, out int milliseconds)
        => int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out milliseconds)
            && IsValid(milliseconds);
}
