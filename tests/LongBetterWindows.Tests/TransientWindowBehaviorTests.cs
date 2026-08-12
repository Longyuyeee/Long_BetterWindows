using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LongBetterWindows.PluginSdk.Wpf;

namespace LongBetterWindows.Tests;

public sealed class TransientWindowBehaviorTests
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;

    [Fact]
    public void MakeNonActivating_AppliesNativeTransientStyles()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                window = new Window
                {
                    Width = 80,
                    Height = 40,
                    Left = -32000,
                    Top = -32000,
                    WindowStyle = WindowStyle.None,
                };
                TransientWindowBehavior.MakeNonActivating(
                    window,
                    clickThrough: true);

                window.Show();
                var handle = new WindowInteropHelper(window).Handle;
                Assert.NotEqual(IntPtr.Zero, handle);

                var styles = ReadWindowLong(handle, GwlExStyle).ToInt64();
                Assert.Equal(WsExNoActivate, styles & WsExNoActivate);
                Assert.Equal(WsExTransparent, styles & WsExTransparent);
                Assert.Equal(WsExToolWindow, styles & WsExToolWindow);
                Assert.False(window.ShowActivated);
                Assert.False(window.ShowInTaskbar);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr window, int index);

    private static IntPtr ReadWindowLong(IntPtr window, int index)
        => IntPtr.Size == 8
            ? GetWindowLongPtr(window, index)
            : new IntPtr(GetWindowLong(window, index));
}
