using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class ClipboardService : IClipboardService
    {
        public Task<HostApiResponse<string?>> GetTextAsync()
        {
            return RunOnStaThreadAsync(() =>
            {
                try
                {
                    var text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
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
            return RunOnStaThreadAsync(() =>
            {
                try
                {
                    Clipboard.SetText(text);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> SetImageAsync(BitmapSource image)
        {
            ArgumentNullException.ThrowIfNull(image);
            var transferableImage = image.IsFrozen ? image : image.Clone();
            if (transferableImage.CanFreeze && !transferableImage.IsFrozen)
                transferableImage.Freeze();
            return RunOnStaThreadAsync(() =>
            {
                try
                {
                    Clipboard.SetImage(transferableImage);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<BitmapSource?>> GetImageAsync()
        {
            return RunOnStaThreadAsync(() =>
            {
                try
                {
                    var image = Clipboard.ContainsImage() ? Clipboard.GetImage() : null;
                    if (image?.CanFreeze == true)
                        image.Freeze();
                    return HostApiResponse<BitmapSource?>.Success(image);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<BitmapSource?>.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> ClearAsync()
        {
            return RunOnStaThreadAsync(() =>
            {
                try
                {
                    Clipboard.Clear();
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        private static Task<T> RunOnStaThreadAsync<T>(Func<T> action)
        {
            var completion = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    completion.TrySetResult(action());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            })
            {
                IsBackground = true,
                Name = "Long.Clipboard.STA",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return completion.Task;
        }
    }
}
