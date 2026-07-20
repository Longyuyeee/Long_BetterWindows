using System.IO;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using LongBetterWindows.Host.Capabilities;
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
    public class WebPluginRuntime : IDisposable
    {
        private static readonly System.Text.Json.JsonSerializerOptions MessageJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private WebView2? _webView;
        private readonly PluginManifest _manifest;
        private readonly WebPluginNavigationPolicy _navigationPolicy;
        private bool _themeSubscribed;

        public WebView2? WebView => _webView;
        public PluginManifest Manifest => _manifest;

        public WebPluginRuntime(PluginManifest manifest, string pluginDir)
        {
            _manifest = manifest;
            _navigationPolicy = new WebPluginNavigationPolicy(pluginDir);
            // WebView2 延迟到 UI 线程创建（InitializeAsync）
        }

        public WebView2 EnsureView()
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            if (!dispatcher.CheckAccess())
                return dispatcher.Invoke(EnsureView);
            return _webView ??= new WebView2();
        }

        public async Task<bool> InitializeAsync()
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
                webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
                webView.CoreWebView2.DownloadStarting += OnDownloadStarting;
                webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                var uiKitScript = BuildUiKitInjectionScript();
                if (!string.IsNullOrEmpty(uiKitScript))
                    await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(uiKitScript);

                // 注入 long.* JS API 桥接
                // 在页面加载前注入初始化脚本（与导航并行执行）
                _ = webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    BuildJsBridge(_manifest.Id))
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            Log.Error(t.Exception, "[Web:{Id}] JS Bridge 注入失败", _manifest.Id);
                        else
                            Log.Debug("[Web:{Id}] JS Bridge 注入完成", _manifest.Id);
                    }, TaskScheduler.Default);

                if (!_themeSubscribed)
                {
                    App.ThemeChanged += OnThemeChanged;
                    _themeSubscribed = true;
                }

                // 加载插件 HTML
                if (!_navigationPolicy.TryResolveEntryPoint(_manifest.EntryPoint, out var entryUri))
                    throw new InvalidDataException(
                        $"Web 插件入口不存在或越出插件目录：{_manifest.EntryPoint}");

                webView.CoreWebView2.Navigate(entryUri!.AbsoluteUri);
            });
            try
            {
                await await initialization;
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

                return $$"""
                    (function () {
                      const installLongUi = function () {
                        document.documentElement.dataset.longTheme = {{themeJson}};
                        if (!document.getElementById('long-ui-kit')) {
                          const style = document.createElement('style');
                          style.id = 'long-ui-kit';
                          style.textContent = {{cssJson}};
                          (document.head || document.documentElement).appendChild(style);
                        }
                      };
                      if (document.readyState === 'loading')
                        document.addEventListener('DOMContentLoaded', installLongUi, { once: true });
                      else
                        installLongUi();
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

        private void OnNavigationStarting(
            object? sender,
            CoreWebView2NavigationStartingEventArgs args)
        {
            if (_navigationPolicy.IsTrustedLocalUri(args.Uri)) return;

            args.Cancel = true;
            Log.Warning("[Web:{Id}] 已阻止越界页面导航：{Uri}", _manifest.Id, args.Uri);
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

        private void OnWebMessageReceived(
            object? sender,
            CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (!_navigationPolicy.IsTrustedLocalUri(args.Source))
            {
                Log.Warning("[Web:{Id}] 已拒绝非插件页面的 Bridge 消息：{Source}",
                    _manifest.Id, args.Source);
                return;
            }

            HandleJsMessage(args.WebMessageAsJson);
        }

        public void Dispose()
        {
            if (_themeSubscribed)
            {
                App.ThemeChanged -= OnThemeChanged;
                _themeSubscribed = false;
            }

            if (_webView?.CoreWebView2 != null)
            {
                _webView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
                _webView.CoreWebView2.DownloadStarting -= OnDownloadStarting;
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            }

            _webView?.Dispose();
            _webView = null;
        }

        private void HandleJsMessage(string json)
        {
            // 处理来自 JS 的异步消息
            try
            {
                var msg = System.Text.Json.JsonSerializer.Deserialize<JsMessage>(json, MessageJsonOptions);
                if (msg == null) return;

                _ = HandleJsRequestAsync(msg);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "WebPlugin JS 消息处理失败");
            }
        }

        private async Task HandleJsRequestAsync(JsMessage msg)
        {
            try
            {
                object? result;
                using (PluginAccessContext.Enter(_manifest.Id))
                    result = await DispatchJsCall(msg.Method, msg.Args);
                var response = System.Text.Json.JsonSerializer.Serialize(new
                {
                    id = msg.Id,
                    result,
                });

                PostWebMessage(response);
            }
            catch (Exception ex)
            {
                var error = System.Text.Json.JsonSerializer.Serialize(new
                {
                    id = msg.Id,
                    error = ex.Message,
                });
                PostWebMessage(error);
            }
        }

        private void PostWebMessage(string json)
        {
            var coreWebView = _webView?.CoreWebView2;
            if (coreWebView == null)
            {
                Log.Warning("[Web:{Id}] WebView 尚未就绪，消息已忽略", _manifest.Id);
                return;
            }

            coreWebView.PostWebMessageAsJson(json);
        }

        public async Task SendCommandAsync(PluginCommandInvocation invocation)
        {
            var webView = _webView;
            if (webView?.CoreWebView2 == null) return;

            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "long.command",
                command = invocation,
            });
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                () => webView.CoreWebView2.PostWebMessageAsJson(json));
        }

        private async Task<object?> DispatchJsCall(string method, object?[] args)
        {
            // ✅ 权限检查：验证插件是否声明了所需的 capability
            var requiredCapability = GetRequiredCapability(method);
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

            var h = HostProvider.Instance;

            // ✅ 设置插件上下文以支持回滚追踪
            using (PluginAccessContext.Enter(_manifest.Id))
            {
                return await DispatchJsCallInternal(method, args, h);
            }
        }

        private async Task<object?> DispatchJsCallInternal(string method, object?[] args, IHostApi h)
        {
            return await (method switch
            {
                // === long.app ===
                "app.openUrl" => Ok(h.ShellExecute.OpenUrlAsync(Arg(args, 0))),
                "app.openFolder" => Ok(h.ShellExecute.OpenFolderAsync(Arg(args, 0))),
                "app.openWithDefault" => Ok(h.ShellExecute.OpenWithDefaultAsync(Arg(args, 0))),
                "app.showNotification" => Task.FromResult<object?>(UIToast(Arg(args, 0) + "\n" + Arg(args, 1))),
                "app.getVersion" => Task.FromResult<object?>(new { version = App.ProductVersion }),
                "app.log" => Task.FromResult<object?>(PluginLog(args)),

                // === long.clipboard ===
                "clipboard.getText" => Ok(h.Clipboard.GetTextAsync()),
                "clipboard.setText" => Ok(h.Clipboard.SetTextAsync(Arg(args, 0))),
                "clipboard.clear" => Ok(h.Clipboard.ClearAsync()),
                "clipboard.startMonitoring" => Ok(h.Clipboard.StartMonitoringAsync()),
                "clipboard.stopMonitoring" => Ok(h.Clipboard.StopMonitoringAsync()),

                // === long.shell ===
                "shell.getActiveFolder" => Ok(h.ShellSelection.GetActiveExplorerFolderPathAsync()),
                "shell.getSelectedItems" => OkList(h.ShellSelection.GetSelectedItemsAsync()),
                "shell.getItemScreenRect" => Ok(h.ShellSelection.GetSelectedItemScreenRectAsync()),

                // === long.shell execute ===
                "shell.openUrl" => Ok(h.ShellExecute.OpenUrlAsync(Arg(args, 0))),
                "shell.openFolder" => Ok(h.ShellExecute.OpenFolderAsync(Arg(args, 0))),
                "shell.openWithDefault" => Ok(h.ShellExecute.OpenWithDefaultAsync(Arg(args, 0))),

                // === long.http ===
                "http.get" => Ok(h.Http.GetAsync(Arg(args, 0), ParseHeaders(args, 1))),
                "http.post" => HttpPost(args),
                "http.download" => HttpDownload(args),

                // === long.fs.ads ===
                "fs.ads.read" => Ok(h.ADS.ReadAsync(Arg(args, 0), Arg(args, 1, "long_note"))),
                "fs.ads.write" => Ok(h.ADS.WriteAsync(Arg(args, 0), Arg(args, 1, "long_note"), Arg(args, 2))),
                "fs.ads.delete" => Ok(h.ADS.DeleteAsync(Arg(args, 0), Arg(args, 1, "long_note"))),
                "fs.ads.exists" => Ok(h.ADS.ExistsAsync(Arg(args, 0), Arg(args, 1, "long_note"))),
                "fs.ads.isNTFS" => Ok(h.ADS.IsNTFSVolumeAsync(Arg(args, 0))),

                // === long.hotkey ===
                "hotkey.register" => HotKeyRegister(args),
                "hotkey.unregister" => HotKeyUnregister(args),
                "hotkey.isConflict" => Ok(h.HotKey.IsConflictAsync(Arg(args, 0))),

                // === long.registry ===
                "registry.read" => Ok(h.Registry.ReadValueAsync(Arg(args, 0), Arg(args, 1))),
                "registry.write" => Ok(h.Registry.WriteValueAsync(Arg(args, 0), Arg(args, 1), Arg(args, 2))),
                "registry.delete" => Ok(h.Registry.DeleteValueAsync(Arg(args, 0), Arg(args, 1))),

                // === long.storage ===
                "storage.get" => Ok(h.Storage.GetAsync(Arg(args, 0))),
                "storage.set" => Ok(h.Storage.SetAsync(Arg(args, 0), Arg(args, 1))),
                "storage.delete" => Ok(h.Storage.DeleteAsync(Arg(args, 0))),
                "storage.containsKey" => Ok(h.Storage.ContainsKeyAsync(Arg(args, 0))),

                // === long.process ===
                "process.start" => Ok(h.Process.StartAsync(Arg(args, 0), Arg(args, 1, null!))),
                "process.getList" => OkList(h.Process.GetRunningProcessesAsync(Arg(args, 0, null!))),
                "process.kill" => Ok(h.Process.KillAsync(IntArg(args, 0))),

                // === long.fileOps ===
                "fileOps.copy" => Ok(h.FileOps.CopyAsync(Arg(args, 0), Arg(args, 1))),
                "fileOps.move" => Ok(h.FileOps.MoveAsync(Arg(args, 0), Arg(args, 1))),
                "fileOps.delete" => Ok(h.FileOps.DeleteAsync(Arg(args, 0))),
                "fileOps.exists" => Ok(h.FileOps.ExistsAsync(Arg(args, 0))),

                // === long.performance ===
                "performance.getCpuUsage" => Ok(h.Performance.GetCpuUsageAsync()),
                "performance.getMemoryInfo" => Ok(h.Performance.GetMemoryInfoAsync()),
                "performance.getDiskInfo" => OkList(h.Performance.GetDiskInfoAsync()),
                "performance.getSystemInfo" => Ok(h.Performance.GetSystemInfoAsync()),
                "performance.getTopByCpu" => OkList(h.Performance.GetTopProcessesByCpuAsync(IntArg(args, 0, 10))),
                "performance.getTopByMemory" => OkList(h.Performance.GetTopProcessesByMemoryAsync(IntArg(args, 0, 10))),

                // === long.networkPort / long.network ===
                "networkPort.getTcpConnections" => OkList(h.NetworkPort.GetTcpConnectionsAsync()),
                "networkPort.getTcpListeners" => OkList(h.NetworkPort.GetTcpListenersAsync()),
                "networkPort.getUdpEndpoints" => OkList(h.NetworkPort.GetUdpEndpointsAsync()),
                "networkPort.findOwner" => Ok(h.NetworkPort.FindPortOwnerAsync(
                    IntArg(args, 0), EnumArg(args, 1, ProtocolType.Tcp))),
                "networkPort.isInUse" => Ok(h.NetworkPort.IsPortInUseAsync(
                    IntArg(args, 0), EnumArg(args, 1, ProtocolType.Tcp))),
                "networkPort.getSummary" => Ok(h.NetworkPort.GetPortSummaryAsync()),
                "network.getStats" => Ok(h.NetworkMonitor.GetNetworkStatsAsync()),
                "network.getSpeed" => Ok(h.NetworkMonitor.GetNetworkSpeedAsync()),
                "network.getInterfaces" => OkList(h.NetworkMonitor.GetNetworkInterfacesAsync()),

                // === long.audio ===
                "audio.getVolume" => Ok(h.Audio.GetVolumeAsync()),
                "audio.setVolume" => Ok(h.Audio.SetVolumeAsync(IntArg(args, 0))),
                "audio.getMute" => Ok(h.Audio.GetMuteAsync()),
                "audio.setMute" => Ok(h.Audio.SetMuteAsync(BoolArg(args, 0))),
                "audio.increase" => Ok(h.Audio.IncreaseVolumeAsync(IntArg(args, 0, 5))),
                "audio.decrease" => Ok(h.Audio.DecreaseVolumeAsync(IntArg(args, 0, 5))),
                "audio.getDevices" => OkList(h.Audio.GetAudioDevicesAsync()),
                "audio.setDefaultDevice" => Ok(h.Audio.SetDefaultDeviceAsync(Arg(args, 0))),

                // === long.power ===
                "power.getStatus" => Ok(h.Power.GetPowerStatusAsync()),
                "power.lock" => Ok(h.Power.LockScreenAsync()),
                "power.sleep" => Ok(h.Power.SleepAsync()),
                "power.hibernate" => Ok(h.Power.HibernateAsync()),
                "power.shutdown" => Ok(h.Power.ShutdownAsync(IntArg(args, 0))),
                "power.reboot" => Ok(h.Power.RebootAsync(IntArg(args, 0))),
                "power.preventSleep" => Ok(h.Power.PreventSleepAsync(BoolArg(args, 0))),

                // === long.theme / wallpaper / brightness ===
                "theme.get" => Ok(h.Theme.GetSystemThemeAsync()),
                "theme.set" => Ok(h.Theme.SetSystemThemeAsync(EnumArg(args, 0, SystemTheme.Auto))),
                "theme.toggle" => Ok(h.Theme.ToggleThemeAsync()),
                "theme.getAccentColor" => Ok(h.Theme.GetAccentColorAsync()),
                "theme.setAccentColor" => Ok(h.Theme.SetAccentColorAsync(Arg(args, 0))),
                "wallpaper.get" => Ok(h.Wallpaper.GetWallpaperAsync()),
                "wallpaper.set" => Ok(h.Wallpaper.SetWallpaperAsync(
                    Arg(args, 0), EnumArg(args, 1, WallpaperStyle.Fill))),
                "wallpaper.getStyle" => Ok(h.Wallpaper.GetWallpaperStyleAsync()),
                "brightness.get" => Ok(h.Brightness.GetBrightnessAsync()),
                "brightness.set" => Ok(h.Brightness.SetBrightnessAsync(IntArg(args, 0))),
                "brightness.increase" => Ok(h.Brightness.IncreaseBrightnessAsync(IntArg(args, 0, 10))),
                "brightness.decrease" => Ok(h.Brightness.DecreaseBrightnessAsync(IntArg(args, 0, 10))),

                // === long.pinyin ===
                "pinyin.get" => Ok(h.Pinyin.GetPinyinAsync(Arg(args, 0))),
                "pinyin.getInitials" => Ok(h.Pinyin.GetPinyinInitialsAsync(Arg(args, 0))),
                "pinyin.match" => Ok(h.Pinyin.MatchAsync(Arg(args, 0), Arg(args, 1))),
                "pinyin.filter" => OkList(h.Pinyin.FilterAsync(StringListArg(args, 0), Arg(args, 1))),

                // === long.input ===
                "input.keyPress" => Ok(h.Input.SendKeyPressAsync(IntArg(args, 0))),
                "input.mouseClick" => Ok(h.Input.SendMouseClickAsync(
                    IntArg(args, 0), IntArg(args, 1), BoolArg(args, 2))),
                "input.moveCursor" => Ok(h.Input.MoveCursorAsync(IntArg(args, 0), IntArg(args, 1))),

                // === long.fileSystem ===
                "fileSystem.enumerate" => OkList(h.FileSystem.EnumerateFilesAsync(
                    Arg(args, 0), Arg(args, 1, "*.*"), BoolArg(args, 2, true))),
                "fileSystem.hash" => Ok(h.FileSystem.ComputeFileHashAsync(Arg(args, 0))),
                "fileSystem.metadata" => Ok(h.FileSystem.GetFileMetadataAsync(Arg(args, 0))),
                "fileSystem.findDuplicates" => OkList(h.FileSystem.FindDuplicateFilesAsync(Arg(args, 0))),
                "fileSystem.batchRename" => Ok(h.FileSystem.BatchRenameAsync(
                    JsonArg<List<RenameOperation>>(args, 0) ?? new List<RenameOperation>())),
                "fileSystem.classify" => Ok(h.FileSystem.ClassifyFilesAsync(
                    Arg(args, 0), EnumArg(args, 1, ClassifyMode.ByExtension))),
                "fileSystem.findLarge" => OkList(h.FileSystem.FindLargeFilesAsync(
                    Arg(args, 0), LongArg(args, 1))),
                "fileSystem.searchContent" => OkList(h.FileSystem.SearchFileContentAsync(
                    Arg(args, 0), Arg(args, 1), StringListArg(args, 2))),

                // === long.cache ===
                "cache.cleanTemp" => Ok(h.Cache.CleanTempFilesAsync()),
                "cache.cleanWindowsUpdate" => Ok(h.Cache.CleanWindowsUpdateCacheAsync()),
                "cache.cleanBrowser" => Ok(h.Cache.CleanBrowserCacheAsync(Arg(args, 0))),
                "cache.emptyRecycleBin" => Ok(h.Cache.EmptyRecycleBinAsync()),
                "cache.getStatistics" => Ok(h.Cache.GetCacheStatisticsAsync()),
                "cache.cleanAll" => Ok(h.Cache.CleanAllAsync()),

                // === long.schedule ===
                "schedule.create" => Ok(h.Schedule.CreateTaskAsync(
                    JsonArg<ScheduleTask>(args, 0) ?? new ScheduleTask())),
                "schedule.delete" => Ok(h.Schedule.DeleteTaskAsync(Arg(args, 0))),
                "schedule.getAll" => OkList(h.Schedule.GetAllTasksAsync()),
                "schedule.setEnabled" => Ok(h.Schedule.SetTaskEnabledAsync(
                    Arg(args, 0), BoolArg(args, 1))),
                "schedule.runNow" => Ok(h.Schedule.RunTaskNowAsync(Arg(args, 0))),

                // === long.shell file ops ===
                "shell.listFiles" => Task.FromResult<object?>(ShellListFiles(Arg(args, 0))),
                "shell.renameFile" => ShellRenameFileAsync(Arg(args, 0), Arg(args, 1)),

                // === long.window ===
                "window.getForeground" => WindowGetForeground(),
                "window.getVisible" => OkList(h.WindowInfo.GetVisibleWindowsAsync()),

                // === long.ui ===
                "ui.showToast" => Task.FromResult<object?>(UIToast(Arg(args, 0))),
                "ui.createWindow" => UICreateWindow(args),
                "ui.confirm" => Ok(h.UI.ConfirmAsync(Arg(args, 0), Arg(args, 1, "确认"))),
                "ui.prompt" => Ok(h.UI.PromptAsync(
                    Arg(args, 0), Arg(args, 1, "输入"), Arg(args, 2))),
                "ui.select" => Ok(h.UI.SelectAsync(
                    Arg(args, 0), StringListArg(args, 1).ToArray(), Arg(args, 2, "选择"))),
                "ui.closeWindow" => Ok(h.UI.CloseWindowAsync(Arg(args, 0))),
                "ui.sendMessage" => Ok(h.UI.SendMessageToWindowAsync(Arg(args, 0), Arg(args, 1))),

                // === long.screenshot ===
                "screenshot.captureFull" => Ok(h.ScreenCapture.CaptureFullScreenAsync()),
                "screenshot.captureRegion" => CaptureRegionToFile(args),

                _ => Task.FromResult<object?>(new { success = false, error = $"未知方法: {method}" }),
            });
        }

        /// <summary>
        /// 获取调用指定方法所需的 capability
        /// </summary>
        internal static string? GetRequiredCapability(string method) => method switch
        {
            // 文件系统 ADS
            "fs.ads.read" or "fs.ads.write" or "fs.ads.delete" or "fs.ads.exists" or "fs.ads.isNTFS"
                => "fs.ads.access",

            // 注册表
            "registry.read" or "registry.write" or "registry.delete"
                => "system.registry.write",

            // 热键
            "hotkey.register" or "hotkey.unregister" or "hotkey.isConflict"
                => "system.hotkey",

            // 剪贴板
            "clipboard.getText" or "clipboard.setText" or "clipboard.clear"
                => "system.clipboard",
            "clipboard.startMonitoring" or "clipboard.stopMonitoring"
                => "system.clipboard.monitor",

            // Shell 选择
            "shell.getActiveFolder" or "shell.getSelectedItems" or "shell.getItemScreenRect"
                => "shell.selection",

            // 网络 HTTP
            "http.get" or "http.post" or "http.download"
                => "network.http",

            // 本地存储（无需声明，所有插件都可用）
            "storage.get" or "storage.set" or "storage.delete" or "storage.containsKey"
                => null,

            // 通知
            "app.showNotification" or "ui.showToast"
                => "system.notification",

            // 截图
            "screenshot.captureFull" or "screenshot.captureRegion"
                => "system.screenshot",

            // Shell 执行
            "app.openUrl" or "shell.openUrl" or "app.openFolder" or "shell.openFolder" or
            "app.openWithDefault" or "shell.openWithDefault"
                => "shell.execute",

            // 文件操作
            "shell.listFiles" or "shell.renameFile"
                => "file.ops",

            // 窗口信息
            "window.getForeground"
                => "window.info",
            "window.getVisible"
                => "window.info",

            "process.start" or "process.getList" or "process.kill"
                => "system.process",
            "fileOps.copy" or "fileOps.move" or "fileOps.delete" or "fileOps.exists"
                => "file.ops",
            "performance.getCpuUsage" or "performance.getMemoryInfo" or "performance.getDiskInfo" or
            "performance.getSystemInfo" or "performance.getTopByCpu" or "performance.getTopByMemory"
                => "system.performance",
            "networkPort.getTcpConnections" or "networkPort.getTcpListeners" or "networkPort.getUdpEndpoints" or
            "networkPort.findOwner" or "networkPort.isInUse" or "networkPort.getSummary"
                => "network.ports",
            "network.getStats" or "network.getSpeed" or "network.getInterfaces"
                => "network.monitor",
            "audio.getVolume" or "audio.setVolume" or "audio.getMute" or "audio.setMute" or
            "audio.increase" or "audio.decrease" or "audio.getDevices" or "audio.setDefaultDevice"
                => "system.audio",
            "power.getStatus" or "power.lock" or "power.sleep" or "power.hibernate" or
            "power.shutdown" or "power.reboot" or "power.preventSleep"
                => "system.power",
            "theme.get" or "theme.set" or "theme.toggle" or "theme.getAccentColor" or "theme.setAccentColor"
                => "system.theme",
            "wallpaper.get" or "wallpaper.set" or "wallpaper.getStyle"
                => "system.wallpaper",
            "brightness.get" or "brightness.set" or "brightness.increase" or "brightness.decrease"
                => "display.brightness",
            "pinyin.get" or "pinyin.getInitials" or "pinyin.match"
                => "text.pinyin",
            "pinyin.filter"
                => "text.pinyin",
            "input.keyPress" or "input.mouseClick" or "input.moveCursor"
                => "system.input",
            "fileSystem.enumerate" or "fileSystem.hash" or "fileSystem.metadata" or
            "fileSystem.findDuplicates" or "fileSystem.batchRename" or "fileSystem.classify" or
            "fileSystem.findLarge" or "fileSystem.searchContent"
                => "filesystem.advanced",
            "cache.cleanTemp" or "cache.cleanWindowsUpdate" or "cache.cleanBrowser" or
            "cache.emptyRecycleBin" or "cache.getStatistics" or "cache.cleanAll"
                => "system.cache",
            "schedule.create" or "schedule.delete" or "schedule.getAll" or
            "schedule.setEnabled" or "schedule.runNow"
                => "system.schedule",

            // UI 窗口
            "ui.createWindow" or "ui.confirm" or "ui.prompt" or "ui.select" or
            "ui.closeWindow" or "ui.sendMessage"
                => "ui.window",

            // 应用信息（无需权限）
            "app.getVersion" or "app.log"
                => null,

            _ => null
        };

        // 辅助方法
        private static string Arg(object?[] args, int i, string def = "") =>
            args.Length > i ? args[i]?.ToString() ?? def : def;

        private static int IntArg(object?[] args, int i, int def = 0) =>
            int.TryParse(Arg(args, i), out var value) ? value : def;

        private static long LongArg(object?[] args, int i, long def = 0) =>
            long.TryParse(Arg(args, i), out var value) ? value : def;

        private static bool BoolArg(object?[] args, int i, bool def = false) =>
            bool.TryParse(Arg(args, i), out var value) ? value : def;

        private static T EnumArg<T>(object?[] args, int i, T def) where T : struct, Enum =>
            Enum.TryParse<T>(Arg(args, i), true, out var value) ? value : def;

        private static T? JsonArg<T>(object?[] args, int i)
        {
            if (args.Length <= i || args[i] == null) return default;
            if (args[i] is System.Text.Json.JsonElement element)
                return System.Text.Json.JsonSerializer.Deserialize<T>(
                    element.GetRawText(), MessageJsonOptions);

            var json = System.Text.Json.JsonSerializer.Serialize(args[i]);
            return System.Text.Json.JsonSerializer.Deserialize<T>(json, MessageJsonOptions);
        }

        private static List<string> StringListArg(object?[] args, int i) =>
            JsonArg<List<string>>(args, i) ?? new List<string>();

        private static async Task<object?> Ok<T>(Task<HostApiResponse<T>> t) { var r = await t; return new { success = r.IsSuccess, data = r.Data, error = r.ErrorMessage }; }

        private static async Task<object?> Ok(Task<HostApiResponse> t) { var r = await t; return new { success = r.IsSuccess, error = r.ErrorMessage }; }

        private static async Task<object?> OkList<T>(Task<HostApiResponse<List<T>>> t) { var r = await t; return new { success = r.IsSuccess, data = r.Data, error = r.ErrorMessage }; }

        private static object OkObj() => new { success = true };

        private object PluginLog(object?[] args)
        {
            Log.Information("[Web:{PluginId}] {Message}", _manifest.Id, string.Join(" ", args.Select(a => a?.ToString())));
            return OkObj();
        }

        private async Task<object?> CaptureRegionToFile(object?[] args)
        {
            var x = int.Parse(Arg(args, 0, "0"));
            var y = int.Parse(Arg(args, 1, "0"));
            var w = int.Parse(Arg(args, 2, "0"));
            var h = int.Parse(Arg(args, 3, "0"));
            var result = await HostProvider.Instance.ScreenCapture.CaptureRegionAsync(x, y, w, h);
            if (!result.IsSuccess || result.Data == null)
                return new { success = false, error = result.ErrorMessage };

            // 保存到临时文件并返回路径
            var tempDir = System.IO.Path.GetTempPath();
            var fileName = $"long_screenshot_{DateTime.Now:yyyyMMddHHmmss}.png";
            var filePath = System.IO.Path.Combine(tempDir, fileName);
            using (var stream = new System.IO.FileStream(filePath, System.IO.FileMode.Create))
            {
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(result.Data));
                encoder.Save(stream);
            }
            return new { success = true, filePath = filePath.Replace("\\", "/") };
        }

        private object ShellListFiles(string dir)
        {
            if (!Directory.Exists(dir)) return new { success = false };
            var files = Directory.GetFiles(dir).Select(f => new { name = Path.GetFileName(f), path = f.Replace("\\", "/") }).ToList();
            return new { success = true, files };
        }

        private async Task<object?> ShellRenameFileAsync(string oldPath, string newName)
        {
            var directory = Path.GetDirectoryName(oldPath);
            if (directory == null)
                return new { success = false, error = "源路径无效" };

            var newPath = Path.Combine(directory, newName);
            var result = await HostProvider.Instance.FileOps.MoveAsync(oldPath, newPath);
            return new { success = result.IsSuccess, error = result.ErrorMessage };
        }

        private async Task<object?> WindowGetForeground()
        {
            var result = await HostProvider.Instance.WindowInfo.GetForegroundWindowInfoAsync();
            return result.IsSuccess ? result.Data! : new { success = false };
        }

        private object UIToast(string msg)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                Views.FloatingHudWindow.ShowToast(msg));
            return OkObj();
        }

        // === HTTP 辅助方法 ===

        private static Dictionary<string, string>? ParseHeaders(object?[] args, int index)
        {
            if (args.Length <= index || args[index] == null) return null;
            try
            {
                var json = args[index]?.ToString();
                if (string.IsNullOrEmpty(json)) return null;
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json!);
            }
            catch { return null; }
        }

        private async Task<object?> HttpPost(object?[] args)
        {
            var url = Arg(args, 0);
            var body = Arg(args, 1);
            var contentType = Arg(args, 2, "application/json");
            var headers = ParseHeaders(args, 3);
            var r = await HostProvider.Instance.Http.PostAsync(url, body, contentType, headers);
            return new { success = r.IsSuccess, data = r.Data, error = r.ErrorMessage };
        }

        private async Task<object?> HttpDownload(object?[] args)
        {
            var url = Arg(args, 0);
            var r = await HostProvider.Instance.Http.DownloadAsync(url);
            if (!r.IsSuccess || r.Data == null)
                return new { success = false, error = r.ErrorMessage };

            // 保存到临时文件
            var tempDir = System.IO.Path.GetTempPath();
            var fileName = $"long_download_{DateTime.Now:yyyyMMddHHmmss}_{System.IO.Path.GetFileName(new Uri(url).AbsolutePath)}";
            if (string.IsNullOrEmpty(System.IO.Path.GetExtension(fileName)))
                fileName += ".bin";
            var filePath = System.IO.Path.Combine(tempDir, fileName);
            await System.IO.File.WriteAllBytesAsync(filePath, r.Data);
            return new { success = true, filePath = filePath.Replace("\\", "/"), size = r.Data.Length };
        }

        private async Task<object?> HotKeyRegister(object?[] args)
        {
            var hotkey = Arg(args, 0);
            var r = await HostProvider.Instance.HotKey.RegisterAsync(hotkey, () =>
            {
                PostWebMessage(
                    System.Text.Json.JsonSerializer.Serialize(new { type = "hotkey", hotkey }));
            });
            return new { success = r.IsSuccess, error = r.ErrorMessage };
        }

        private async Task<object?> HotKeyUnregister(object?[] args)
        {
            var hotkey = Arg(args, 0);
            var r = await HostProvider.Instance.HotKey.UnregisterAsync(hotkey);
            return new { success = r.IsSuccess, error = r.ErrorMessage };
        }

        private async Task<object?> UICreateWindow(object?[] args)
        {
            var title = Arg(args, 0);
            var htmlContent = Arg(args, 1);
            var width = args.Length > 2 ? int.Parse(Arg(args, 2, "600")) : 600;
            var height = args.Length > 3 ? int.Parse(Arg(args, 3, "400")) : 400;
            var resizable = args.Length > 4 ? bool.Parse(Arg(args, 4, "true")) : true;

            var r = await HostProvider.Instance.UI.CreateWindowAsync(title, htmlContent, width, height, resizable);
            return new { success = r.IsSuccess, data = r.Data, error = r.ErrorMessage };
        }

        internal static string BuildJsBridge(string pluginId)
        {
            var js = @"
(function() {
var _id=0,_pending={},_hotkeys={};
function call(method,args){
  return new Promise(function(resolve,reject){
    var id=++_id;
    _pending[id]={resolve:resolve,reject:reject};
    window.chrome.webview.postMessage({id:id,method:method,args:args||[]});
  });
}
window.long = {
  app: {
    openUrl: function(url){return call('app.openUrl',[url]);},
    openFolder: function(path){return call('app.openFolder',[path]);},
    openWithDefault: function(path){return call('app.openWithDefault',[path]);},
    showNotification: function(title,body){return call('app.showNotification',[title,body]);},
    getVersion: function(){return call('app.getVersion',[]);},
    log: function(){return call('app.log',Array.prototype.slice.call(arguments));}
  },
  clipboard: {
    getText: function(){return call('clipboard.getText',[]);},
    setText: function(t){return call('clipboard.setText',[t]);},
    clear: function(){return call('clipboard.clear',[]);},
    startMonitoring: function(){return call('clipboard.startMonitoring',[]);},
    stopMonitoring: function(){return call('clipboard.stopMonitoring',[]);}
  },
  shell: {
    getActiveFolder: function(){return call('shell.getActiveFolder',[]);},
    getSelectedItems: function(){return call('shell.getSelectedItems',[]);},
    getItemScreenRect: function(){return call('shell.getItemScreenRect',[]);},
    listFiles: function(dir){return call('shell.listFiles',[dir]);},
    renameFile: function(oldPath,newName){return call('shell.renameFile',[oldPath,newName]);},
    openUrl: function(url){return call('shell.openUrl',[url]);},
    openFolder: function(path){return call('shell.openFolder',[path]);},
    openWithDefault: function(path){return call('shell.openWithDefault',[path]);}
  },
  fs: { ads: {
    read: function(p,s){return call('fs.ads.read',[p,s||'long_note']);},
    write: function(p,c,s){return call('fs.ads.write',[p,c,s||'long_note']);},
    delete: function(p,s){return call('fs.ads.delete',[p,s||'long_note']);},
    exists: function(p,s){return call('fs.ads.exists',[p,s||'long_note']);},
    isNTFS: function(p){return call('fs.ads.isNTFS',[p]);}
  }},
  hotkey: {
    register: function(h,callback){if(typeof callback==='function')_hotkeys[h]=callback;return call('hotkey.register',[h]);},
    unregister: function(h){delete _hotkeys[h];return call('hotkey.unregister',[h]);},
    isConflict: function(h){return call('hotkey.isConflict',[h]);}
  },
  registry: {
    read: function(k,v){return call('registry.read',[k,v]);},
    write: function(k,n,v){return call('registry.write',[k,n,v]);},
    delete: function(k,v){return call('registry.delete',[k,v]);}
  },
  storage: {
    get: function(k){return call('storage.get',[k]);},
    set: function(k,v){return call('storage.set',[k,v]);},
    delete: function(k){return call('storage.delete',[k]);},
    containsKey: function(k){return call('storage.containsKey',[k]);}
  },
  process: {
    start: function(path,args){return call('process.start',[path,args]);},
    getList: function(filter){return call('process.getList',[filter]);},
    kill: function(id){return call('process.kill',[id]);}
  },
  fileOps: {
    copy: function(source,dest){return call('fileOps.copy',[source,dest]);},
    move: function(source,dest){return call('fileOps.move',[source,dest]);},
    delete: function(path){return call('fileOps.delete',[path]);},
    exists: function(path){return call('fileOps.exists',[path]);}
  },
  performance: {
    getCpuUsage: function(){return call('performance.getCpuUsage',[]);},
    getMemoryInfo: function(){return call('performance.getMemoryInfo',[]);},
    getDiskInfo: function(){return call('performance.getDiskInfo',[]);},
    getSystemInfo: function(){return call('performance.getSystemInfo',[]);},
    getTopByCpu: function(count){return call('performance.getTopByCpu',[count||10]);},
    getTopByMemory: function(count){return call('performance.getTopByMemory',[count||10]);}
  },
  networkPort: {
    getTcpConnections: function(){return call('networkPort.getTcpConnections',[]);},
    getTcpListeners: function(){return call('networkPort.getTcpListeners',[]);},
    getUdpEndpoints: function(){return call('networkPort.getUdpEndpoints',[]);},
    findPortOwner: function(port,protocol){return call('networkPort.findOwner',[port,protocol||'tcp']);},
    isPortInUse: function(port,protocol){return call('networkPort.isInUse',[port,protocol||'tcp']);},
    getSummary: function(){return call('networkPort.getSummary',[]);}
  },
  network: {
    getStats: function(){return call('network.getStats',[]);},
    getSpeed: function(){return call('network.getSpeed',[]);},
    getInterfaces: function(){return call('network.getInterfaces',[]);}
  },
  audio: {
    getVolume: function(){return call('audio.getVolume',[]);},
    setVolume: function(volume){return call('audio.setVolume',[volume]);},
    getMute: function(){return call('audio.getMute',[]);},
    setMute: function(mute){return call('audio.setMute',[mute]);},
    increase: function(step){return call('audio.increase',[step||5]);},
    decrease: function(step){return call('audio.decrease',[step||5]);},
    getDevices: function(){return call('audio.getDevices',[]);},
    setDefaultDevice: function(id){return call('audio.setDefaultDevice',[id]);}
  },
  power: {
    getStatus: function(){return call('power.getStatus',[]);},
    getBatteryStatus: function(){return call('power.getStatus',[]);},
    lock: function(){return call('power.lock',[]);},
    sleep: function(){return call('power.sleep',[]);},
    hibernate: function(){return call('power.hibernate',[]);},
    shutdown: function(delay){return call('power.shutdown',[delay||0]);},
    reboot: function(delay){return call('power.reboot',[delay||0]);},
    preventSleep: function(prevent){return call('power.preventSleep',[prevent]);}
  },
  theme: {
    get: function(){return call('theme.get',[]);},
    set: function(theme){return call('theme.set',[theme]);},
    toggle: function(){return call('theme.toggle',[]);},
    getAccentColor: function(){return call('theme.getAccentColor',[]);},
    setAccentColor: function(color){return call('theme.setAccentColor',[color]);}
  },
  wallpaper: {
    get: function(){return call('wallpaper.get',[]);},
    set: function(path,style){return call('wallpaper.set',[path,style||'fill']);},
    getStyle: function(){return call('wallpaper.getStyle',[]);}
  },
  brightness: {
    get: function(){return call('brightness.get',[]);},
    set: function(value){return call('brightness.set',[value]);},
    increase: function(step){return call('brightness.increase',[step||10]);},
    decrease: function(step){return call('brightness.decrease',[step||10]);}
  },
  pinyin: {
    get: function(text){return call('pinyin.get',[text]);},
    getInitials: function(text){return call('pinyin.getInitials',[text]);},
    match: function(text,query){return call('pinyin.match',[text,query]);},
    filter: function(items,query){return call('pinyin.filter',[items,query]);}
  },
  input: {
    keyPress: function(vkCode){return call('input.keyPress',[vkCode]);},
    mouseClick: function(x,y,rightButton){return call('input.mouseClick',[x,y,!!rightButton]);},
    moveCursor: function(x,y){return call('input.moveCursor',[x,y]);}
  },
  fileSystem: {
    enumerate: function(path,pattern,recursive){return call('fileSystem.enumerate',[path,pattern||'*.*',recursive!==false]);},
    hash: function(path){return call('fileSystem.hash',[path]);},
    metadata: function(path){return call('fileSystem.metadata',[path]);},
    findDuplicates: function(path){return call('fileSystem.findDuplicates',[path]);},
    batchRename: function(operations){return call('fileSystem.batchRename',[operations||[]]);},
    classify: function(path,mode){return call('fileSystem.classify',[path,mode||'ByExtension']);},
    findLarge: function(path,minSizeBytes){return call('fileSystem.findLarge',[path,minSizeBytes]);},
    searchContent: function(path,keyword,extensions){return call('fileSystem.searchContent',[path,keyword,extensions||[]]);}
  },
  cache: {
    cleanTemp: function(){return call('cache.cleanTemp',[]);},
    cleanWindowsUpdate: function(){return call('cache.cleanWindowsUpdate',[]);},
    cleanBrowser: function(browser){return call('cache.cleanBrowser',[browser]);},
    emptyRecycleBin: function(){return call('cache.emptyRecycleBin',[]);},
    getStatistics: function(){return call('cache.getStatistics',[]);},
    cleanAll: function(){return call('cache.cleanAll',[]);}
  },
  schedule: {
    create: function(task){return call('schedule.create',[task]);},
    delete: function(taskId){return call('schedule.delete',[taskId]);},
    getAll: function(){return call('schedule.getAll',[]);},
    setEnabled: function(taskId,enabled){return call('schedule.setEnabled',[taskId,enabled]);},
    runNow: function(taskId){return call('schedule.runNow',[taskId]);}
  },
  ui: {
    showToast: function(m){return call('ui.showToast',[m]);},
    createWindow: function(title,htmlContent,width,height,resizable){return call('ui.createWindow',[title,htmlContent,width,height,resizable]);},
    confirm: function(message,title){return call('ui.confirm',[message,title||'确认']);},
    prompt: function(message,title,defaultValue){return call('ui.prompt',[message,title||'输入',defaultValue||'']);},
    select: function(message,options,title){return call('ui.select',[message,options||[],title||'选择']);},
    closeWindow: function(windowId){return call('ui.closeWindow',[windowId]);},
    sendMessage: function(windowId,message){return call('ui.sendMessage',[windowId,message]);}
  },
  screenshot: {
    captureFull: function(){return call('screenshot.captureFull',[]);},
    captureRegion: function(x,y,w,h){return call('screenshot.captureRegion',[x,y,w,h]);}
  },
  http: {
    get: function(url,headers){return call('http.get',[url,headers]);},
    post: function(url,body,contentType,headers){return call('http.post',[url,body,contentType||'application/json',headers]);},
    download: function(url){return call('http.download',[url]);}
  },
  window: {
    getForeground: function(){return call('window.getForeground',[]);},
    getVisible: function(){return call('window.getVisible',[]);}
  }
};
window.chrome.webview.addEventListener('message',function(e){
  try{
    var m=typeof e.data==='string'?JSON.parse(e.data):e.data;
    if(m.id&&_pending[m.id]){
      if(m.error)_pending[m.id].reject(new Error(m.error));
      else _pending[m.id].resolve(m.result);
      delete _pending[m.id];
    }
    if(m.type==='hotkey'){
      if(typeof _hotkeys[m.hotkey]==='function')_hotkeys[m.hotkey]();
      else console.log('[Long] key:',m.hotkey);
    }
  }catch(ex){}
});
console.log('[Long] Bridge ready · __PLUGIN_ID__');
})();";

            return js.Replace("__PLUGIN_ID__", pluginId);
        }
    }

    internal class JsMessage
    {
        public int Id { get; set; }
        public string Method { get; set; } = string.Empty;
        public object?[] Args { get; set; } = Array.Empty<object?>();
    }
}
