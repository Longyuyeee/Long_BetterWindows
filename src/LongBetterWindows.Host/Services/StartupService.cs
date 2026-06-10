using System.IO;
using Microsoft.Win32;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    public class StartupService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "LongBetterWindows";

        public bool IsAutoStartEnabled
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                    return key?.GetValue(AppName) != null;
                }
                catch { return false; }
            }
        }

        public void SetAutoStart(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (key == null) return;

                if (enabled)
                {
                    var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "LongBetterWindows.Host.exe");
                    key.SetValue(AppName, $"\"{exePath}\"");
                    Log.Information("开机自启已启用");
                }
                else
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                    Log.Information("开机自启已禁用");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "开机自启设置失败");
            }
        }
    }
}
