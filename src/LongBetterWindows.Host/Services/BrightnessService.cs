using System.Runtime.InteropServices;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class BrightnessService : IBrightnessService
    {
        public Task<HostApiResponse<int>> GetBrightnessAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var brightness = GetCurrentBrightness();
                    return HostApiResponse<int>.Success(brightness);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<int>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> SetBrightnessAsync(int brightness)
        {
            return Task.Run(() =>
            {
                try
                {
                    brightness = Math.Clamp(brightness, 0, 100);
                    SetCurrentBrightness(brightness);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public async Task<HostApiResponse<int>> IncreaseBrightnessAsync(int step = 10)
        {
            var currentResult = await GetBrightnessAsync();
            if (!currentResult.IsSuccess)
                return HostApiResponse<int>.Failure(currentResult.ErrorCode, currentResult.ErrorMessage);

            var newBrightness = Math.Min(currentResult.Data + step, 100);
            await SetBrightnessAsync(newBrightness);
            return HostApiResponse<int>.Success(newBrightness);
        }

        public async Task<HostApiResponse<int>> DecreaseBrightnessAsync(int step = 10)
        {
            var currentResult = await GetBrightnessAsync();
            if (!currentResult.IsSuccess)
                return HostApiResponse<int>.Failure(currentResult.ErrorCode, currentResult.ErrorMessage);

            var newBrightness = Math.Max(currentResult.Data - step, 0);
            await SetBrightnessAsync(newBrightness);
            return HostApiResponse<int>.Success(newBrightness);
        }

        private int GetCurrentBrightness()
        {
            try
            {
                IntPtr hLCD = CreateFile("\\\\?\\LCD#", 0x80000000 | 0x40000000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (hLCD == IntPtr.Zero || hLCD == new IntPtr(-1))
                    return 50; // 默认值

                DISPLAY_BRIGHTNESS brightness = new DISPLAY_BRIGHTNESS();
                brightness.ucDisplayPolicy = 1;
                brightness.ucACBrightness = 0;
                brightness.ucDCBrightness = 0;

                int size = Marshal.SizeOf(brightness);
                IntPtr pBrightness = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(brightness, pBrightness, false);

                uint dwReturned = 0;
                bool result = DeviceIoControl(hLCD, 0x70070, pBrightness, (uint)size, pBrightness, (uint)size, ref dwReturned, IntPtr.Zero);

                if (result)
                {
                    brightness = Marshal.PtrToStructure<DISPLAY_BRIGHTNESS>(pBrightness);
                }

                Marshal.FreeHGlobal(pBrightness);
                CloseHandle(hLCD);

                return brightness.ucACBrightness;
            }
            catch
            {
                return 50;
            }
        }

        private void SetCurrentBrightness(int brightness)
        {
            try
            {
                IntPtr hLCD = CreateFile("\\\\?\\LCD#", 0x80000000 | 0x40000000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (hLCD == IntPtr.Zero || hLCD == new IntPtr(-1))
                    return;

                DISPLAY_BRIGHTNESS displayBrightness = new DISPLAY_BRIGHTNESS();
                displayBrightness.ucDisplayPolicy = 1;
                displayBrightness.ucACBrightness = (byte)brightness;
                displayBrightness.ucDCBrightness = (byte)brightness;

                int size = Marshal.SizeOf(displayBrightness);
                IntPtr pBrightness = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(displayBrightness, pBrightness, false);

                uint dwReturned = 0;
                DeviceIoControl(hLCD, 0x70072, pBrightness, (uint)size, IntPtr.Zero, 0, ref dwReturned, IntPtr.Zero);

                Marshal.FreeHGlobal(pBrightness);
                CloseHandle(hLCD);
            }
            catch { }
        }

        #region Win32 API

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DISPLAY_BRIGHTNESS
        {
            public byte ucDisplayPolicy;
            public byte ucACBrightness;
            public byte ucDCBrightness;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer,
            uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, ref uint lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        #endregion
    }
}
