using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IHotKeyService
    {
        Task<HostApiResponse> RegisterAsync(string hotkey, Action callback);
        Task<HostApiResponse> UnregisterAsync(string hotkey);
        Task<HostApiResponse<bool>> IsConflictAsync(string hotkey);
    }
}
