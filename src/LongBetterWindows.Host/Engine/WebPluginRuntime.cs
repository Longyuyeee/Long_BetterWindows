using Microsoft.Web.WebView2.Wpf;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// WebView2 插件运行时——支持 HTML/JS/CSS 开发插件。
    ///
    /// manifest.json 中 "runtime": "webview" 时使用。
    /// 插件是标准的 HTML 文件，通过 long.* JS API 调用宿主能力。
    ///
    /// JS API 用法 (uTools 风格):
    ///   await long.hotkey.register("Alt+X", callback)
    ///   let text = await long.clipboard.getText()
    ///   await long.clipboard.setText("hello")
    ///   let path = await long.shell.getActiveFolder()
    ///   let note = await long.ads.read(path, "long_note")
    ///   await long.storage.set("key", "value")
    /// </summary>
    public partial class WebPluginRuntime : IDisposable
    {
        private readonly PluginManifest _manifest;
        private readonly WebPluginViewLifecycle _viewLifecycle;
        private readonly WebPluginHostDispatcher _hostDispatcher;
        private readonly WebPluginCommandCoordinator _commands;
        private readonly WebPluginBridgeContext _bridgeContext;
        private readonly WidgetLifecycleCoordinator? _widgetLifecycle;
        public WebView2? WebView => _viewLifecycle.View;
        public PluginManifest Manifest => _manifest;
        public WebPluginRuntime(PluginManifest manifest, string pluginDir)
            : this(manifest, pluginDir, new WebPluginBridgeContext(manifest.Id))
        {
        }

        internal WebPluginRuntime(
            PluginManifest manifest,
            string pluginDir,
            WebPluginBridgeContext bridgeContext,
            string? entryPoint = null,
            WidgetSurfaceLayout? initialWidgetLayout = null)
        {
            _manifest = manifest;
            _bridgeContext = bridgeContext;
            _viewLifecycle = new WebPluginViewLifecycle(
                manifest,
                pluginDir,
                HandleJsMessage,
                entryPoint);
            _widgetLifecycle = _bridgeContext.IsWidget
                ? new WidgetLifecycleCoordinator(
                    _bridgeContext,
                    PostWebMessage,
                    initialLayout: initialWidgetLayout)
                : null;
            _hostDispatcher = new WebPluginHostDispatcher(
                manifest.Id,
                HostProvider.Instance,
                PostWebMessage,
                _bridgeContext,
                contentVersion => _widgetLifecycle?.MarkReady(contentVersion)
                    ?? new { success = true });
            _commands = new WebPluginCommandCoordinator(
                manifest.Id,
                message => _viewLifecycle.SendMessageAsync(message));
            // WebView2 延迟到 UI 线程创建（InitializeAsync）
        }

        public WebView2 EnsureView() => _viewLifecycle.EnsureView();

        public async Task<bool> InitializeAsync()
        {
            var initialized = await _viewLifecycle.InitializeAsync();
            if (initialized)
                _widgetLifecycle?.Mount();
            return initialized;
        }

        public void Dispose()
        {
            _widgetLifecycle?.Dispose();
            _commands.Dispose();
            _hostDispatcher.Dispose();
            _viewLifecycle.Dispose();
        }
        private void HandleJsMessage(string json)
        {
            // 处理来自 JS 的异步消息
            try
            {
                if (!WebPluginBridgeProtocol.IsWithinBridgeMessageLimit(json))
                {
                    Log.Warning(
                        "[Web:{Id}] Bridge message rejected because it exceeds {Limit} bytes.",
                        _manifest.Id,
                        WebPluginBridgeContext.BridgeMessageLimitBytes);
                    return;
                }

                if (_commands.TryHandle(json)) return;
                var msg = WebPluginBridgeProtocol.ParseRequest(json);
                if (msg == null) return;

                _ = HandleJsRequestAsync(msg);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "WebPlugin JS 消息处理失败");
            }
        }

        private async Task HandleJsRequestAsync(WebBridgeRequest msg)
        {
            try
            {
                object? result;
                using (PluginAccessContext.Enter(_manifest.Id))
                    result = await DispatchJsCall(msg.Method, msg.Args);
                var response = WebPluginBridgeProtocol.SerializeResult(msg.Id, result);

                PostWebMessage(response);
            }
            catch (Exception ex)
            {
                var error = WebPluginBridgeProtocol.SerializeError(msg.Id, ex.Message);
                PostWebMessage(error);
            }
        }

        private void PostWebMessage(string json) => _viewLifecycle.PostMessage(json);

        public async Task<PluginCommandResult> SendCommandAsync(
            PluginCommandInvocation invocation,
            CancellationToken cancellationToken = default)
            => await _commands.SendAsync(invocation, cancellationToken);

        public Task NotifyLanguageChangedAsync(
            PluginLanguageContext context)
            => _viewLifecycle.SetLanguageMessageAsync(
                WebPluginBridgeProtocol.SerializeLanguageChanged(context));
        private async Task<object?> DispatchJsCall(string method, object?[] args)
        {
            // ✅ 权限检查：验证插件是否声明了所需的 capability
            var requiredCapability = WebPluginBridgeProtocol.GetRequiredCapability(method);
            if (requiredCapability != null && !_manifest.Capabilities.Contains(requiredCapability))
            {
                Log.Warning("[Web:{Id}] 权限拒绝: {Method} 需要 {Capability}",
                    _manifest.Id, method, requiredCapability);
                return new { success = false, error = $"插件未声明权限: {requiredCapability}" };
            }

            // ✅ 记录 API 调用
            if (requiredCapability != null)
            {
                Services.CapabilityUsageTracker.Instance.RecordApiCall(_manifest.Id, requiredCapability, method);
            }

            // ✅ 设置插件上下文以支持回滚追踪
            using (PluginAccessContext.Enter(_manifest.Id))
            {
                return await _hostDispatcher.DispatchAsync(method, args);
            }
        }

    }

}
