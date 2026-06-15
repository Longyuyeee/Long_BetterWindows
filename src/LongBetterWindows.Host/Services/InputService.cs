using System.Runtime.InteropServices;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class InputService : IInputService
    {
        [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint INPUT_MOUSE = 0;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_ABSOLUTE = 0x8000;

        public Task<HostApiResponse> SendKeyPressAsync(int vkCode)
        {
            return Task.Run(() =>
            {
                try
                {
                    keybd_event((byte)vkCode, 0, 0, UIntPtr.Zero);
                    keybd_event((byte)vkCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    return HostApiResponse.Success();
                }
                catch (Exception ex) { return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }

        public Task<HostApiResponse> SendMouseClickAsync(int x, int y, bool rightButton = false)
        {
            return Task.Run(() =>
            {
                try
                {
                    SetCursorPos(x, y);
                    var down = rightButton ? 0x0008u : MOUSEEVENTF_LEFTDOWN;
                    var up = rightButton ? 0x0010u : MOUSEEVENTF_LEFTUP;
                    SendMouseInput(x, y, down | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE);
                    Task.Delay(30).Wait();
                    SendMouseInput(x, y, up | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE);
                    return HostApiResponse.Success();
                }
                catch (Exception ex) { return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }

        public Task<HostApiResponse> MoveCursorAsync(int x, int y)
        {
            return Task.Run(() =>
            {
                try { SetCursorPos(x, y); return HostApiResponse.Success(); }
                catch (Exception ex) { return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }

        private static void SendMouseInput(int x, int y, uint flags)
        {
            var input = new INPUT { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dx = x, dy = y, dwFlags = flags } } };
            SendInput(1, ref input, Marshal.SizeOf<INPUT>());
        }

        struct INPUT { public uint type; public INPUTUNION u; }
        [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; }
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    }
}
