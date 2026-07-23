using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class ClipboardService : IClipboardService, IDisposable
    {
        private HwndSource? _hwndSource;
        private IntPtr _nextClipboardViewer;
        private readonly ClipboardMonitoringLeaseManager _monitorLeases;
        private bool _isMonitoring;
        private string? _lastClipboardText;

        public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;
        public bool IsMonitoring => _monitorLeases.IsActive && _isMonitoring;

        public ClipboardService()
        {
            _monitorLeases = new ClipboardMonitoringLeaseManager(
                StartMonitoringCoreAsync,
                StopMonitoringCoreAsync);
        }

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

        public Task<HostApiResponse> StartMonitoringAsync() =>
            _monitorLeases.AcquireAsync();

        public Task<HostApiResponse> StopMonitoringAsync() =>
            _monitorLeases.ReleaseAsync();

        private Task<HostApiResponse> StartMonitoringCoreAsync()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return Task.FromResult(HostApiResponse.Failure(
                    ApiErrorCode.Unknown, "主窗口尚未初始化"));
            if (dispatcher.CheckAccess())
                return Task.FromResult(StartMonitoringCore());
            return dispatcher.InvokeAsync(StartMonitoringCore).Task;
        }

        private HostApiResponse StartMonitoringCore()
        {
            try
            {
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow == null)
                    throw new InvalidOperationException("主窗口未初始化");

                var windowHelper = new WindowInteropHelper(mainWindow);
                _hwndSource = HwndSource.FromHwnd(windowHelper.Handle);
                if (_hwndSource == null)
                    return HostApiResponse.Failure(
                        ApiErrorCode.Unknown, "主窗口句柄不可用");

                _hwndSource.AddHook(WndProc);
                _nextClipboardViewer = SetClipboardViewer(_hwndSource.Handle);
                _isMonitoring = true;

                if (Clipboard.ContainsText())
                    _lastClipboardText = Clipboard.GetText();
                return HostApiResponse.Success();
            }
            catch (Exception ex)
            {
                return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
            }
        }

        private Task<HostApiResponse> StopMonitoringCoreAsync()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                _isMonitoring = false;
                return Task.FromResult(HostApiResponse.Success());
            }
            if (dispatcher.CheckAccess())
                return Task.FromResult(StopMonitoringCore());
            return dispatcher.InvokeAsync(StopMonitoringCore).Task;
        }

        private HostApiResponse StopMonitoringCore()
        {
            try
            {
                if (_hwndSource != null)
                {
                    ChangeClipboardChain(_hwndSource.Handle, _nextClipboardViewer);
                    _hwndSource.RemoveHook(WndProc);
                    _hwndSource = null;
                }
                _nextClipboardViewer = IntPtr.Zero;
                _isMonitoring = false;
                return HostApiResponse.Success();
            }
            catch (Exception ex)
            {
                return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_DRAWCLIPBOARD = 0x0308;
            const int WM_CHANGECBCHAIN = 0x030D;

            switch (msg)
            {
                case WM_DRAWCLIPBOARD:
                    OnClipboardChanged();
                    if (_nextClipboardViewer != IntPtr.Zero)
                        SendMessage(_nextClipboardViewer, msg, wParam, lParam);
                    handled = true;
                    break;

                case WM_CHANGECBCHAIN:
                    if (wParam == _nextClipboardViewer)
                        _nextClipboardViewer = lParam;
                    else if (_nextClipboardViewer != IntPtr.Zero)
                        SendMessage(_nextClipboardViewer, msg, wParam, lParam);
                    handled = true;
                    break;
            }

            return IntPtr.Zero;
        }

        private void OnClipboardChanged()
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var contentType = ClipboardContentType.Unknown;
                    string? text = null;

                    if (Clipboard.ContainsText())
                    {
                        text = Clipboard.GetText();
                        contentType = ClipboardContentType.Text;

                        // 防止重复触发
                        if (text == _lastClipboardText)
                            return;
                        _lastClipboardText = text;
                    }
                    else if (Clipboard.ContainsImage())
                    {
                        contentType = ClipboardContentType.Image;
                    }
                    else if (Clipboard.ContainsFileDropList())
                    {
                        contentType = ClipboardContentType.Files;
                    }

                    ClipboardChanged?.Invoke(this, new ClipboardChangedEventArgs
                    {
                        Text = text,
                        ContentType = contentType,
                        Timestamp = DateTime.Now
                    });
                });
            }
            catch (Exception ex)
            {
                // 记录日志但不阻止程序运行
                System.Diagnostics.Debug.WriteLine($"剪贴板监听错误: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _monitorLeases.StopAllAsync().GetAwaiter().GetResult();
            _monitorLeases.Dispose();
        }

        #region Win32 API

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardViewer(IntPtr hWndNewViewer);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeClipboardChain(IntPtr hWndRemove, IntPtr hWndNewNext);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        #endregion

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
