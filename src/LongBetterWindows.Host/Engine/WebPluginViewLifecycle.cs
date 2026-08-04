using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Services;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    internal sealed class WebPluginViewLifecycle : IDisposable
    {
        private readonly PluginManifest _manifest;
        private readonly WebPluginNavigationPolicy _navigationPolicy;
        private readonly Action<string> _messageReceived;
        private readonly WebPluginLanguageMessageState _languageMessages = new();
        private WebView2? _webView;
        private bool _themeSubscribed;
        private bool _accessibilitySubscribed;
        private bool _languageSubscribed;
        private TaskCompletionSource<bool>? _navigationCompletion;

        internal WebPluginViewLifecycle(
            PluginManifest manifest,
            string pluginDirectory,
            Action<string> messageReceived,
            string? entryPoint = null)
        {
            _manifest = manifest;
            _navigationPolicy = new WebPluginNavigationPolicy(pluginDirectory);
            _messageReceived = messageReceived;
            EntryPoint = string.IsNullOrWhiteSpace(entryPoint)
                ? manifest.EntryPoint
                : entryPoint;
        }

        internal WebView2? View => _webView;
        internal string EntryPoint { get; }

        internal WebView2 EnsureView()
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            if (!dispatcher.CheckAccess())
                return dispatcher.Invoke(EnsureView);
            return _webView ??= new WebView2();
        }

        internal async Task<bool> InitializeAsync()
        {
            // WebView2 必须在 STA 线程（UI 线程）初始化和使用
            var initialization = System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var webView = EnsureView();

                var env = await CoreWebView2Environment.CreateAsync();
                await webView.EnsureCoreWebView2Async(env);

#if DEBUG
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
#else
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
#endif
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    _navigationPolicy.VirtualHostName,
                    _navigationPolicy.PluginRoot,
                    CoreWebView2HostResourceAccessKind.DenyCors);
                webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
                webView.CoreWebView2.DownloadStarting += OnDownloadStarting;
                webView.CoreWebView2.AddWebResourceRequestedFilter(
                    "*",
                    CoreWebView2WebResourceContext.All);
                webView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
                webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    _navigationPolicy.BuildContentSecurityPolicyInjectionScript());

                var uiKitScript = BuildUiKitInjectionScript();
                if (!string.IsNullOrEmpty(uiKitScript))
                    await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(uiKitScript);

                // 注入 long.* JS API 桥接
                // 注入必须在导航前完成，否则首条命令可能早于页面处理器到达。
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    WebPluginBridgeProtocol.BuildInjectionScript(_manifest.Id));
                Log.Debug("[Web:{Id}] JS Bridge 注入完成", _manifest.Id);

                if (!_themeSubscribed)
                {
                    App.ThemeChanged += OnThemeChanged;
                    _themeSubscribed = true;
                }
                if (!_accessibilitySubscribed)
                {
                    App.AccessibilityPreferencesChanged += OnAccessibilityPreferencesChanged;
                    _accessibilitySubscribed = true;
                }
                if (!_languageSubscribed)
                {
                    ServicesInitializer.I18n.LanguageChanged += OnLanguageChanged;
                    _languageSubscribed = true;
                }

                // 加载插件 HTML
                if (!_navigationPolicy.TryResolveEntryPoint(EntryPoint, out var entryUri))
                    throw new InvalidDataException(
                        $"Web 插件入口不存在或越出插件目录：{EntryPoint}");

                _navigationCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                webView.CoreWebView2.Navigate(entryUri!.AbsoluteUri);
            });
            try
            {
                await await initialization;
                var navigationCompletion = _navigationCompletion
                    ?? throw new InvalidOperationException(
                        $"Web plugin navigation did not start: {_manifest.Id}");
                var navigationSucceeded = await navigationCompletion.Task.WaitAsync(
                    TimeSpan.FromSeconds(15));
                if (!navigationSucceeded)
                {
                    Log.Error(
                        "WebPlugin {PluginId} 初始页面导航失败",
                        _manifest.Id);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "WebPlugin {PluginId} 初始化失败", _manifest.Id);
                return false;
            }

            Log.Information("WebPlugin {PluginId} 已初始化", _manifest.Id);
            return true;
        }

        private string BuildUiKitInjectionScript()
        {
            try
            {
                var assetsDirectory = Path.Combine(AppContext.BaseDirectory, "WebAssets");
                var cssPath = Path.Combine(assetsDirectory, "long-ui.css");
                var jsPath = Path.Combine(assetsDirectory, "long-ui.js");
                if (!File.Exists(cssPath))
                {
                    Log.Warning("[Web:{Id}] Long Web UI Kit 未找到: {Path}", _manifest.Id, cssPath);
                    return string.Empty;
                }

                var css = File.ReadAllText(cssPath);
                var helpers = File.Exists(jsPath) ? File.ReadAllText(jsPath) : string.Empty;
                var isLight = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme()
                    == Wpf.Ui.Appearance.ApplicationTheme.Light;
                var cssJson = System.Text.Json.JsonSerializer.Serialize(css);
                var themeJson = System.Text.Json.JsonSerializer.Serialize(isLight ? "light" : "dark");
                var uiKitVersionJson = System.Text.Json.JsonSerializer.Serialize(
                    PluginUiKitVersion.Current);
                var highContrastJson = App.IsHighContrastEnabled ? "true" : "false";
                var reducedMotionJson = App.IsReducedMotionEnabled ? "true" : "false";
                var languageJson = System.Text.Json.JsonSerializer.Serialize(
                    ServicesInitializer.I18n.CurrentLanguage);

                return $$"""
                    (function () {
                      window.LongUI = window.LongUI || {};
                      window.LongUI.version = {{uiKitVersionJson}};
                      window.LongUI._initialLanguage = {{languageJson}};
                      const installLongUiDocument = function () {
                        document.documentElement.dataset.longTheme = {{themeJson}};
                        document.documentElement.dataset.longUiKitVersion = {{uiKitVersionJson}};
                        document.documentElement.dataset.longHighContrast = '{{highContrastJson}}';
                        document.documentElement.dataset.longReducedMotion = '{{reducedMotionJson}}';
                        if (!document.getElementById('long-ui-kit')) {
                          const style = document.createElement('style');
                          style.id = 'long-ui-kit';
                          style.textContent = {{cssJson}};
                          document.head.appendChild(style);
                        }
                      };
                      if (document.readyState === 'loading')
                        document.addEventListener('DOMContentLoaded', installLongUiDocument, { once: true });
                      else
                        installLongUiDocument();
                      {{helpers}}
                    })();
                    """;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Web:{Id}] Long Web UI Kit 注入准备失败", _manifest.Id);
                return string.Empty;
            }
        }

        private void OnThemeChanged(bool isLight)
        {
            var webView = _webView;
            if (webView?.CoreWebView2 == null) return;

            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    var theme = isLight ? "light" : "dark";
                    await webView.CoreWebView2.ExecuteScriptAsync(
                        $"window.LongUI?.setTheme('{theme}');");
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[Web:{Id}] 同步主题失败", _manifest.Id);
                }
            });
        }

        private void OnAccessibilityPreferencesChanged(bool highContrast, bool reducedMotion)
        {
            var webView = _webView;
            if (webView?.CoreWebView2 == null) return;

            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await webView.CoreWebView2.ExecuteScriptAsync(
                        $"window.LongUI?.setHighContrast({highContrast.ToString().ToLowerInvariant()});" +
                        $"window.LongUI?.setReducedMotion({reducedMotion.ToString().ToLowerInvariant()});");
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[Web:{Id}] Failed to synchronize accessibility preferences", _manifest.Id);
                }
            });
        }

        private void OnLanguageChanged(string language)
        {
            var webView = _webView;
            if (webView?.CoreWebView2 == null) return;
            var languageJson = System.Text.Json.JsonSerializer.Serialize(language);

            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await webView.CoreWebView2.ExecuteScriptAsync(
                        $"window.LongUI?._setHostLanguage({languageJson});");
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[Web:{Id}] Failed to synchronize host language", _manifest.Id);
                }
            });
        }

        private void OnNavigationStarting(
            object? sender,
            CoreWebView2NavigationStartingEventArgs args)
        {
            if (_navigationPolicy.IsTrustedWebViewUri(args.Uri))
            {
                if (_webView is not null)
                    WebPluginUiModalState.SetOpen(_webView, false);
                _languageMessages.BeginNavigation();
                return;
            }

            args.Cancel = true;
            Log.Warning("[Web:{Id}] 已阻止越界页面导航：{Uri}", _manifest.Id, args.Uri);
        }

        private void OnNavigationCompleted(
            object? sender,
            CoreWebView2NavigationCompletedEventArgs args)
        {
            var message = _languageMessages.CompleteNavigation(args.IsSuccess);
            _navigationCompletion?.TrySetResult(args.IsSuccess);
            if (message is not null)
                PostMessageCore(message);
        }

        private void OnNewWindowRequested(
            object? sender,
            CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            Log.Warning("[Web:{Id}] 已阻止新窗口请求：{Uri}", _manifest.Id, args.Uri);
        }

        private void OnDownloadStarting(
            object? sender,
            CoreWebView2DownloadStartingEventArgs args)
        {
            args.Cancel = true;
            args.Handled = true;
            Log.Warning("[Web:{Id}] 已阻止浏览器下载；插件应使用 long.http.download", _manifest.Id);
        }

        private void OnWebResourceRequested(
            object? sender,
            CoreWebView2WebResourceRequestedEventArgs args)
        {
            var requestUri = args.Request.Uri;
            if (_navigationPolicy.ShouldBlockWebResourceRequest(requestUri))
            {
                args.Response = CreateTextResponse(
                    403,
                    "Forbidden",
                    "Long Assistant blocked a plugin subresource outside the trusted virtual origin.");
                Log.Warning("[Web:{Id}] 已阻止越界子资源请求：{Uri}", _manifest.Id, requestUri);
                return;
            }

            if (args.ResourceContext != CoreWebView2WebResourceContext.Document)
                return;

            if (!_navigationPolicy.TryResolveVirtualUriToLocalPath(requestUri, out var localPath)
                || localPath is null
                || !WebPluginNavigationPolicy.IsHtmlDocumentPath(localPath))
            {
                return;
            }

            try
            {
                args.Response = CreateFileResponse(
                    localPath,
                    "text/html; charset=utf-8",
                    _navigationPolicy.BuildContentSecurityPolicyResponseHeader());
            }
            catch (Exception ex)
            {
                args.Response = CreateTextResponse(
                    500,
                    "Internal Server Error",
                    "Long Assistant could not load the plugin document.");
                Log.Warning(ex, "[Web:{Id}] Web 插件 HTML 文档响应创建失败：{Uri}", _manifest.Id, requestUri);
            }
        }

        private void OnWebMessageReceived(
            object? sender,
            CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (!_navigationPolicy.IsTrustedWebViewUri(args.Source))
            {
                Log.Warning("[Web:{Id}] 已拒绝非插件页面的 Bridge 消息：{Source}",
                    _manifest.Id, args.Source);
                return;
            }

            if (_webView is not null
                && WebPluginUiModalState.TryRead(
                    args.WebMessageAsJson,
                    out var isOpen))
            {
                WebPluginUiModalState.SetOpen(_webView, isOpen);
                return;
            }

            _messageReceived(args.WebMessageAsJson);
        }

        public void Dispose()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null
                && !dispatcher.HasShutdownStarted
                && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(Dispose);
                return;
            }

            if (_themeSubscribed)
            {
                App.ThemeChanged -= OnThemeChanged;
                _themeSubscribed = false;
            }
            if (_accessibilitySubscribed)
            {
                App.AccessibilityPreferencesChanged -= OnAccessibilityPreferencesChanged;
                _accessibilitySubscribed = false;
            }
            if (_languageSubscribed)
            {
                ServicesInitializer.I18n.LanguageChanged -= OnLanguageChanged;
                _languageSubscribed = false;
            }

            if (_webView?.CoreWebView2 != null)
            {
                _webView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
                _webView.CoreWebView2.DownloadStarting -= OnDownloadStarting;
                _webView.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            }

            if (_webView is not null)
                WebPluginUiModalState.SetOpen(_webView, false);

            _webView?.Dispose();
            _webView = null;
            _languageMessages.BeginNavigation();
        }

        internal void PostMessage(string json)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
                return;

            if (dispatcher.CheckAccess())
                PostMessageCore(json);
            else
                _ = dispatcher.InvokeAsync(() => PostMessageCore(json));
        }

        internal async Task SendMessageAsync(string json)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
                return;

            if (dispatcher.CheckAccess())
                PostMessageCore(json);
            else
                await dispatcher.InvokeAsync(() => PostMessageCore(json));
        }

        internal async Task SetLanguageMessageAsync(string json)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
                return;

            if (dispatcher.CheckAccess())
                SetLanguageMessageCore(json);
            else
                await dispatcher.InvokeAsync(() => SetLanguageMessageCore(json));
        }

        private void SetLanguageMessageCore(string json)
        {
            var message = _languageMessages.Update(json);
            if (message is not null)
                PostMessageCore(message);
        }

        private void PostMessageCore(string json)
        {
            var coreWebView = _webView?.CoreWebView2;
            if (coreWebView == null)
            {
                Log.Warning("[Web:{Id}] WebView 尚未就绪，消息已忽略", _manifest.Id);
                return;
            }

            coreWebView.PostWebMessageAsJson(json);
        }

        private CoreWebView2WebResourceResponse CreateFileResponse(
            string path,
            string contentType,
            string extraHeaders)
        {
            var stream = File.OpenRead(path);
            var headers = $"Content-Type: {contentType}\r\n"
                + "X-Content-Type-Options: nosniff\r\n"
                + extraHeaders;
            return _webView!.CoreWebView2.Environment.CreateWebResourceResponse(
                stream,
                200,
                "OK",
                headers);
        }

        private CoreWebView2WebResourceResponse CreateTextResponse(
            int statusCode,
            string reasonPhrase,
            string message)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(message);
            var stream = new MemoryStream(bytes);
            var headers = "Content-Type: text/plain; charset=utf-8\r\n"
                + "X-Content-Type-Options: nosniff\r\n"
                + _navigationPolicy.BuildContentSecurityPolicyResponseHeader();
            return _webView!.CoreWebView2.Environment.CreateWebResourceResponse(
                stream,
                statusCode,
                reasonPhrase,
                headers);
        }
    }
}
