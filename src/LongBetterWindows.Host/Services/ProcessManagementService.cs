using System.Diagnostics;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using Serilog;

namespace LongBetterWindows.Host.Services;

/// <summary>
/// 进程管理服务
/// </summary>
public class ProcessManagementService : IProcessService
{
    private readonly ILogger _logger = Log.ForContext<ProcessManagementService>();

    public Task<HostApiResponse> StartAsync(string path, string? args = null)
    {
        return Task.Run(() =>
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = args ?? string.Empty,
                    UseShellExecute = true
                };

                var process = Process.Start(startInfo);
                if (process == null)
                {
                    return HostApiResponse.Failure(
                        ApiErrorCode.Unknown, "无法启动进程");
                }

                _logger.Information("已启动进程: {Path} (PID: {Pid})", path, process.Id);
                return HostApiResponse.Success();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "启动进程失败: {Path}", path);
                return HostApiResponse.Failure(
                    ApiErrorCode.Unknown, ex.Message);
            }
        });
    }

    public Task<HostApiResponse<List<ProcessInfo>>> GetRunningProcessesAsync(string? nameFilter = null)
    {
        return Task.Run(() =>
        {
            try
            {
                var processes = Process.GetProcesses()
                    .Where(p => string.IsNullOrEmpty(nameFilter) ||
                               p.ProcessName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                    .Select(p => new ProcessInfo
                    {
                        Id = p.Id,
                        Name = p.ProcessName,
                        MainWindowTitle = p.MainWindowTitle
                    })
                    .Take(50)
                    .ToList();

                return HostApiResponse<List<ProcessInfo>>.Success(processes);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "获取进程列表失败");
                return HostApiResponse<List<ProcessInfo>>.Failure(
                    ApiErrorCode.Unknown, ex.Message);
            }
        });
    }

    public Task<HostApiResponse> KillAsync(int processId)
    {
        return Task.Run(() =>
        {
            try
            {
                var process = Process.GetProcessById(processId);
                process.Kill();
                _logger.Information("已结束进程: PID {Pid}", processId);
                return HostApiResponse.Success();
            }
            catch (ArgumentException)
            {
                return HostApiResponse.Failure(
                    ApiErrorCode.InvalidArgument, "进程不存在");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "结束进程失败: PID {Pid}", processId);
                return HostApiResponse.Failure(
                    ApiErrorCode.Unknown, ex.Message);
            }
        });
    }
}
