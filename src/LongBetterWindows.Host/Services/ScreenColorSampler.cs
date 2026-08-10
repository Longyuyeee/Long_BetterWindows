using System.ComponentModel;
using System.Runtime.InteropServices;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services;

public sealed class ScreenColorSampler : IScreenColorSampler
{
    public HostApiResponse<ScreenColorSample> Sample(int physicalX, int physicalY)
    {
        var bounds = ScreenCaptureService.GetVirtualScreenBounds();
        if (physicalX < bounds.X
            || physicalY < bounds.Y
            || physicalX >= (long)bounds.X + bounds.Width
            || physicalY >= (long)bounds.Y + bounds.Height)
        {
            return HostApiResponse<ScreenColorSample>.Failure(
                ApiErrorCode.InvalidArgument,
                "The sample coordinate is outside the physical virtual screen.");
        }

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            return Failure("GetDC");

        try
        {
            var pixel = GetPixel(screenDc, physicalX, physicalY);
            if (pixel == uint.MaxValue)
                return Failure("GetPixel");

            return HostApiResponse<ScreenColorSample>.Success(new(
                (byte)(pixel & 0xFF),
                (byte)((pixel >> 8) & 0xFF),
                (byte)((pixel >> 16) & 0xFF)));
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static HostApiResponse<ScreenColorSample> Failure(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        var exception = new Win32Exception(error, $"{operation} failed.");
        return HostApiResponse<ScreenColorSample>.Failure(
            ApiErrorCode.Win32Error,
            exception.Message);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern uint GetPixel(IntPtr dc, int x, int y);
}
