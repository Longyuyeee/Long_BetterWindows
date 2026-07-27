using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Services;

internal sealed class PluginSettingsService(
    PluginRegistry registry,
    string pluginId) : IPluginSettingsService
{
    public Task<HostApiResponse<string?>> GetAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.FromResult(HostApiResponse<string?>.Failure(
                ApiErrorCode.InvalidArgument,
                "Plugin setting key cannot be empty."));
        }

        var entry = registry.Get(pluginId);
        return Task.FromResult(entry is null
            ? HostApiResponse<string?>.Failure(
                ApiErrorCode.NotFound,
                $"Plugin '{pluginId}' is not registered.")
            : HostApiResponse<string?>.Success(entry.GetSetting(key)));
    }

    public Task<HostApiResponse> SetAsync(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.FromResult(HostApiResponse.Failure(
                ApiErrorCode.InvalidArgument,
                "Plugin setting key cannot be empty."));
        }

        var entry = registry.Get(pluginId);
        return Task.FromResult(entry is null
            ? HostApiResponse.Failure(
                ApiErrorCode.NotFound,
                $"Plugin '{pluginId}' is not registered.")
            : entry.SetSetting(key, value));
    }
}
