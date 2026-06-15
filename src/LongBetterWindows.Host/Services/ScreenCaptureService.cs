using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class ScreenCaptureService : IScreenCaptureService
    {
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        const uint SRCCOPY = 0x00CC0020;

        public Task<HostApiResponse> CaptureFullScreenAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        int x = (int)SystemParameters.VirtualScreenLeft;
                        int y = (int)SystemParameters.VirtualScreenTop;
                        int w = (int)SystemParameters.VirtualScreenWidth;
                        int h = (int)SystemParameters.VirtualScreenHeight;
                        var bitmap = CaptureRect(x, y, w, h);
                        Clipboard.SetImage(bitmap);
                    });
                    return HostApiResponse.Success();
                }
                catch (Exception ex) { return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }

        public Task<HostApiResponse<BitmapSource>> CaptureToBitmapAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    BitmapSource? result = null;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        int x = (int)SystemParameters.VirtualScreenLeft;
                        int y = (int)SystemParameters.VirtualScreenTop;
                        int w = (int)SystemParameters.VirtualScreenWidth;
                        int h = (int)SystemParameters.VirtualScreenHeight;
                        result = CaptureRect(x, y, w, h);
                    });
                    return HostApiResponse<BitmapSource>.Success(result!);
                }
                catch (Exception ex) { return HostApiResponse<BitmapSource>.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }

        public Task<HostApiResponse<BitmapSource>> CaptureRegionAsync(int x, int y, int width, int height)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (width <= 0 || height <= 0)
                        return HostApiResponse<BitmapSource>.Failure(ApiErrorCode.InvalidArgument, "区域宽高必须大于 0");

                    BitmapSource? result = null;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        result = CaptureRect(x, y, width, height);
                    });
                    return HostApiResponse<BitmapSource>.Success(result!);
                }
                catch (Exception ex) { return HostApiResponse<BitmapSource>.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }

        /// <summary>GDI 屏幕区域捕获，必须在 UI 线程调用</summary>
        private static BitmapSource CaptureRect(int sx, int sy, int w, int h)
        {
            var dc = GetDC(IntPtr.Zero);
            var memDc = CreateCompatibleDC(dc);
            var bmp = CreateCompatibleBitmap(dc, w, h);
            SelectObject(memDc, bmp);
            BitBlt(memDc, 0, 0, w, h, dc, sx, sy, SRCCOPY);
            var result = Imaging.CreateBitmapSourceFromHBitmap(
                bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            DeleteObject(bmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, dc);
            return result;
        }
    }
}
