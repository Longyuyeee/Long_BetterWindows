using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IFileOpsService
    {
        Task<HostApiResponse> CopyAsync(string source, string dest);
        Task<HostApiResponse> MoveAsync(string source, string dest);
        Task<HostApiResponse> DeleteAsync(string path);
        Task<HostApiResponse<bool>> ExistsAsync(string path);
    }
}
