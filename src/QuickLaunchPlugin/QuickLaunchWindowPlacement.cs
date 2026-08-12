using System.Windows;

namespace QuickLaunchPlugin;

internal static class QuickLaunchWindowPlacement
{
    public static Point Calculate(
        Rect workArea,
        Size windowSize,
        double margin = 16,
        double verticalRatio = 0.25)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea));
        if (windowSize.Width <= 0 || windowSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowSize));

        margin = Math.Max(0, margin);
        verticalRatio = Math.Clamp(verticalRatio, 0, 1);
        var preferredLeft = workArea.Left
            + (workArea.Width - windowSize.Width) / 2;
        var preferredTop = workArea.Top + workArea.Height * verticalRatio;
        return new Point(
            ConstrainAxis(
                preferredLeft,
                workArea.Left,
                workArea.Right,
                windowSize.Width,
                margin),
            ConstrainAxis(
                preferredTop,
                workArea.Top,
                workArea.Bottom,
                windowSize.Height,
                margin));
    }

    private static double ConstrainAxis(
        double preferred,
        double minimum,
        double maximum,
        double extent,
        double margin)
    {
        var constrainedMinimum = minimum + margin;
        var constrainedMaximum = maximum - extent - margin;
        if (constrainedMaximum < constrainedMinimum)
            return minimum + (maximum - minimum - extent) / 2;
        return Math.Clamp(preferred, constrainedMinimum, constrainedMaximum);
    }
}
