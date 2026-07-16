using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IPowerService
    {
        /// <summary>关机</summary>
        Task<HostApiResponse> ShutdownAsync(int delay = 0);

        /// <summary>重启</summary>
        Task<HostApiResponse> RebootAsync(int delay = 0);

        /// <summary>睡眠</summary>
        Task<HostApiResponse> SleepAsync();

        /// <summary>休眠</summary>
        Task<HostApiResponse> HibernateAsync();

        /// <summary>锁定屏幕</summary>
        Task<HostApiResponse> LockScreenAsync();

        /// <summary>获取电源状态</summary>
        Task<HostApiResponse<PowerStatus>> GetPowerStatusAsync();

        /// <summary>阻止系统休眠</summary>
        Task<HostApiResponse> PreventSleepAsync(bool prevent);
    }

    public class PowerStatus
    {
        public ACLineStatus ACLineStatus { get; set; }
        public byte BatteryFlag { get; set; }
        public byte BatteryLifePercent { get; set; }
        public int BatteryLifeTime { get; set; }
        public int BatteryFullLifeTime { get; set; }
    }

    public enum ACLineStatus : byte
    {
        Offline = 0,
        Online = 1,
        Unknown = 255
    }
}
