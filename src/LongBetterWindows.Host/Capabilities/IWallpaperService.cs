using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IWallpaperService
    {
        /// <summary>获取当前壁纸路径</summary>
        Task<HostApiResponse<string>> GetWallpaperAsync();

        /// <summary>设置壁纸</summary>
        Task<HostApiResponse> SetWallpaperAsync(string path, WallpaperStyle style = WallpaperStyle.Fill);

        /// <summary>获取壁纸样式</summary>
        Task<HostApiResponse<WallpaperStyle>> GetWallpaperStyleAsync();
    }

    public enum WallpaperStyle
    {
        Center = 0,
        Stretch = 1,
        Fit = 2,
        Fill = 3,
        Span = 4,
        Tile = 5
    }
}
