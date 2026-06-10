using System.Runtime.InteropServices;
using Microsoft.Win32;
using LongBetterWindows.Host.Contracts;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    public class ColumnInjectionService
    {
        private const string CommentProperty = "System.Comment";

        private static readonly string[] FolderTypeKeys =
        {
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell\{5C4F28B5-F869-4E84-8E60-F11DB97C5CC7}",
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell\{7D49D726-3C21-4F05-99AA-FDC2C9474656}",
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell\{885A186E-A440-4ADA-812B-DB871B942259}",
        };

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private const uint SHCNE_UPDATEDIR = 0x00001000;
        private const uint SHCNE_EXTENDED_EVENT = 0x04000000;
        private const uint SHCNF_FLUSH = 0x1000;

        public bool IsCommentColumnEnabled
        {
            get
            {
                try
                {
                    var key = FolderTypeKeys.FirstOrDefault();
                    if (key == null) return false;

                    var fullPath = $@"HKEY_CURRENT_USER\{key}";
                    var columns = Registry.GetValue(fullPath, "ColumnList", null) as string;

                    return columns != null && columns.Contains(CommentProperty);
                }
                catch
                {
                    return false;
                }
            }
        }

        public async Task<HostApiResponse> EnableCommentColumnAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    bool anySuccess = false;

                    foreach (var keyPath in FolderTypeKeys)
                    {
                        try
                        {
                            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true)
                                ?? Registry.CurrentUser.CreateSubKey(keyPath);

                            if (key == null) continue;

                            var existing = key.GetValue("ColumnList") as string ?? "";
                            var columns = existing.Split(';', StringSplitOptions.RemoveEmptyEntries)
                                .Select(c => c.Trim())
                                .ToList();

                            if (!columns.Any(c => c.Contains(CommentProperty, StringComparison.OrdinalIgnoreCase)))
                            {
                                columns.Insert(0, $"prop:{CommentProperty}");
                                key.SetValue("ColumnList", string.Join(";", columns));
                                Log.Information("备注列已注入: {Key}", keyPath);
                                anySuccess = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "列注入失败: {Key}", keyPath);
                        }
                    }

                    if (anySuccess)
                    {
                        RefreshExplorer();
                        Log.Information("Explorer 刷新通知已发送");
                    }

                    return anySuccess
                        ? HostApiResponse.Success()
                        : HostApiResponse.Failure(ApiErrorCode.RegistryAccessDenied,
                            "无法修改注册表以启用备注列。");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "备注列注入失败");
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public async Task<HostApiResponse> DisableCommentColumnAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    foreach (var keyPath in FolderTypeKeys)
                    {
                        try
                        {
                            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
                            if (key == null) continue;

                            var existing = key.GetValue("ColumnList") as string ?? "";
                            var columns = existing.Split(';', StringSplitOptions.RemoveEmptyEntries)
                                .Where(c => !c.Contains(CommentProperty, StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            key.SetValue("ColumnList", string.Join(";", columns));
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "列移除失败: {Key}", keyPath);
                        }
                    }

                    RefreshExplorer();
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "备注列移除失败");
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        private static void RefreshExplorer()
        {
            SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
            SHChangeNotify(SHCNE_EXTENDED_EVENT, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
