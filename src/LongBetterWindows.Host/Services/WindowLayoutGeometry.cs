using LongBetterWindows.Host.Capabilities;

namespace LongBetterWindows.Host.Services;

internal static class WindowLayoutGeometry
{
    public static NativeWindowRect Calculate(
        NativeWindowRect workArea,
        WindowLayout layout)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea));
        if (!Enum.IsDefined(layout))
            throw new ArgumentOutOfRangeException(nameof(layout));

        var halfWidth = workArea.Width / 2;
        var halfHeight = workArea.Height / 2;
        var thirdWidth = workArea.Width / 3;
        return layout switch
        {
            WindowLayout.Left => FromSize(
                workArea.Left,
                workArea.Top,
                halfWidth,
                workArea.Height),
            WindowLayout.Right => FromSize(
                workArea.Left + halfWidth,
                workArea.Top,
                workArea.Width - halfWidth,
                workArea.Height),
            WindowLayout.Maximize => workArea,
            WindowLayout.Bottom => FromSize(
                workArea.Left,
                workArea.Top + halfHeight,
                workArea.Width,
                workArea.Height - halfHeight),
            WindowLayout.TopLeft => FromSize(
                workArea.Left,
                workArea.Top,
                halfWidth,
                halfHeight),
            WindowLayout.TopRight => FromSize(
                workArea.Left + halfWidth,
                workArea.Top,
                workArea.Width - halfWidth,
                halfHeight),
            WindowLayout.BottomLeft => FromSize(
                workArea.Left,
                workArea.Top + halfHeight,
                halfWidth,
                workArea.Height - halfHeight),
            WindowLayout.BottomRight => FromSize(
                workArea.Left + halfWidth,
                workArea.Top + halfHeight,
                workArea.Width - halfWidth,
                workArea.Height - halfHeight),
            WindowLayout.ThirdLeft => FromSize(
                workArea.Left,
                workArea.Top,
                thirdWidth,
                workArea.Height),
            WindowLayout.ThirdRight => FromSize(
                workArea.Left + thirdWidth,
                workArea.Top,
                workArea.Width - thirdWidth,
                workArea.Height),
            _ => throw new ArgumentOutOfRangeException(nameof(layout)),
        };
    }

    private static NativeWindowRect FromSize(
        int x,
        int y,
        int width,
        int height)
        => new(x, y, x + width, y + height);
}
