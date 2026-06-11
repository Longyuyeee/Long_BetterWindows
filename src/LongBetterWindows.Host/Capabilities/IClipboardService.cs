using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IClipboardService
    {
        Task<HostApiResponse<string?>> GetTextAsync();
        Task<HostApiResponse> SetTextAsync(string text);
        Task<HostApiResponse> ClearAsync();
    }
}
