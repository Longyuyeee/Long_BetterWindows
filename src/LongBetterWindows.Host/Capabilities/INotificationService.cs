using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface INotificationService
    {
        Task<HostApiResponse> ShowAsync(string title, string body);
    }
}
