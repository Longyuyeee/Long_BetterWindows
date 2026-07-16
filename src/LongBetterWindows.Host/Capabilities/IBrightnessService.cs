using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IBrightnessService
    {
        /// <summary>获取屏幕亮度（0-100）</summary>
        Task<HostApiResponse<int>> GetBrightnessAsync();

        /// <summary>设置屏幕亮度（0-100）</summary>
        Task<HostApiResponse> SetBrightnessAsync(int brightness);

        /// <summary>增加亮度</summary>
        Task<HostApiResponse<int>> IncreaseBrightnessAsync(int step = 10);

        /// <summary>降低亮度</summary>
        Task<HostApiResponse<int>> DecreaseBrightnessAsync(int step = 10);
    }
}
