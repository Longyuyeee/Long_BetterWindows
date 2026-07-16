using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class WallpaperService : IWallpaperService
    {
        public Task<HostApiResponse<string>> GetWallpaperAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                    if (key == null)
                        return HostApiResponse<string>.Failure(ApiErrorCode.NotFound, "无法访问注册表");

                    var wallpaper = key.GetValue("Wallpaper")?.ToString() ?? "";
                    return HostApiResponse<string>.Success(wallpaper);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<string>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> SetWallpaperAsync(string path, WallpaperStyle style = WallpaperStyle.Fill)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(path))
                        return HostApiResponse.Failure(ApiErrorCode.NotFound, "壁纸文件不存在");

                    // 设置壁纸样式
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
                    if (key == null)
                        return HostApiResponse.Failure(ApiErrorCode.NotFound, "无法访问注册表");

                    key.SetValue("WallpaperStyle", GetStyleValue(style).ToString());
                    key.SetValue("TileWallpaper", style == WallpaperStyle.Tile ? "1" : "0");

                    // 应用壁纸
                    SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<WallpaperStyle>> GetWallpaperStyleAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                    if (key == null)
                        return HostApiResponse<WallpaperStyle>.Failure(ApiErrorCode.NotFound, "无法访问注册表");

                    var styleValue = key.GetValue("WallpaperStyle")?.ToString() ?? "10";
                    var tileValue = key.GetValue("TileWallpaper")?.ToString() ?? "0";

                    var style = ParseStyle(styleValue, tileValue);
                    return HostApiResponse<WallpaperStyle>.Success(style);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<WallpaperStyle>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        private int GetStyleValue(WallpaperStyle style)
        {
            return style switch
            {
                WallpaperStyle.Center => 0,
                WallpaperStyle.Stretch => 2,
                WallpaperStyle.Fit => 6,
                WallpaperStyle.Fill => 10,
                WallpaperStyle.Span => 22,
                WallpaperStyle.Tile => 0,
                _ => 10
            };
        }

        private WallpaperStyle ParseStyle(string styleValue, string tileValue)
        {
            if (tileValue == "1") return WallpaperStyle.Tile;

            return styleValue switch
            {
                "0" => WallpaperStyle.Center,
                "2" => WallpaperStyle.Stretch,
                "6" => WallpaperStyle.Fit,
                "10" => WallpaperStyle.Fill,
                "22" => WallpaperStyle.Span,
                _ => WallpaperStyle.Fill
            };
        }

        #region Win32 API

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        private const int SPI_SETDESKWALLPAPER = 0x0014;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDCHANGE = 0x02;

        #endregion
    }
}
