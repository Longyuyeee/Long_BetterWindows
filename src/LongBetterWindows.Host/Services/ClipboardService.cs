using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class ClipboardService : IClipboardService, IDisposable
    {
        private HwndSource? _hwndSource;
        private IntPtr _nextClipboardViewer;
        private bool _isMonitoring;
        private string? _lastClipboardText;

        public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;
        public bool IsMonitoring => _isMonitoring;

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

        public Task<HostApiResponse> StartMonitoringAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    if (_isMonitoring)
                        return HostApiResponse.Success();

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var mainWindow = Application.Current.MainWindow;
                        if (mainWindow == null)
                            throw new InvalidOperationException("主窗口未初始化");

                        var windowHelper = new WindowInteropHelper(mainWindow);
                        _hwndSource = HwndSource.FromHwnd(windowHelper.Handle);

                        if (_hwndSource != null)
                        {
                            _hwndSource.AddHook(WndProc);
                            _nextClipboardViewer = SetClipboardViewer(_hwndSource.Handle);
                            _isMonitoring = true;

                            // 记录当前剪贴板内容
                            if (Clipboard.ContainsText())
                                _lastClipboardText = Clipboard.GetText();
                        }
                    });

                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> StopMonitoringAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    if (!_isMonitoring)
                        return HostApiResponse.Success();

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_hwndSource != null)
                        {
                            ChangeClipboardChain(_hwndSource.Handle, _nextClipboardViewer);
                            _hwndSource.RemoveHook(WndProc);
                            _hwndSource = null;
                        }
                        _isMonitoring = false;
                    });

                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
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
            StopMonitoringAsync().Wait();
        }

        #region Win32 API

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardViewer(IntPtr hWndNewViewer);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeClipboardChain(IntPtr hWndRemove, IntPtr hWndNewNext);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        #endregion
    }
}
