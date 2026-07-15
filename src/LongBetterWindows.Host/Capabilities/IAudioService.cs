using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities;

/// <summary>
/// 音频控制能力接口
/// </summary>
public interface IAudioService
{
    /// <summary>
    /// 设置音量 (0-100)
    /// </summary>
    Task<HostApiResponse> SetVolumeAsync(int volume);

    /// <summary>
    /// 获取当前音量
    /// </summary>
    Task<HostApiResponse<int>> GetVolumeAsync();

    /// <summary>
    /// 设置静音状态
    /// </summary>
    Task<HostApiResponse> SetMuteAsync(bool mute);

    /// <summary>
    /// 获取静音状态
    /// </summary>
    Task<HostApiResponse<bool>> GetMuteAsync();

    /// <summary>
    /// 播放系统声音
    /// </summary>
    Task<HostApiResponse> PlaySoundAsync(string soundType);
}
