using System.Runtime.InteropServices;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class WindowManagerDesktopIsolationTests
{
    private const uint WsOverlapped = 0;
    private const uint WsVisible = 0x10000000;

    [Theory]
    [InlineData(WindowLayout.Left)]
    [InlineData(WindowLayout.Right)]
    [InlineData(WindowLayout.Bottom)]
    [InlineData(WindowLayout.TopLeft)]
    [InlineData(WindowLayout.TopRight)]
    [InlineData(WindowLayout.BottomLeft)]
    [InlineData(WindowLayout.BottomRight)]
    [InlineData(WindowLayout.ThirdLeft)]
    [InlineData(WindowLayout.ThirdRight)]
    public void Layout_UsesExactMonitorWorkAreaOnDisposableWindow(
        WindowLayout layout)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var window = CreateDisposableWindow();
        try
        {
            var native = new WindowNativeApi();
            var monitor = native.MonitorFromWindow(window);
            Assert.NotEqual(IntPtr.Zero, monitor);
            Assert.True(native.TryGetMonitorWorkArea(
                monitor,
                out var workArea,
                out var monitorError),
                $"GetMonitorInfo failed with Win32 error {monitorError}.");
            var expected = WindowLayoutGeometry.Calculate(workArea, layout);
            var service = new WindowInfoService(native);

            var result = service.ApplyLayout(window, layout);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(native.TryGetWindowRect(
                window,
                out var actual,
                out var rectError),
                $"GetWindowRect failed with Win32 error {rectError}.");
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Left, result.Data?.After?.X);
            Assert.Equal(expected.Top, result.Data?.After?.Y);
            Assert.Equal(expected.Width, result.Data?.After?.Width);
            Assert.Equal(expected.Height, result.Data?.After?.Height);
        }
        finally
        {
            Assert.True(DestroyWindow(window));
        }
    }

    [Fact]
    public void Topmost_RoundTripPreservesDisposableWindowGeometry()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var window = CreateDisposableWindow();
        try
        {
            var native = new WindowNativeApi();
            Assert.True(native.TryGetWindowRect(
                window,
                out var original,
                out var originalError),
                $"GetWindowRect failed with Win32 error {originalError}.");
            Assert.True(native.TryGetTopmost(
                window,
                out var initiallyTopmost,
                out var initialStyleError),
                $"GetWindowLongPtr failed with Win32 error {initialStyleError}.");
            Assert.False(initiallyTopmost);
            var service = new WindowInfoService(native);

            var enabled = service.ToggleTopmost(window);
            var disabled = service.ToggleTopmost(window);

            Assert.True(enabled.IsSuccess, enabled.ErrorMessage);
            Assert.True(enabled.Data?.After?.IsTopmost);
            Assert.True(disabled.IsSuccess, disabled.ErrorMessage);
            Assert.False(disabled.Data?.After?.IsTopmost);
            Assert.True(native.TryGetWindowRect(
                window,
                out var after,
                out var afterError),
                $"GetWindowRect failed with Win32 error {afterError}.");
            Assert.Equal(original, after);
        }
        finally
        {
            Assert.True(DestroyWindow(window));
        }
    }

    private static IntPtr CreateDisposableWindow()
    {
        var window = CreateWindowEx(
            0,
            "STATIC",
            "LongAssistant WindowManager isolation target",
            WsOverlapped | WsVisible,
            -32000,
            -32000,
            320,
            240,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, window);
        return window;
    }

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);
}
