using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IStorageService
    {
        Task<HostApiResponse<string?>> GetAsync(string key);
        Task<HostApiResponse> SetAsync(string key, string value);
        Task<HostApiResponse> DeleteAsync(string key);
        Task<HostApiResponse<bool>> ContainsKeyAsync(string key);
    }
}
