namespace LongBetterWindows.Host.Engine
{
    internal sealed record WidgetSurfaceLayout(
        int Columns,
        int Rows,
        double Width,
        double Height,
        double DpiScale)
    {
        internal static WidgetSurfaceLayout Empty { get; } = new(0, 0, 0, 0, 1);

        internal bool IsValid =>
            Columns is >= 1 and <= 24
            && Rows is >= 1 and <= 24
            && double.IsFinite(Width)
            && double.IsFinite(Height)
            && double.IsFinite(DpiScale)
            && Width > 0
            && Height > 0
            && DpiScale > 0;

        internal static bool TryFromLogicalSize(
            int columns,
            int rows,
            double logicalWidth,
            double logicalHeight,
            double dpiScaleX,
            double dpiScaleY,
            out WidgetSurfaceLayout layout)
        {
            var scale = Math.Max(dpiScaleX, dpiScaleY);
            layout = new WidgetSurfaceLayout(
                columns,
                rows,
                Math.Round(logicalWidth * dpiScaleX, MidpointRounding.AwayFromZero),
                Math.Round(logicalHeight * dpiScaleY, MidpointRounding.AwayFromZero),
                scale);
            return layout.IsValid;
        }

        internal object ToPayload() => new
        {
            columns = Columns,
            rows = Rows,
            width = Width,
            height = Height,
            dpi_scale = DpiScale,
            scale = DpiScale,
        };
    }
}
