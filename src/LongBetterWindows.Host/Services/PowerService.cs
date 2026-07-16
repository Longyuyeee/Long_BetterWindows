using System.Runtime.InteropServices;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class PowerService : IPowerService
    {
        public Task<HostApiResponse> ShutdownAsync(int delay = 0)
        {
            return Task.Run(() =>
            {
                try
                {
                    ExitWindowsEx(EWX_SHUTDOWN | EWX_FORCE, SHTDN_REASON_MAJOR_OTHER);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> RebootAsync(int delay = 0)
        {
            return Task.Run(() =>
            {
                try
                {
                    ExitWindowsEx(EWX_REBOOT | EWX_FORCE, SHTDN_REASON_MAJOR_OTHER);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> SleepAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    SetSuspendState(false, false, false);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> HibernateAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    SetSuspendState(true, false, false);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> LockScreenAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    LockWorkStation();
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<PowerStatus>> GetPowerStatusAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var status = new SYSTEM_POWER_STATUS();
                    GetSystemPowerStatus(ref status);

                    var powerStatus = new PowerStatus
                    {
                        ACLineStatus = (ACLineStatus)status.ACLineStatus,
                        BatteryFlag = status.BatteryFlag,
                        BatteryLifePercent = status.BatteryLifePercent,
                        BatteryLifeTime = status.BatteryLifeTime,
                        BatteryFullLifeTime = status.BatteryFullLifeTime
                    };

                    return HostApiResponse<PowerStatus>.Success(powerStatus);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<PowerStatus>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> PreventSleepAsync(bool prevent)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (prevent)
                    {
                        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
                    }
                    else
                    {
                        SetThreadExecutionState(ES_CONTINUOUS);
                    }
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        #region Win32 API

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        [DllImport("user32.dll")]
        private static extern void LockWorkStation();

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(ref SYSTEM_POWER_STATUS lpSystemPowerStatus);

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);

        private const uint EWX_SHUTDOWN = 0x00000001;
        private const uint EWX_REBOOT = 0x00000002;
        private const uint EWX_FORCE = 0x00000004;
        private const uint SHTDN_REASON_MAJOR_OTHER = 0x00000000;

        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte Reserved1;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        #endregion
    }
}
