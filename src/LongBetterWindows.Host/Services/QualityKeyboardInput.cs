using System.Runtime.InteropServices;

namespace LongBetterWindows.Host.Services
{
    internal static class QualityKeyboardInput
    {
        private const uint InputKeyboard = 1;
        private const uint KeyUp = 0x0002;
        private const ushort Control = 0x11;
        private const ushort D = 0x44;

        internal static bool Activate(nint windowHandle)
        {
            if (windowHandle == nint.Zero)
                return false;

            var foregroundWindow = GetForegroundWindow();
            var currentThread = GetCurrentThreadId();
            var foregroundThread = foregroundWindow == nint.Zero
                ? 0
                : GetWindowThreadProcessId(foregroundWindow, nint.Zero);
            var attached = foregroundThread != 0
                && foregroundThread != currentThread
                && AttachThreadInput(
                    currentThread,
                    foregroundThread,
                    attach: true);
            try
            {
                BringWindowToTop(windowHandle);
                SetForegroundWindow(windowHandle);
                SetActiveWindow(windowHandle);
                return GetForegroundWindow() == windowHandle;
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(
                        currentThread,
                        foregroundThread,
                        attach: false);
                }
            }
        }

        internal static bool SendControlD()
            => Send(
            [
                Key(Control),
                Key(D),
                Key(D, flags: KeyUp),
                Key(Control, flags: KeyUp),
            ]);

        private static bool Send(Input[] inputs)
            => inputs.Length > 0
                && SendInput(
                    (uint)inputs.Length,
                    inputs,
                    Marshal.SizeOf<Input>()) == inputs.Length;

        private static Input Key(
            ushort virtualKey = 0,
            ushort scanCode = 0,
            uint flags = 0)
            => new()
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = virtualKey,
                        ScanCode = scanCode,
                        Flags = flags,
                    },
                },
            };

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(
            uint inputCount,
            [In] Input[] inputs,
            int inputSize);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(nint windowHandle);

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
        private static extern nint SetActiveWindow(nint windowHandle);

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            internal uint Type;
            internal InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit, Size = 32)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            internal KeyboardInput Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            internal ushort VirtualKey;
            internal ushort ScanCode;
            internal uint Flags;
            internal uint Time;
            internal nint ExtraInfo;
        }
    }
}
