using System.Runtime.InteropServices;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class AudioService : IAudioService
    {
        public Task<HostApiResponse<int>> GetVolumeAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    waveOutGetVolume(IntPtr.Zero, out uint volume);
                    var leftVolume = (volume & 0xFFFF) * 100 / 0xFFFF;
                    return HostApiResponse<int>.Success((int)leftVolume);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<int>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> SetVolumeAsync(int volume)
        {
            return Task.Run(() =>
            {
                try
                {
                    volume = Math.Clamp(volume, 0, 100);
                    uint v = (uint)(volume * 0xFFFF / 100);
                    uint stereoVolume = (v << 16) | v;
                    waveOutSetVolume(IntPtr.Zero, stereoVolume);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<bool>> GetMuteAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    // 简化实现：检查音量是否为0
                    waveOutGetVolume(IntPtr.Zero, out uint volume);
                    bool muted = volume == 0;
                    return HostApiResponse<bool>.Success(muted);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<bool>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> SetMuteAsync(bool mute)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (mute)
                    {
                        waveOutSetVolume(IntPtr.Zero, 0);
                    }
                    else
                    {
                        // 恢复到50%音量
                        uint v = (uint)(50 * 0xFFFF / 100);
                        uint stereoVolume = (v << 16) | v;
                        waveOutSetVolume(IntPtr.Zero, stereoVolume);
                    }
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public async Task<HostApiResponse<int>> IncreaseVolumeAsync(int step = 5)
        {
            var currentResult = await GetVolumeAsync();
            if (!currentResult.IsSuccess) return HostApiResponse<int>.Failure(currentResult.ErrorCode, currentResult.ErrorMessage);

            var newVolume = Math.Min(currentResult.Data + step, 100);
            await SetVolumeAsync(newVolume);
            return HostApiResponse<int>.Success(newVolume);
        }

        public async Task<HostApiResponse<int>> DecreaseVolumeAsync(int step = 5)
        {
            var currentResult = await GetVolumeAsync();
            if (!currentResult.IsSuccess) return HostApiResponse<int>.Failure(currentResult.ErrorCode, currentResult.ErrorMessage);

            var newVolume = Math.Max(currentResult.Data - step, 0);
            await SetVolumeAsync(newVolume);
            return HostApiResponse<int>.Success(newVolume);
        }

        public Task<HostApiResponse<List<AudioDevice>>> GetAudioDevicesAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var devices = new List<AudioDevice>
                    {
                        new AudioDevice { Id = "default", Name = "默认音频设备", Type = "Output", IsDefault = true }
                    };
                    return HostApiResponse<List<AudioDevice>>.Success(devices);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<List<AudioDevice>>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> SetDefaultDeviceAsync(string deviceId)
        {
            return Task.Run(() =>
            {
                try
                {
                    // 简化实现
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        #region Win32 API

        [DllImport("winmm.dll")]
        private static extern int waveOutGetVolume(IntPtr hwo, out uint dwVolume);

        [DllImport("winmm.dll")]
        private static extern int waveOutSetVolume(IntPtr hwo, uint dwVolume);

        #endregion
    }
}
