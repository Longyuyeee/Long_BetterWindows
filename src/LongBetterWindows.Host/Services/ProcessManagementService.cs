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

    public Task<HostApiResponse<int>> StartAsync(string path, string? args = null)
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
                    return HostApiResponse<int>.Failure(
                        ApiErrorCode.Unknown, "无法启动进程");
                }

                _logger.Information("已启动进程: {Path} (PID: {Pid})", path, process.Id);
                return HostApiResponse<int>.Success(process.Id);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "启动进程失败: {Path}", path);
                return HostApiResponse<int>.Failure(
                    ApiErrorCode.Unknown, ex.Message);
            }
        });
    }

    public Task<HostApiResponse> KillAsync(int pid)
    {
        return Task.Run(() =>
        {
            try
            {
                var process = Process.GetProcessById(pid);
                process.Kill();
                _logger.Information("已结束进程: PID {Pid}", pid);
                return HostApiResponse.Success();
            }
            catch (ArgumentException)
            {
                return HostApiResponse.Failure(
                    ApiErrorCode.InvalidArgument, "进程不存在");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "结束进程失败: PID {Pid}", pid);
                return HostApiResponse.Failure(
                    ApiErrorCode.Unknown, ex.Message);
            }
        });
    }

    public Task<HostApiResponse<List<ProcessInfo>>> GetListAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var processes = Process.GetProcesses()
                    .Select(p => new ProcessInfo
                    {
                        Pid = p.Id,
                        Name = p.ProcessName,
                        MemoryMB = p.WorkingSet64 / 1024 / 1024
                    })
                    .OrderByDescending(p => p.MemoryMB)
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

    public Task<HostApiResponse<bool>> IsRunningAsync(string processName)
    {
        return Task.Run(() =>
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                var isRunning = processes.Length > 0;

                foreach (var p in processes)
                {
                    p.Dispose();
                }

                return HostApiResponse<bool>.Success(isRunning);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "检查进程状态失败: {ProcessName}", processName);
                return HostApiResponse<bool>.Failure(
                    ApiErrorCode.Unknown, ex.Message);
            }
        });
    }
}
