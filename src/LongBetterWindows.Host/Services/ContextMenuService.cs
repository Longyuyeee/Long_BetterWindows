using System.IO;
using Microsoft.Win32;
using LongBetterWindows.Host.Contracts;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    public class ContextMenuService
    {
        private const string MenuKeyPath = @"Software\Classes\Directory\shell\LongNote";
        private const string BackgroundKeyPath = @"Software\Classes\Directory\Background\shell\LongNote";
        private const string MenuName = "备注此文件夹";

        private string GetExePath()
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                exePath = Path.Combine(AppContext.BaseDirectory, "LongBetterWindows.Host.exe");
            return exePath;
        }

        public bool IsRegistered
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(MenuKeyPath);
                    return key != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public async Task<HostApiResponse> RegisterAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var exePath = GetExePath();
                    RegisterMenuKey(MenuKeyPath, exePath);
                    RegisterMenuKey(BackgroundKeyPath, exePath);

                    Log.Information("右键菜单已注册: {MenuName}", MenuName);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "右键菜单注册失败");
                    return HostApiResponse.Failure(ApiErrorCode.RegistryAccessDenied, ex.Message);
                }
            });
        }

        public async Task<HostApiResponse> UnregisterAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(MenuKeyPath, throwOnMissingSubKey: false);
                    Registry.CurrentUser.DeleteSubKeyTree(BackgroundKeyPath, throwOnMissingSubKey: false);

                    Log.Information("右键菜单已移除");
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "右键菜单移除失败");
                    return HostApiResponse.Failure(ApiErrorCode.RegistryAccessDenied, ex.Message);
                }
            });
        }

        private void RegisterMenuKey(string keyPath, string exePath)
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            if (key == null) return;

            key.SetValue(null, MenuName);
            key.SetValue("Icon", $"\"{exePath}\",0");

            using var cmdKey = key.CreateSubKey("command");
            cmdKey?.SetValue(null, $"\"{exePath}\" --note \"%1\"");
        }
    }
}
