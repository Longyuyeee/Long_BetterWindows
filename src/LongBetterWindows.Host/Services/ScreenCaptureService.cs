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
                        int w = (int)SystemParameters.PrimaryScreenWidth;
                        int h = (int)SystemParameters.PrimaryScreenHeight;
                        var dc = GetDC(IntPtr.Zero);
                        var memDc = CreateCompatibleDC(dc);
                        var bmp = CreateCompatibleBitmap(dc, w, h);
                        SelectObject(memDc, bmp);
                        BitBlt(memDc, 0, 0, w, h, dc, 0, 0, SRCCOPY);
                        var bitmap = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        Clipboard.SetImage(bitmap);
                        DeleteObject(bmp); DeleteDC(memDc); ReleaseDC(IntPtr.Zero, dc);
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
                        int w = (int)SystemParameters.PrimaryScreenWidth;
                        int h = (int)SystemParameters.PrimaryScreenHeight;
                        var dc = GetDC(IntPtr.Zero);
                        var memDc = CreateCompatibleDC(dc);
                        var bmp = CreateCompatibleBitmap(dc, w, h);
                        SelectObject(memDc, bmp);
                        BitBlt(memDc, 0, 0, w, h, dc, 0, 0, SRCCOPY);
                        result = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        DeleteObject(bmp); DeleteDC(memDc); ReleaseDC(IntPtr.Zero, dc);
                    });
                    return HostApiResponse<BitmapSource>.Success(result!);
                }
                catch (Exception ex) { return HostApiResponse<BitmapSource>.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }
    }
}
