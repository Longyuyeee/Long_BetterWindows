using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MacroPlugin;

internal delegate IntPtr MacroHookProc(
    int code,
    IntPtr message,
    IntPtr data);

internal enum MacroMouseButton
{
    Left,
    Right,
}

internal readonly record struct MacroMouseHookData(int X, int Y);
internal readonly record struct MacroKeyboardHookData(uint VirtualKey);

internal interface IMacroNativeApi
{
    IntPtr InstallHook(
        int hookType,
        MacroHookProc callback,
        out int error);
    bool UninstallHook(IntPtr hook, out int error);
    IntPtr CallNextHook(
        int code,
        IntPtr message,
        IntPtr data);
    MacroMouseHookData ReadMouseHookData(IntPtr data);
    MacroKeyboardHookData ReadKeyboardHookData(IntPtr data);
    bool TrySetCursorPosition(int x, int y, out int error);
    bool TrySendMouseButton(
        int x,
        int y,
        MacroMouseButton button,
        bool isDown,
        out int error);
    bool TrySendKey(
        int virtualKey,
        bool isDown,
        out int error);
}

internal sealed class MacroNativeApi : IMacroNativeApi
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventVirtualDesk = 0x4000;
    private const uint MouseEventAbsolute = 0x8000;
    private const uint KeyEventKeyUp = 0x0002;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private static readonly IntPtr ModuleHandle = GetModuleHandle(
        Process.GetCurrentProcess().MainModule?.ModuleName);

    public IntPtr InstallHook(
        int hookType,
        MacroHookProc callback,
        out int error)
    {
        var hook = SetWindowsHookEx(
            hookType,
            callback,
            ModuleHandle,
            0);
        error = hook == IntPtr.Zero
            ? Marshal.GetLastWin32Error()
            : 0;
        return hook;
    }

    public bool UninstallHook(IntPtr hook, out int error)
    {
        var succeeded = UnhookWindowsHookEx(hook);
        error = succeeded ? 0 : Marshal.GetLastWin32Error();
        return succeeded;
    }

    public IntPtr CallNextHook(
        int code,
        IntPtr message,
        IntPtr data)
        => CallNextHookEx(IntPtr.Zero, code, message, data);

    public MacroMouseHookData ReadMouseHookData(IntPtr data)
    {
        var value = Marshal.PtrToStructure<MouseHookData>(data);
        return new MacroMouseHookData(value.Point.X, value.Point.Y);
    }

    public MacroKeyboardHookData ReadKeyboardHookData(IntPtr data)
    {
        var value = Marshal.PtrToStructure<KeyboardHookData>(data);
        return new MacroKeyboardHookData(value.VirtualKey);
    }

    public bool TrySetCursorPosition(
        int x,
        int y,
        out int error)
    {
        var succeeded = SetCursorPos(x, y);
        error = succeeded ? 0 : Marshal.GetLastWin32Error();
        return succeeded;
    }

    public bool TrySendMouseButton(
        int x,
        int y,
        MacroMouseButton button,
        bool isDown,
        out int error)
    {
        var virtualLeft = GetSystemMetrics(SmXVirtualScreen);
        var virtualTop = GetSystemMetrics(SmYVirtualScreen);
        var virtualWidth = Math.Max(
            1,
            GetSystemMetrics(SmCxVirtualScreen));
        var virtualHeight = Math.Max(
            1,
            GetSystemMetrics(SmCyVirtualScreen));
        var flags = (button, isDown) switch
        {
            (MacroMouseButton.Left, true) => MouseEventLeftDown,
            (MacroMouseButton.Left, false) => MouseEventLeftUp,
            (MacroMouseButton.Right, true) => MouseEventRightDown,
            _ => MouseEventRightUp,
        };
        var input = new NativeInput
        {
            Type = InputMouse,
            Value = new NativeInputValue
            {
                Mouse = new NativeMouseInput
                {
                    X = NormalizeAbsoluteCoordinate(
                        x,
                        virtualLeft,
                        virtualWidth),
                    Y = NormalizeAbsoluteCoordinate(
                        y,
                        virtualTop,
                        virtualHeight),
                    Flags = flags
                        | MouseEventMove
                        | MouseEventVirtualDesk
                        | MouseEventAbsolute,
                },
            },
        };
        return TrySend(input, out error);
    }

    public bool TrySendKey(
        int virtualKey,
        bool isDown,
        out int error)
    {
        var input = new NativeInput
        {
            Type = InputKeyboard,
            Value = new NativeInputValue
            {
                Keyboard = new NativeKeyboardInput
                {
                    VirtualKey = checked((ushort)virtualKey),
                    Flags = isDown ? 0 : KeyEventKeyUp,
                },
            },
        };
        return TrySend(input, out error);
    }

    internal static int NormalizeAbsoluteCoordinate(
        int coordinate,
        int origin,
        int extent)
        => (int)Math.Round(
            Math.Clamp(
                (coordinate - origin)
                / (double)Math.Max(1, extent - 1),
                0,
                1)
            * 65535);

    private static bool TrySend(
        NativeInput input,
        out int error)
    {
        var inserted = SendInput(
            1,
            ref input,
            Marshal.SizeOf<NativeInput>());
        error = inserted == 1 ? 0 : Marshal.GetLastWin32Error();
        return inserted == 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputValue Value;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputValue
    {
        [FieldOffset(0)]
        public NativeMouseInput Mouse;

        [FieldOffset(0)]
        public NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowsHookExW",
        SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookType,
        MacroHookProc callback,
        IntPtr module,
        uint threadId);

    [DllImport(
        "user32.dll",
        EntryPoint = "UnhookWindowsHookEx",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll", EntryPoint = "CallNextHookEx")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr message,
        IntPtr data);

    [DllImport(
        "user32.dll",
        EntryPoint = "SendInput",
        SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        ref NativeInput inputs,
        int size);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetCursorPos",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", EntryPoint = "GetSystemMetrics")]
    private static extern int GetSystemMetrics(int index);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetModuleHandleW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
