using System.Runtime.InteropServices;

namespace LongBetterWindows.Host.Services
{
    internal static class ForegroundWindowActivator
    {
        internal static bool TryActivate(nint windowHandle)
        {
            if (windowHandle == nint.Zero || !IsWindow(windowHandle))
                return false;

            var currentThread = GetCurrentThreadId();
            var foregroundThread = GetWindowThreadProcessId(
                GetForegroundWindow(),
                nint.Zero);
            var targetThread = GetWindowThreadProcessId(windowHandle, nint.Zero);
            var attachedForeground = AttachIfNeeded(currentThread, foregroundThread);
            var attachedTarget = targetThread != foregroundThread
                && AttachIfNeeded(currentThread, targetThread);
            try
            {
                BringWindowToTop(windowHandle);
                SetForegroundWindow(windowHandle);
                SetActiveWindow(windowHandle);
                return GetForegroundWindow() == windowHandle;
            }
            finally
            {
                if (attachedTarget)
                    AttachThreadInput(currentThread, targetThread, attach: false);
                if (attachedForeground)
                    AttachThreadInput(currentThread, foregroundThread, attach: false);
            }
        }

        private static bool AttachIfNeeded(uint currentThread, uint targetThread)
            => targetThread != 0
                && targetThread != currentThread
                && AttachThreadInput(currentThread, targetThread, attach: true);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(nint windowHandle);

        [DllImport("user32.dll")]
        private static extern nint GetForegroundWindow();

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            nint windowHandle,
            nint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(
            uint idAttach,
            uint idAttachTo,
            [MarshalAs(UnmanagedType.Bool)] bool attach);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BringWindowToTop(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(nint windowHandle);

        [DllImport("user32.dll")]
        private static extern nint SetActiveWindow(nint windowHandle);
    }
}
