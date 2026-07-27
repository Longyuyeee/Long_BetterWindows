using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities;

public interface IPluginSettingsService
{
    Task<HostApiResponse<string?>> GetAsync(string key);

    Task<HostApiResponse> SetAsync(string key, string value);
}
