using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace LongBetterWindows.Host.Services;

public static class ScreenColorSampler
{
    public static Color Sample(int x, int y)
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            throw LastWin32Error("GetDC");

        try
        {
            var pixel = GetPixel(screenDc, x, y);
            if (pixel == uint.MaxValue)
                throw LastWin32Error("GetPixel");

            return Color.FromRgb(
                (byte)(pixel & 0xFF),
                (byte)((pixel >> 8) & 0xFF),
                (byte)((pixel >> 16) & 0xFF));
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static Win32Exception LastWin32Error(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        return new Win32Exception(error, $"{operation} failed.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern uint GetPixel(IntPtr dc, int x, int y);
}
