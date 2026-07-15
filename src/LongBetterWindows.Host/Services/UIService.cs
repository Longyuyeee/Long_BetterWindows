using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using Microsoft.Web.WebView2.Wpf;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    public class UIService : IUICapability
    {
        private readonly Dictionary<string, Window> _windows = new();
        private readonly object _lock = new();

        public async Task<HostApiResponse<string>> CreateWindowAsync(
            string title,
            string htmlContent,
            int width = 600,
            int height = 400,
            bool resizable = true)
        {
            ArgumentNullException.ThrowIfNull(title);
            ArgumentNullException.ThrowIfNull(htmlContent);

            if (width < 200 || width > 3840)
                return HostApiResponse<string>.Failure(ApiErrorCode.InvalidArgument, "窗口宽度必须在 200-3840 之间");

            if (height < 150 || height > 2160)
                return HostApiResponse<string>.Failure(ApiErrorCode.InvalidArgument, "窗口高度必须在 150-2160 之间");

            var windowId = $"window_{Guid.NewGuid():N}";

            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var window = new Window
                    {
                        Title = title,
                        Width = width,
                        Height = height,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        Background = new SolidColorBrush(Color.FromRgb(30, 31, 34)),
                        ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.NoResize
                    };

                    var webView = new WebView2();
                    window.Content = webView;

                    window.Loaded += async (_, _) =>
                    {
                        try
                        {
                            await webView.EnsureCoreWebView2Async();
                            webView.CoreWebView2.NavigateToString(WrapHtmlContent(htmlContent, title));
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "WebView2 初始化失败");
                        }
                    };

                    window.Closed += (_, _) =>
                    {
                        lock (_lock)
                        {
                            _windows.Remove(windowId);
                        }
                        webView.Dispose();
                    };

                    lock (_lock)
                    {
                        _windows[windowId] = window;
                    }

                    window.Show();
                });

                Log.Information("创建自定义窗口: {WindowId} ({Title})", windowId, title);
                return HostApiResponse<string>.Success(windowId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "创建窗口失败");
                return HostApiResponse<string>.Failure(ApiErrorCode.Unknown, ex.Message);
            }
        }

        public async Task<HostApiResponse<bool>> ConfirmAsync(string message, string title = "确认")
        {
            ArgumentNullException.ThrowIfNull(message);

            try
            {
                var result = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var msgResult = MessageBox.Show(
                        message,
                        title,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    return msgResult == MessageBoxResult.Yes;
                });

                return HostApiResponse<bool>.Success(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "显示确认对话框失败");
                return HostApiResponse<bool>.Failure(ApiErrorCode.Unknown, ex.Message);
            }
        }

        public async Task<HostApiResponse<string?>> PromptAsync(
            string message,
            string title = "输入",
            string defaultValue = "")
        {
            ArgumentNullException.ThrowIfNull(message);

            try
            {
                var result = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new InputDialog(message, title, defaultValue);
                    var confirmed = dialog.ShowDialog() == true;
                    return confirmed ? dialog.InputText : null;
                });

                return HostApiResponse<string?>.Success(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "显示输入对话框失败");
                return HostApiResponse<string?>.Failure(ApiErrorCode.Unknown, ex.Message);
            }
        }

        public async Task<HostApiResponse<int>> SelectAsync(
            string message,
            string[] options,
            string title = "选择")
        {
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(options);

            if (options.Length == 0)
                return HostApiResponse<int>.Failure(ApiErrorCode.InvalidArgument, "选项列表不能为空");

            try
            {
                var result = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new SelectDialog(message, options, title);
                    var confirmed = dialog.ShowDialog() == true;
                    return confirmed ? dialog.SelectedIndex : -1;
                });

                return HostApiResponse<int>.Success(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "显示选择对话框失败");
                return HostApiResponse<int>.Failure(ApiErrorCode.Unknown, ex.Message);
            }
        }

        public Task<HostApiResponse> CloseWindowAsync(string windowId)
        {
            ArgumentNullException.ThrowIfNull(windowId);

            Window? window;
            lock (_lock)
            {
                if (!_windows.TryGetValue(windowId, out window))
                    return Task.FromResult(HostApiResponse.Failure(ApiErrorCode.NotFound, "窗口不存在"));
            }

            try
            {
                return Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    window.Close();
                    Log.Information("关闭窗口: {WindowId}", windowId);
                    return HostApiResponse.Success();
                }).Task;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "关闭窗口失败");
                return Task.FromResult(HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message));
            }
        }

        public Task<HostApiResponse> SendMessageToWindowAsync(string windowId, string message)
        {
            ArgumentNullException.ThrowIfNull(windowId);
            ArgumentNullException.ThrowIfNull(message);

            Window? window;
            lock (_lock)
            {
                if (!_windows.TryGetValue(windowId, out window))
                    return Task.FromResult(HostApiResponse.Failure(ApiErrorCode.NotFound, "窗口不存在"));
            }

            try
            {
                var task = Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (window.Content is WebView2 webView && webView.CoreWebView2 != null)
                    {
                        var script = $"window.dispatchEvent(new CustomEvent('hostMessage', {{ detail: {message} }}));";
                        await webView.CoreWebView2.ExecuteScriptAsync(script);
                    }
                    return HostApiResponse.Success();
                });
                return task.Task.Unwrap();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "发送消息到窗口失败");
                return Task.FromResult(HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message));
            }
        }

        private string WrapHtmlContent(string content, string title)
        {
            // 如果已经是完整 HTML，直接返回
            if (content.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                content.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                return content;
            }

            // 否则包装为完整页面
            return $@"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{title}</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
            background: #1E1F22;
            color: #E8E8E8;
            padding: 20px;
        }}
    </style>
