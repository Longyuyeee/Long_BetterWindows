using System.Runtime.InteropServices;

namespace LongBetterWindows.Host.Services
{
    internal static class NativeMethods
    {
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(
            IntPtr hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(
            IntPtr hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FlushFileBuffers(IntPtr hFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool DeleteFileW(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetFileInformationByHandleEx(
            IntPtr hFile,
            int FileInformationClass,
            out FILE_STREAM_INFO lpFileInformation,
            uint dwBufferSize);

        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        public const uint CREATE_ALWAYS = 2;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

        // === 多显示器支持 ===

    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_STREAM_INFO
    {
        public uint NextEntryOffset;
        public uint StreamNameLength;
        public long StreamSize;
        public long StreamAllocationSize;
        // StreamName follows in memory (WCHAR[])
    }
}
