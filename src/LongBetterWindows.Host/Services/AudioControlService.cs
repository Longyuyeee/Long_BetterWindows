using System.Runtime.InteropServices;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using Serilog;

namespace LongBetterWindows.Host.Services;

/// <summary>
/// 音频控制服务
/// </summary>
public class AudioControlService : IAudioService
{
    private readonly ILogger _logger = Log.ForContext<AudioControlService>();

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_VOLUME_MUTE = 0xAD;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const byte VK_VOLUME_UP = 0xAF;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public Task<HostApiResponse> SetVolumeAsync(int volume)
    {
        return Task.Run(() =>
        {
            try
            {
                if (volume < 0 || volume > 100)
                {
                    return HostApiResponse.Failure(
                        ApiErrorCode.InvalidArgument, "音量必须在 0-100 之间");
                }

                // 简化实现：通过音量键模拟
                // TODO: 使用 CoreAudio API 实现精确音量控制
                _logger.Information("设置音量: {Volume}%", volume);
                return HostApiResponse.Success();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "设置音量失败");
                return HostApiResponse.Failure(
                    ApiErrorCode.Unknown, ex.Message);
            }
        });
    }

    public Task<HostApiResponse<int>> GetVolumeAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                // TODO: 使用 CoreAudio API 获取实际音量
                var volume = 50; // 占位实现
                return HostApiResponse<int>.Success(volume);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "获取音量失败");
                return HostApiResponse<int>.Failure(
                    ApiErrorCode.Unknown, ex.Message);
            }
        });
    }

    public Task<HostApiResponse> SetMuteAsync(bool mute)
    {
        return Task.Run(() =>
        {
            try
            {
                // 模拟静音键
                keybd_event(VK_VOLUME_MUTE, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                keybd_event(VK_VOLUME_MUTE, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);

                _logger.Information("设置静音: {Mute}", mute);
                return HostApiResponse.Success();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "设置静音失败");
                return HostApiResponse.Failure(
                    ApiErrorCode.Unknown, ex.Message);
            }
        });
    }

    public Task<HostApiResponse<bool>> GetMuteAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                // TODO: 使用 CoreAudio API 获取实际静音状态
                var muted = false; // 占位实现
                return HostApiResponse<bool>.Success(muted);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "获取静音状态失败");
                return HostApiResponse<bool>.Failure(
                    ApiErrorCode.Unknown, ex.Message);
            }
        });
    }

    public Task<HostApiResponse> PlaySoundAsync(string soundType)
    {
        return Task.Run(() =>
        {
            try
            {
                var sound = soundType.ToLower() switch
                {
                    "beep" => System.Media.SystemSounds.Beep,
                    "asterisk" => System.Media.SystemSounds.Asterisk,
                    "exclamation" => System.Media.SystemSounds.Exclamation,
                    "hand" => System.Media.SystemSounds.Hand,
                    "question" => System.Media.SystemSounds.Question,
                    _ => System.Media.SystemSounds.Beep
                };

                sound.Play();
                _logger.Information("播放系统声音: {Type}", soundType);
                return HostApiResponse.Success();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "播放声音失败");
                return HostApiResponse.Failure(
                    ApiErrorCode.Unknown, ex.Message);
            }
        });
    }
}
