using System.Windows;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class ClipboardService : IClipboardService
    {
        public Task<HostApiResponse<string?>> GetTextAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    string? text = null;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (Clipboard.ContainsText())
                            text = Clipboard.GetText();
                    });
                    return HostApiResponse<string?>.Success(text);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<string?>.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> SetTextAsync(string text)
        {
            return Task.Run(() =>
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        Clipboard.SetText(text));
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> ClearAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(Clipboard.Clear);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
            });
        }
    }
}
