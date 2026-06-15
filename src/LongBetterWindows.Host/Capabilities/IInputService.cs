using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IInputService
    {
        Task<HostApiResponse> SendKeyPressAsync(int vkCode);
        Task<HostApiResponse> SendMouseClickAsync(int x, int y, bool rightButton = false);
        Task<HostApiResponse> MoveCursorAsync(int x, int y);
    }
}
