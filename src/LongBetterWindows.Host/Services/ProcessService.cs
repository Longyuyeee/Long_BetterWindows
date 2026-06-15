using System.Diagnostics;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class ProcessService : IProcessService
    {
        public Task<HostApiResponse> StartAsync(string path, string? args = null)
        {
            return Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = args ?? "",
                        UseShellExecute = true,
                    };
                    Process.Start(psi);
                    return HostApiResponse.Success();
                }
                catch (Exception ex) { return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }

        public Task<HostApiResponse<List<ProcessInfo>>> GetRunningProcessesAsync(string? nameFilter = null)
        {
            return Task.Run(() =>
            {
                try
                {
                    var procs = Process.GetProcesses()
                        .Where(p =>
                        {
                            try { return nameFilter == null || p.ProcessName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase); }
                            catch { return false; }
                        })
                        .Take(50)
                        .Select(p =>
                        {
                            try { return new ProcessInfo { Id = p.Id, Name = p.ProcessName, MainWindowTitle = p.MainWindowTitle }; }
                            catch { return new ProcessInfo { Id = p.Id, Name = p.ProcessName }; }
                        })
                        .ToList();
                    return HostApiResponse<List<ProcessInfo>>.Success(procs);
                }
                catch (Exception ex) { return HostApiResponse<List<ProcessInfo>>.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }

        public Task<HostApiResponse> KillAsync(int processId)
        {
            return Task.Run(() =>
            {
                try
                {
                    var proc = Process.GetProcessById(processId);
                    proc.Kill();
                    return HostApiResponse.Success();
                }
                catch (Exception ex) { return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }
    }
}
