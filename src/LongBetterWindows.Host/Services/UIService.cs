using System.Net;
using System.Text.Json;
using System.Windows;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Views;
using Microsoft.Web.WebView2.Wpf;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    public class UIService : IUICapability
    {
        private readonly Dictionary<string, ManagedPluginWindow> _windows = new();
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
                    var webView = new WebView2();
                    var window = new PluginContentWindow(
                        title,
                        width,
                        height,
                        resizable,
                        webView);
                    var owner = ResolveOwner();
                    if (owner is not null)
                        window.Owner = owner;
                    else
                        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
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
                        _windows[windowId] = new ManagedPluginWindow(window, webView);
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
                        ResolveOwner(),
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
                    var dialog = new CapabilityInputDialog(
                        ResolveOwner(), message, title, defaultValue);
                    dialog.ShowDialog();
                    return dialog.Confirmed ? dialog.InputText : null;
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
                    var dialog = new CapabilitySelectDialog(
                        ResolveOwner(), message, options, title);
                    dialog.ShowDialog();
                    return dialog.Confirmed ? dialog.SelectedIndex : -1;
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

            ManagedPluginWindow? managed;
            lock (_lock)
            {
                if (!_windows.TryGetValue(windowId, out managed))
                    return Task.FromResult(HostApiResponse.Failure(ApiErrorCode.NotFound, "窗口不存在"));
            }

            try
            {
                return Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    managed.Window.Close();
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

            ManagedPluginWindow? managed;
            lock (_lock)
            {
                if (!_windows.TryGetValue(windowId, out managed))
                    return Task.FromResult(HostApiResponse.Failure(ApiErrorCode.NotFound, "窗口不存在"));
            }

            try
            {
                var task = Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (managed.WebView.CoreWebView2 != null)
                    {
                        var script = $"window.dispatchEvent(new CustomEvent('hostMessage', {{ detail: {message} }}));";
                        await managed.WebView.CoreWebView2.ExecuteScriptAsync(script);
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
            => new CapabilityInputDialog(
                owner: null,
                "Theme probe",
                "Prompt",
                "Value");

        internal static Window CreateSelectDialogForQuality()
            => new CapabilitySelectDialog(
                owner: null,
                "Select one option",
                ["First option", "Second option", "Third option"],
                "Selection");

        private static Window? ResolveOwner()
        {
            var activeWindow = Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(window => window.IsActive && window.IsVisible);
            if (activeWindow is not null)
                return activeWindow;

            return Application.Current.MainWindow is { IsVisible: true } main
                ? main
                : null;
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

        private sealed record ManagedPluginWindow(
            PluginContentWindow Window,
            WebView2 WebView);
    }
}
