using System.Windows;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Views;

namespace LongBetterWindows.Host.Services
{
    public class NotificationService : INotificationService
    {
        public Task<HostApiResponse> ShowAsync(string title, string body)
        {
            Application.Current.Dispatcher.Invoke(() =>
                FloatingHudWindow.ShowToast($"{title}\n{body}"));
            return Task.FromResult(HostApiResponse.Success());
        }
    }
}
