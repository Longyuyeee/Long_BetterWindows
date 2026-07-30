using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public class ProcessInfo
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string MainWindowTitle { get; init; } = "";
    }

    public interface IProcessService
    {
        Task<HostApiResponse> StartAsync(string path, string? args = null);
        Task<HostApiResponse<List<ProcessInfo>>> GetRunningProcessesAsync(string? nameFilter = null);
        Task<HostApiResponse> KillAsync(int processId);
        Task<HostApiResponse> KillVerifiedAsync(
            int processId,
            string expectedName,
            string expectedIdentity);
    }
}
