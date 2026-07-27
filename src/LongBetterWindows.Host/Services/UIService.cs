using System.Net;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Views;
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
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.NoResize
                    };
                    ApplyWindowTheme(window);

                    var webView = new WebView2();
                    window.Content = webView;
                    Action<bool> themeChanged = isLight =>
                        _ = ApplyWebThemeAsync(webView, isLight);
                    webView.NavigationCompleted += async (_, args) =>
                    {
                        if (args.IsSuccess)
                            await ApplyWebThemeAsync(webView, App.IsLightTheme);
                    };

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
                        App.ThemeChanged -= themeChanged;
                        lock (_lock)
                        {
                            _windows.Remove(windowId);
                        }
                        webView.Dispose();
                    };
                    App.ThemeChanged += themeChanged;

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
                    ThemedMessageDialog.ShowConfirmation(
                        owner: null,
                        message,
                        title));

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

        private static string WrapHtmlContent(string content, string title)
        {
            // 如果已经是完整 HTML，直接返回
            if (content.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                content.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                return content;
            }

            // 否则包装为完整页面
            var initialTheme = App.IsLightTheme ? "light" : "dark";
            return $@"<!DOCTYPE html>
<html lang=""zh-CN"" data-long-theme=""{initialTheme}"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{WebUtility.HtmlEncode(title)}</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        :root {{
            color-scheme: dark;
            --long-bg: #0B0D12;
            --long-text: #F4F6FB;
        }}
        :root[data-long-theme=""light""] {{
            color-scheme: light;
            --long-bg: #F5F7FB;
            --long-text: #171A22;
        }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
            background: var(--long-bg);
            color: var(--long-text);
            padding: 20px;
        }}
    </style>
</head>
<body>
{content}
</body>
</html>";
        }

        internal static Window CreatePromptDialogForQuality()
            => new InputDialog("Theme probe", "Prompt", "Value");

        private static void ApplyWindowTheme(Window window)
        {
            window.SetResourceReference(
                Control.BackgroundProperty,
                "Long.Brush.Background.Base");
            if (Application.Current.MainWindow is { IsVisible: true } owner
                && !ReferenceEquals(owner, window))
            {
                window.Owner = owner;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private static void ApplyControlStyle(
            FrameworkElement control,
            string resourceKey)
            => control.SetResourceReference(
                FrameworkElement.StyleProperty,
                resourceKey);

        private static Button CreateDialogButton(
            string content,
            string automationId,
            bool primary)
        {
            var button = new Button
            {
                Content = content,
                Width = 80,
                Height = 32,
            };
            ApplyControlStyle(
                button,
                primary ? "LongButton.Primary" : "LongButton");
            AutomationProperties.SetAutomationId(button, automationId);
            return button;
        }

        private static async Task ApplyWebThemeAsync(
            WebView2 webView,
            bool isLight)
        {
            if (webView.CoreWebView2 == null)
                return;

            try
            {
                var themeJson = JsonSerializer.Serialize(
                    isLight ? "light" : "dark");
                await webView.CoreWebView2.ExecuteScriptAsync($$"""
                    (() => {
                      const theme = {{themeJson}};
                      document.documentElement.dataset.longTheme = theme;
                      document.documentElement.style.colorScheme = theme;
                      window.dispatchEvent(new CustomEvent(
                        'longthemechanged',
                        { detail: { theme } }));
                    })();
                    """);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "同步自定义窗口主题失败");
            }
        }

        private class InputDialog : Window
        {
            private readonly TextBox _textBox;

            public string InputText => _textBox.Text;

            public InputDialog(string message, string title, string defaultValue)
            {
                Title = title;
                Width = 400;
                Height = 180;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                ResizeMode = ResizeMode.NoResize;
                ApplyWindowTheme(this);

                var grid = new Grid { Margin = new Thickness(20) };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = message,
                    Margin = new Thickness(0, 0, 0, 12),
                    TextWrapping = TextWrapping.Wrap
                };
                label.SetResourceReference(
                    TextBlock.ForegroundProperty,
                    "Long.Brush.Text.Primary");
                Grid.SetRow(label, 0);
                grid.Children.Add(label);

                _textBox = new TextBox
                {
                    Text = defaultValue,
                    VerticalAlignment = VerticalAlignment.Top
                };
                ApplyControlStyle(_textBox, "LongTextBox");
                AutomationProperties.SetAutomationId(
                    _textBox,
                    "Long.UI.Prompt.Input");
                Grid.SetRow(_textBox, 1);
                grid.Children.Add(_textBox);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 12, 0, 0)
                };
                Grid.SetRow(buttonPanel, 2);

                var okButton = CreateDialogButton(
                    ServicesInitializer.I18n.T("action.confirm"),
                    "Long.UI.Prompt.Confirm",
                    primary: true);
                okButton.Click += (_, _) => { DialogResult = true; Close(); };
                buttonPanel.Children.Add(okButton);

                var cancelButton = CreateDialogButton(
                    ServicesInitializer.I18n.T("action.cancel"),
                    "Long.UI.Prompt.Cancel",
                    primary: false);
                cancelButton.Margin = new Thickness(8, 0, 0, 0);
                cancelButton.Click += (_, _) => { DialogResult = false; Close(); };
                buttonPanel.Children.Add(cancelButton);

                grid.Children.Add(buttonPanel);
                Content = grid;

                _textBox.Focus();
                _textBox.SelectAll();
            }
        }

        private class SelectDialog : Window
        {
            private readonly ListBox _listBox;

            public int SelectedIndex => _listBox.SelectedIndex;

            public SelectDialog(string message, string[] options, string title)
            {
                Title = title;
                Width = 400;
                Height = 300;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                ResizeMode = ResizeMode.NoResize;
                ApplyWindowTheme(this);

                var grid = new Grid { Margin = new Thickness(20) };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = message,
                    Margin = new Thickness(0, 0, 0, 12),
                    TextWrapping = TextWrapping.Wrap
                };
                label.SetResourceReference(
                    TextBlock.ForegroundProperty,
                    "Long.Brush.Text.Primary");
                Grid.SetRow(label, 0);
                grid.Children.Add(label);

                _listBox = new ListBox();
                _listBox.SetResourceReference(
                    Control.BackgroundProperty,
                    "Long.Brush.Background.Raised");
                _listBox.SetResourceReference(
                    Control.ForegroundProperty,
                    "Long.Brush.Text.Primary");
                _listBox.SetResourceReference(
                    Control.BorderBrushProperty,
                    "Long.Brush.Stroke.Default");
                AutomationProperties.SetAutomationId(
                    _listBox,
                    "Long.UI.Select.Options");
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

                var okButton = CreateDialogButton(
                    ServicesInitializer.I18n.T("action.confirm"),
                    "Long.UI.Select.Confirm",
                    primary: true);
                okButton.Click += (_, _) => { DialogResult = true; Close(); };
                buttonPanel.Children.Add(okButton);

                var cancelButton = CreateDialogButton(
                    ServicesInitializer.I18n.T("action.cancel"),
                    "Long.UI.Select.Cancel",
                    primary: false);
                cancelButton.Margin = new Thickness(8, 0, 0, 0);
                cancelButton.Click += (_, _) => { DialogResult = false; Close(); };
                buttonPanel.Children.Add(cancelButton);

                grid.Children.Add(buttonPanel);
                Content = grid;

                _listBox.Focus();
            }
        }
    }
}