</head>
<body>
{content}
</body>
</html>";
        }

        /// <summary>
        /// 简单输入对话框
        /// </summary>
        private class InputDialog : Window
        {
            private readonly TextBox _textBox;

            public string InputText => _textBox.Text;

            public InputDialog(string message, string title, string defaultValue)
            {
                Title = title;
                Width = 400;
                Height = 180;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Background = new SolidColorBrush(Color.FromRgb(30, 31, 34));
                ResizeMode = ResizeMode.NoResize;

                var grid = new Grid { Margin = new Thickness(20) };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = message,
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(0, 0, 0, 12),
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetRow(label, 0);
                grid.Children.Add(label);

                _textBox = new TextBox
                {
                    Text = defaultValue,
                    Padding = new Thickness(8),
                    Background = new SolidColorBrush(Color.FromRgb(51, 51, 56)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(58, 58, 61)),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Top,
                    Height = 32
                };
                Grid.SetRow(_textBox, 1);
                grid.Children.Add(_textBox);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 12, 0, 0)
                };
                Grid.SetRow(buttonPanel, 2);

                var okButton = new Button
                {
                    Content = "确定",
                    Width = 80,
                    Height = 28,
                    Background = new SolidColorBrush(Color.FromRgb(0, 122, 255)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                okButton.Click += (_, _) => { DialogResult = true; Close(); };
                buttonPanel.Children.Add(okButton);

                var cancelButton = new Button
                {
                    Content = "取消",
                    Width = 80,
                    Height = 28,
                    Margin = new Thickness(8, 0, 0, 0),
                    Background = new SolidColorBrush(Color.FromRgb(58, 58, 61)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                cancelButton.Click += (_, _) => { DialogResult = false; Close(); };
                buttonPanel.Children.Add(cancelButton);

                grid.Children.Add(buttonPanel);
                Content = grid;

                _textBox.Focus();
                _textBox.SelectAll();
            }
        }

        /// <summary>
        /// 简单选择对话框
        /// </summary>
        private class SelectDialog : Window
        {
            private readonly ListBox _listBox;

            public int SelectedIndex => _listBox.SelectedIndex;

            public SelectDialog(string message, string[] options, string title)
            {
                Title = title;
                Width = 400;
                Height = 300;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Background = new SolidColorBrush(Color.FromRgb(30, 31, 34));
                ResizeMode = ResizeMode.NoResize;

                var grid = new Grid { Margin = new Thickness(20) };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = message,
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(0, 0, 0, 12),
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetRow(label, 0);
                grid.Children.Add(label);

                _listBox = new ListBox
                {
                    Background = new SolidColorBrush(Color.FromRgb(51, 51, 56)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(58, 58, 61)),
                    BorderThickness = new Thickness(1)
                };
                foreach (var option in options)
                {
                    _listBox.Items.Add(option);
                }
                if (_listBox.Items.Count > 0)
                    _listBox.SelectedIndex = 0;

                Grid.SetRow(_listBox, 1);
                grid.Children.Add(_listBox);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 12, 0, 0)
                };
                Grid.SetRow(buttonPanel, 2);

                var okButton = new Button
                {
                    Content = "确定",
                    Width = 80,
                    Height = 28,
                    Background = new SolidColorBrush(Color.FromRgb(0, 122, 255)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                okButton.Click += (_, _) => { DialogResult = true; Close(); };
                buttonPanel.Children.Add(okButton);

                var cancelButton = new Button
                {
                    Content = "取消",
                    Width = 80,
                    Height = 28,
                    Margin = new Thickness(8, 0, 0, 0),
                    Background = new SolidColorBrush(Color.FromRgb(58, 58, 61)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                cancelButton.Click += (_, _) => { DialogResult = false; Close(); };
                buttonPanel.Children.Add(cancelButton);

                grid.Children.Add(buttonPanel);
                Content = grid;

                _listBox.Focus();
            }
        }
    }
}
