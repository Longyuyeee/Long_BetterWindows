using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IADSService
    {
        Task<HostApiResponse<string>> ReadAsync(string filePath, string streamName);
        Task<HostApiResponse> WriteAsync(string filePath, string streamName, string content);
        Task<HostApiResponse> DeleteAsync(string filePath, string streamName);
        Task<HostApiResponse<bool>> ExistsAsync(string filePath, string streamName);
        Task<HostApiResponse<bool>> IsNTFSVolumeAsync(string filePath);
    }
}
