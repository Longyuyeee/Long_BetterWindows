using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IRegistryService
    {
        Task<HostApiResponse<string?>> ReadValueAsync(string key, string valueName);
        Task<HostApiResponse> WriteValueAsync(string key, string valueName, string value);
        Task<HostApiResponse> DeleteValueAsync(string key, string valueName);
        Task<HostApiResponse> RollbackAsync(string pluginId);
    }
}
