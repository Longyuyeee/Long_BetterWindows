using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.PluginSdk.Wpf;

namespace LongBetterWindows.Host.Services
{
    public sealed class ScreenCaptureService : IScreenCaptureService
    {
        private const uint Srccopy = 0x00CC0020;
        private const uint Captureblt = 0x40000000;
        private readonly IClipboardService _clipboard;

        public ScreenCaptureService(IClipboardService clipboard)
        {
            _clipboard = clipboard;
        }

        public async Task<HostApiResponse> CaptureFullScreenAsync()
        {
            var capture = await CaptureToBitmapAsync();
            if (!capture.IsSuccess || capture.Data is null)
            {
                return HostApiResponse.Failure(
                    capture.ErrorCode,
                    capture.ErrorMessage ?? "Screen capture failed.");
            }

            return await _clipboard.SetImageAsync(capture.Data);
        }

        public Task<HostApiResponse<BitmapSource>> CaptureToBitmapAsync()
        {
            try
            {
                return CaptureAsync(GetVirtualScreenBounds());
            }
            catch (Exception ex)
            {
                return Task.FromResult(HostApiResponse<BitmapSource>.Failure(
                    ApiErrorCode.Unknown,
                    ex.Message));
            }
        }

        public Task<HostApiResponse<BitmapSource>> CaptureRegionAsync(
            int x,
            int y,
            int width,
            int height)
        {
            if (width <= 0 || height <= 0)
            {
                return Task.FromResult(HostApiResponse<BitmapSource>.Failure(
                    ApiErrorCode.InvalidArgument,
                    "Region width and height must be greater than zero."));
            }

            try
            {
                var virtualScreen = GetVirtualScreenBounds();
                var right = (long)x + width;
                var bottom = (long)y + height;
                if (x < virtualScreen.X
                    || y < virtualScreen.Y
                    || right > (long)virtualScreen.X + virtualScreen.Width
                    || bottom > (long)virtualScreen.Y + virtualScreen.Height)
                {
                    return Task.FromResult(HostApiResponse<BitmapSource>.Failure(
                        ApiErrorCode.InvalidArgument,
                        "Capture region must be inside the virtual screen."));
                }

                return CaptureAsync(new Int32Rect(x, y, width, height));
            }
            catch (Exception ex)
            {
                return Task.FromResult(HostApiResponse<BitmapSource>.Failure(
                    ApiErrorCode.Unknown,
                    ex.Message));
            }
        }

        public static Int32Rect GetVirtualScreenBounds()
            => VirtualScreenHelper.GetPhysicalBounds();

        private static Task<HostApiResponse<BitmapSource>> CaptureAsync(Int32Rect bounds)
            => Task.Run(() =>
            {
                try
                {
                    return HostApiResponse<BitmapSource>.Success(CaptureRect(bounds));
                }
                catch (Exception ex)
                {
                    return HostApiResponse<BitmapSource>.Failure(
                        ApiErrorCode.Unknown,
                        ex.Message);
                }
            });

        private static BitmapSource CaptureRect(Int32Rect bounds)
        {
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memoryDc = IntPtr.Zero;
            IntPtr bitmap = IntPtr.Zero;
            IntPtr previous = IntPtr.Zero;

            try
            {
                screenDc = GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero)
                    throw LastWin32Error("GetDC");

                memoryDc = CreateCompatibleDC(screenDc);
                if (memoryDc == IntPtr.Zero)
                    throw LastWin32Error("CreateCompatibleDC");

                bitmap = CreateCompatibleBitmap(
                    screenDc,
                    bounds.Width,
                    bounds.Height);
                if (bitmap == IntPtr.Zero)
                    throw LastWin32Error("CreateCompatibleBitmap");

                previous = SelectObject(memoryDc, bitmap);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1))
                    throw LastWin32Error("SelectObject");

                if (!BitBlt(
                        memoryDc,
                        0,
                        0,
                        bounds.Width,
                        bounds.Height,
                        screenDc,
                        bounds.X,
                        bounds.Y,
                        Srccopy | Captureblt))
                {
                    throw LastWin32Error("BitBlt");
                }

                var result = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    bitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                result.Freeze();
                return result;
            }
            finally
            {
                if (previous != IntPtr.Zero && previous != new IntPtr(-1))
                    SelectObject(memoryDc, previous);
                if (bitmap != IntPtr.Zero)
                    DeleteObject(bitmap);
                if (memoryDc != IntPtr.Zero)
                    DeleteDC(memoryDc);
                if (screenDc != IntPtr.Zero)
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
        private static extern bool BitBlt(
            IntPtr destination,
            int x,
            int y,
            int width,
            int height,
            IntPtr source,
            int sourceX,
            int sourceY,
            uint operation);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleBitmap(
            IntPtr dc,
            int width,
            int height);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr value);
    }
}
