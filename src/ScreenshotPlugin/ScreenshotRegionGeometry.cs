using System.Windows;

namespace ScreenshotPlugin;

public readonly record struct ScreenshotPhysicalPoint(int X, int Y);

public static class ScreenshotRegionGeometry
{
    public static bool TryCreate(
        Int32Rect virtualScreen,
        ScreenshotPhysicalPoint start,
        ScreenshotPhysicalPoint end,
        out Int32Rect region,
        int minimumExtent = 6)
    {
        if (virtualScreen.Width <= 0 || virtualScreen.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(virtualScreen));
        if (minimumExtent <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumExtent));

        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);
        var width = (long)right - left + 1;
        var height = (long)bottom - top + 1;
        var virtualRight = (long)virtualScreen.X + virtualScreen.Width;
        var virtualBottom = (long)virtualScreen.Y + virtualScreen.Height;

        if (left < virtualScreen.X
            || top < virtualScreen.Y
            || (long)right >= virtualRight
            || (long)bottom >= virtualBottom
            || width < minimumExtent
            || height < minimumExtent
            || width > int.MaxValue
            || height > int.MaxValue)
        {
            region = Int32Rect.Empty;
            return false;
        }

        region = new Int32Rect(left, top, (int)width, (int)height);
        return true;
    }
}
