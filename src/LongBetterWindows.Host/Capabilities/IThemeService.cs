using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IThemeService
    {
        /// <summary>获取当前系统主题</summary>
        Task<HostApiResponse<SystemTheme>> GetSystemThemeAsync();

        /// <summary>设置系统主题</summary>
        Task<HostApiResponse> SetSystemThemeAsync(SystemTheme theme);

        /// <summary>切换暗色/亮色模式</summary>
        Task<HostApiResponse> ToggleThemeAsync();

        /// <summary>获取系统强调色</summary>
        Task<HostApiResponse<string>> GetAccentColorAsync();

        /// <summary>设置系统强调色</summary>
        Task<HostApiResponse> SetAccentColorAsync(string color);
    }

    public enum SystemTheme
    {
        Light = 0,
        Dark = 1,
        Auto = 2
    }
}
