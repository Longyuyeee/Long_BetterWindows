using System.Diagnostics;
using System.IO;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    public class ShellExecuteService : IShellExecuteService
    {
        public Task<HostApiResponse> OpenUrlAsync(string url)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(url))
                        return HostApiResponse.Failure(ApiErrorCode.InvalidArgument, "URL 不能为空");

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true,
                    });

                    Log.Debug("打开 URL: {Url}", url);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "打开 URL 失败: {Url}", url);
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> OpenFolderAsync(string path)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(path))
                        return HostApiResponse.Failure(ApiErrorCode.InvalidArgument, "路径不能为空");

                    if (!Directory.Exists(path) && !File.Exists(path))
                        return HostApiResponse.Failure(ApiErrorCode.NotFound, $"路径不存在: {path}");

                    Process.Start("explorer.exe", $"\"{path}\"");

                    Log.Debug("打开文件夹: {Path}", path);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "打开文件夹失败: {Path}", path);
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> OpenWithDefaultAsync(string path)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(path))
                        return HostApiResponse.Failure(ApiErrorCode.InvalidArgument, "路径不能为空");

                    if (!File.Exists(path) && !Directory.Exists(path))
                        return HostApiResponse.Failure(ApiErrorCode.NotFound, $"路径不存在: {path}");

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true,
                    });

                    Log.Debug("用默认程序打开: {Path}", path);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "用默认程序打开失败: {Path}", path);
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }
    }
}
