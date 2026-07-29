using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities;

public sealed record ScreenColorSample(byte Red, byte Green, byte Blue)
{
    public string Hex => $"#{Red:X2}{Green:X2}{Blue:X2}";
}

public interface IScreenColorSampler
{
    HostApiResponse<ScreenColorSample> Sample(int physicalX, int physicalY);
}
