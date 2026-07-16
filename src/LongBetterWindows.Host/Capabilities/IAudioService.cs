using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IAudioService
    {
        /// <summary>获取系统音量（0-100）</summary>
        Task<HostApiResponse<int>> GetVolumeAsync();

        /// <summary>设置系统音量（0-100）</summary>
        Task<HostApiResponse> SetVolumeAsync(int volume);

        /// <summary>获取静音状态</summary>
        Task<HostApiResponse<bool>> GetMuteAsync();

        /// <summary>设置静音状态</summary>
        Task<HostApiResponse> SetMuteAsync(bool mute);

        /// <summary>音量增加</summary>
        Task<HostApiResponse<int>> IncreaseVolumeAsync(int step = 5);

        /// <summary>音量减少</summary>
        Task<HostApiResponse<int>> DecreaseVolumeAsync(int step = 5);

        /// <summary>获取音频设备列表</summary>
        Task<HostApiResponse<List<AudioDevice>>> GetAudioDevicesAsync();

        /// <summary>切换默认音频设备</summary>
        Task<HostApiResponse> SetDefaultDeviceAsync(string deviceId);
    }

    public class AudioDevice
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsDefault { get; set; }
    }
}
