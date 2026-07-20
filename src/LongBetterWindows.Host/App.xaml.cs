using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;
using LongBetterWindows.Host.Views;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Serilog;
using Wpf.Ui.Appearance;

namespace LongBetterWindows.Host
{
    public partial class App : Application
    {
        internal static bool KeepPaletteVisibleForQuality
            => Current is App { _qualityOpenPalette: true };
        internal static bool KeepSuperPanelVisibleForQuality
            => Current is App { _qualityOpenSuperPanel: true };
        internal static LpakInstaller? PackageInstaller
            => (Current as App)?._packageInstaller;

        public static event Action<bool>? ThemeChanged;
        public static bool IsExiting { get; set; }
        public static string ProductVersion
            => typeof(App).Assembly
                   .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                   .InformationalVersion.Split('+')[0]
               ?? typeof(App).Assembly.GetName().Version?.ToString()
               ?? "0.0.0";
        private string? _directNotePath;
        private bool _isDirectNoteMode;
        private int _pluginRuntimeStarted;
        private bool _showDesignSystemPreview;
        private PluginScanner? _pluginScanner;
        private LpakInstaller? _packageInstaller;
        private static bool _currentIsLight;
        private static bool _forceHighContrast;
        private static bool _forceReduceMotion;
        private string? _requestedCommandKey;
        private string? _requestedCommandText;
        private string? _requestedPluginsDir;
        private bool _exitAfterCommand;
        private bool _qualityOpenPalette;
        private bool _qualityOpenSuperPanel;
        private bool _qualityOpenMarket;
        private string? _qualityMarketCatalog;
        private string? _qualityMarketTrustStore;
        private bool _qualityUseLiveContext;
        private int _qualityIdleMilliseconds;
        private string? _qualityCapturePath;
        private string _qualityCaptureView = "main";
        private int _qualityRenderDpi = 96;
        private int _qualityCaptureDelayMilliseconds = 700;
        private int _qualityCaptureWidth;
        private int _qualityCaptureHeight;
        internal bool ShowDesignSystemPreviewRequested => _showDesignSystemPreview;
        internal bool ShowMarketForQualityRequested => _qualityOpenMarket;
        internal string? QualityMarketplaceCatalogPath => _qualityMarketCatalog;
        internal string? QualityMarketplaceTrustStorePath => _qualityMarketTrustStore;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            Log.Information("Long窗口·全能助手 正在启动...");

            ServicesInitializer.Initialize();
            Log.Information("所有服务已初始化。");

            _forceHighContrast = e.Args.Any(argument =>
                string.Equals(argument, "--quality-high-contrast", StringComparison.OrdinalIgnoreCase));
            _forceReduceMotion = e.Args.Any(argument =>
                string.Equals(argument, "--quality-reduce-motion", StringComparison.OrdinalIgnoreCase));
            Log.Information(
                "Quality accessibility mode: HighContrast={HighContrast}, ReducedMotion={ReducedMotion}",
                SystemParameters.HighContrast || _forceHighContrast,
                !SystemParameters.ClientAreaAnimation || _forceReduceMotion);

            // 命令行主题覆盖仅对本次进程生效，供视觉回归与诊断使用。
            var themeOverride = ReadArgument(e.Args, "--theme")?.ToLowerInvariant();
            var themeSetting = themeOverride is "light" or "dark" or "system"
                ? themeOverride
                : ReadThemeSetting();
            bool isLight;
            if (themeSetting == "light")
                isLight = true;
            else if (themeSetting == "dark")
                isLight = false;
            else
            {
                var appsUseLight = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                isLight = appsUseLight is 1;
            }
            ApplicationThemeManager.Apply(isLight ? ApplicationTheme.Light : ApplicationTheme.Dark);
            _currentIsLight = isLight;
            UpdateThemeResources(isLight);
            UpdateMotionResources();
            SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;

            // 检查命令行 --note 参数（右键菜单触发）
            for (int i = 0; i < e.Args.Length; i++)
            {
                if (e.Args[i] == "--note" && i + 1 < e.Args.Length)
                {
                    _directNotePath = e.Args[i + 1];
                    _isDirectNoteMode = true;
                    break;
                }
                if (e.Args[i] == "--design-system-preview")
                    _showDesignSystemPreview = true;
            }

            if (_showDesignSystemPreview)
                Log.Information("已请求 Long Design System 预览窗口");

            _requestedCommandKey = ReadArgument(e.Args, "--run-command")?.ToLowerInvariant();
            _requestedCommandText = ReadArgument(e.Args, "--command-text");
            _requestedPluginsDir = ReadArgument(e.Args, "--plugins-dir");
            _exitAfterCommand = e.Args.Any(argument =>
                string.Equals(argument, "--exit-after-command", StringComparison.OrdinalIgnoreCase));
            _qualityOpenPalette = e.Args.Any(argument =>
                string.Equals(argument, "--quality-open-palette", StringComparison.OrdinalIgnoreCase));
            _qualityOpenSuperPanel = e.Args.Any(argument =>
                string.Equals(argument, "--quality-open-super-panel", StringComparison.OrdinalIgnoreCase));
            _qualityOpenMarket = e.Args.Any(argument =>
                string.Equals(argument, "--quality-open-market", StringComparison.OrdinalIgnoreCase));
            _qualityMarketCatalog = ReadArgument(e.Args, "--quality-market-catalog");
            _qualityMarketTrustStore = ReadArgument(e.Args, "--quality-market-trust-store");
            _qualityUseLiveContext = e.Args.Any(argument =>
                string.Equals(argument, "--quality-live-context", StringComparison.OrdinalIgnoreCase));
            _qualityIdleMilliseconds = ReadIntegerArgument(e.Args, "--quality-idle-ms", 0, 60_000);
            _qualityCapturePath = ReadArgument(e.Args, "--quality-capture");
            _qualityCaptureView = ReadArgument(e.Args, "--quality-capture-view")?.ToLowerInvariant() ?? "main";
            _qualityRenderDpi = ReadIntegerArgument(e.Args, "--quality-render-dpi", 96, 384);
            _qualityCaptureDelayMilliseconds = ReadIntegerArgument(
                e.Args, "--quality-capture-delay-ms", 100, 10_000);
            _qualityCaptureWidth = ReadIntegerArgument(e.Args, "--quality-width", 0, 3840);
            _qualityCaptureHeight = ReadIntegerArgument(e.Args, "--quality-height", 0, 2160);

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                Log.Fatal(args.ExceptionObject as Exception, "未处理的 AppDomain 异常");
            };

            DispatcherUnhandledException += (sender, args) =>
            {
                Log.Error(args.Exception, "未处理的 UI 线程异常");
                args.Handled = true;
            };

        }

        internal void StartPluginRuntime()
        {
            if (_isDirectNoteMode || Interlocked.Exchange(ref _pluginRuntimeStarted, 1) != 0)
                return;

            // Wait until the main window handle and hotkey service are ready.
            Dispatcher.BeginInvoke(
                new Action(() => _ = LoadPluginsAsync()),
                DispatcherPriority.ApplicationIdle);
        }

        private async Task LoadPluginsAsync()
        {
            try
            {
                _pluginScanner = new PluginScanner(_requestedPluginsDir);
                _packageInstaller = new LpakInstaller(_pluginScanner, _requestedPluginsDir);
                var recovered = await _packageInstaller.RecoverInterruptedTransactionsAsync();
                if (recovered > 0)
                    Log.Warning("启动时恢复了 {Count} 个中断的插件事务", recovered);
                var installed = await _packageInstaller.InstallAllFromDirectoryAsync();
                if (installed > 0)
                    Log.Information("安装了 {Count} 个 .lpak 插件", installed);

                await _pluginScanner.ScanAsync();
                Log.Information("插件加载完成，共 {Count} 个", _pluginScanner.LoadedPlugins.Count);

                if (!string.IsNullOrWhiteSpace(_requestedCommandKey))
                    await RunRequestedCommandAsync(_requestedCommandKey, _requestedCommandText);

                if (_qualityOpenPalette)
                    CommandPaletteWindow.ShowPalette();
                if (_qualityOpenSuperPanel)
                {
                    if (_qualityUseLiveContext)
                        SuperPanelWindow.ShowPanel();
                    else
                        SuperPanelWindow.ShowPanelForQuality();
                }

                if (!string.IsNullOrWhiteSpace(_qualityCapturePath))
                    await RunQualityCaptureAsync();

                if (_qualityIdleMilliseconds > 0)
                    await RunQualityIdleProbeAsync(_qualityIdleMilliseconds);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "插件加载失败");
                if (!string.IsNullOrWhiteSpace(_qualityCapturePath))
                {
                    Shutdown(3);
                    return;
                }
                await ServicesInitializer.Notification.ShowAsync(
                    "插件加载出错", "部分插件未能正确加载，请查看日志。");
            }
        }

        private async Task RunQualityCaptureAsync()
        {
            await Task.Delay(_qualityCaptureDelayMilliseconds);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            var target = _qualityCaptureView switch
            {
                "palette" => Windows.OfType<CommandPaletteWindow>().FirstOrDefault(x => x.IsVisible),
                "super-panel" => Windows.OfType<SuperPanelWindow>().FirstOrDefault(x => x.IsVisible),
                "plugin" => Windows.OfType<PluginWindowHost>().FirstOrDefault(x => x.IsVisible) ?? MainWindow,
                "main" or "market" => MainWindow,
                _ => throw new InvalidDataException($"不支持的质量截图视图：{_qualityCaptureView}"),
            } ?? throw new InvalidDataException($"质量截图窗口未显示：{_qualityCaptureView}");

            if (_qualityCaptureWidth > 0) target.Width = _qualityCaptureWidth;
            if (_qualityCaptureHeight > 0) target.Height = _qualityCaptureHeight;
            target.WindowState = WindowState.Normal;
            target.UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

            var logicalWidth = Math.Max(1, target.ActualWidth);
            var logicalHeight = Math.Max(1, target.ActualHeight);
            var path = Path.GetFullPath(_qualityCapturePath!);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (_qualityCaptureView == "plugin")
            {
                var webView = FindVisualChild<WebView2>(target)
                    ?? throw new InvalidDataException("质量截图未找到活动 WebView2。");
                if (webView.CoreWebView2 == null)
                    throw new InvalidDataException("质量截图时 WebView2 尚未初始化。");
                await using (var stream = new FileStream(
                    path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await webView.CoreWebView2.CapturePreviewAsync(
                        CoreWebView2CapturePreviewImageFormat.Png, stream);
                    await stream.FlushAsync();
                }
                var webDpi = VisualTreeHelper.GetDpi(webView);
                await WriteCaptureMetadataAsync(
                    path, logicalWidth, logicalHeight,
                    Math.Max(1, (int)Math.Ceiling(webView.ActualWidth * webDpi.DpiScaleX)),
                    Math.Max(1, (int)Math.Ceiling(webView.ActualHeight * webDpi.DpiScaleY)),
                    webDpi.PixelsPerInchX, "webview_preview");
                Shutdown(0);
                return;
            }
            var pixelWidth = Math.Max(1, (int)Math.Ceiling(logicalWidth * _qualityRenderDpi / 96d));
            var pixelHeight = Math.Max(1, (int)Math.Ceiling(logicalHeight * _qualityRenderDpi / 96d));
            var bitmap = new RenderTargetBitmap(
                pixelWidth, pixelHeight, _qualityRenderDpi, _qualityRenderDpi, PixelFormats.Pbgra32);
            bitmap.Render(target);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using (var stream = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                encoder.Save(stream);
                await stream.FlushAsync();
            }

            var actualDpi = VisualTreeHelper.GetDpi(target);
            await WriteCaptureMetadataAsync(
                path, logicalWidth, logicalHeight, pixelWidth, pixelHeight,
                actualDpi.PixelsPerInchX, "wpf_render_target");
            Log.Information(
                "质量截图完成: View={View}, RenderDpi={RenderDpi}, ActualDpi={ActualDpi}, Path={Path}",
                _qualityCaptureView, _qualityRenderDpi, actualDpi.PixelsPerInchX, path);
            Shutdown(0);
        }

        private async Task WriteCaptureMetadataAsync(
            string path, double logicalWidth, double logicalHeight,
            int pixelWidth, int pixelHeight, double actualDpi, string captureKind)
        {
            var metadata = new
            {
                schema_version = 1,
                captured_at = DateTimeOffset.UtcNow,
                view = _qualityCaptureView,
                theme = _currentIsLight ? "light" : "dark",
                render_dpi = _qualityRenderDpi,
                actual_monitor_dpi = actualDpi,
                capture_kind = captureKind,
                logical_width = logicalWidth,
                logical_height = logicalHeight,
                pixel_width = pixelWidth,
                pixel_height = pixelHeight,
                high_contrast = SystemParameters.HighContrast || _forceHighContrast,
                reduced_motion = !SystemParameters.ClientAreaAnimation || _forceReduceMotion,
            };
            await File.WriteAllTextAsync(
                path + ".json", System.Text.Json.JsonSerializer.Serialize(
                    metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T match) return match;
                var nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private async Task RunQualityIdleProbeAsync(int delayMilliseconds)
        {
            await Task.Delay(delayMilliseconds);
            var process = System.Diagnostics.Process.GetCurrentProcess();
            process.Refresh();
            Log.Information(
                "质量驻留采样: Plugins={PluginCount}, Commands={CommandCount}, WorkingSetMB={WorkingSetMB:F1}, PrivateMB={PrivateMB:F1}, HighContrast={HighContrast}, ReducedMotion={ReducedMotion}",
                _pluginScanner?.LoadedPlugins.Count ?? 0,
                HostProvider.Instance.PluginStore.Commands.Count,
                process.WorkingSet64 / 1024d / 1024d,
                process.PrivateMemorySize64 / 1024d / 1024d,
                SystemParameters.HighContrast || _forceHighContrast,
                !SystemParameters.ClientAreaAnimation || _forceReduceMotion);
            Shutdown(0);
        }

        private async Task RunRequestedCommandAsync(string commandKey, string? text)
        {
            var registry = HostProvider.Instance.PluginStore;
            var descriptor = registry.Commands.Get(commandKey);
            if (descriptor == null)
            {
                Log.Error("命令行请求的命令不存在: {CommandKey}", commandKey);
                if (_exitAfterCommand) Shutdown(2);
                return;
            }

            var inputType = !string.IsNullOrEmpty(text)
                            && descriptor.Command.AcceptedInputs.Contains(AcceptedInputType.Text)
                ? AcceptedInputType.Text
                : AcceptedInputType.None;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Log.Information("开始执行命令行命令: {CommandKey}", commandKey);
            var result = await new CommandExecutor(registry).ExecuteAsync(
                descriptor.Key,
                new PluginCommandInvocation
                {
                    CommandId = descriptor.Command.Id,
                    InputType = inputType,
                    Text = inputType == AcceptedInputType.Text ? text : null,
                });
            stopwatch.Stop();

            Log.Information("命令行命令 {CommandKey} 完成: Success={Success}, ElapsedMs={ElapsedMs:F1}",
                commandKey, result.IsSuccess, stopwatch.Elapsed.TotalMilliseconds);
            if (_exitAfterCommand)
                Shutdown(result.IsSuccess ? 0 : 3);
        }

        protected override async void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            if (_directNotePath != null)
            {
                var path = _directNotePath;
                _directNotePath = null;

                // 隐藏主窗口（如果显示了）
                if (MainWindow != null)
                    MainWindow.Hide();

                await ShowDirectNoteAsync(path);

                // 右键菜单模式：完成后退出
                Shutdown();
            }
        }

        private async Task ShowDirectNoteAsync(string folderPath)
        {
            Log.Information("右键菜单备注模式: {Path}", folderPath);

            if (!Directory.Exists(folderPath))
            {
                FloatingHudWindow.ShowToast("目标文件夹不存在。");
                return;
            }

            var noteResult = await ServicesInitializer.ADS
                .ReadAsync(folderPath, "long_note");

            string? existingNote = null;
            if (noteResult.IsSuccess && noteResult.Data != null)
                existingNote = noteResult.Data;

            Dispatcher.Invoke(() =>
            {
                var area = SystemParameters.WorkArea;
                double x = area.Left + (area.Width - 320) / 2;
                double y = area.Top + (area.Height - 150) / 2;

                FloatingHudWindow.ShowAt(x, y, existingNote, folderPath, async (text) =>
                {
                    if (string.IsNullOrEmpty(text))
                        await ServicesInitializer.ADS.DeleteAsync(folderPath, "long_note");
                    else
                        await ServicesInitializer.ADS.WriteAsync(folderPath, "long_note", text);

                    Log.Information("右键备注已保存: {Path}", folderPath);
                });
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
            HostProvider.Instance.PluginStore.ShutdownAllAsync().GetAwaiter().GetResult();
            _pluginScanner?.Dispose();
            // 清理服务资源
            ServicesInitializer.DisposeAll();
            Log.Information("Long窗口·全能助手 已退出。");
            Log.CloseAndFlush();
            base.OnExit(e);
        }

        #region Theme Persistence

        private static readonly string ThemeConfigDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LongBetterWindows");
        private static readonly string ThemeConfigPath =
            Path.Combine(ThemeConfigDir, "theme.json");

        private static string? ReadThemeSetting()
        {
            try
            {
                if (File.Exists(ThemeConfigPath))
                {
                    var json = File.ReadAllText(ThemeConfigPath);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("theme", out var prop))
                        return prop.GetString();
                }
            }
            catch (Exception ex) { Log.Warning(ex, "读取主题配置失败"); }
            return null;
        }

        private static string? ReadArgument(IReadOnlyList<string> arguments, string name)
        {
            for (var index = 0; index < arguments.Count - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
                    return arguments[index + 1].Trim();
            }

            return null;
        }

        private static int ReadIntegerArgument(
            IReadOnlyList<string> arguments,
            string name,
            int fallback,
            int maximum)
        {
            var value = ReadArgument(arguments, name);
            return int.TryParse(value, out var parsed)
                ? Math.Clamp(parsed, 0, maximum)
                : fallback;
        }

        public static bool IsFirstRun()
        {
            return ReadThemeSetting() == null; // 无配置 = 首次运行
        }

        public static void MarkOnboarded()
        {
            try
            {
                if (!Directory.Exists(ThemeConfigDir))
                    Directory.CreateDirectory(ThemeConfigDir);
                // 写一个有效配置，让 IsFirstRun 返回 false
                var json = System.Text.Json.JsonSerializer.Serialize(
                    new { theme = "system", onboarded = "true" });
                File.WriteAllText(ThemeConfigPath, json);
            }
            catch { }
        }

        public static void SaveThemeSetting(bool isLight)
        {
            try
            {
                if (!Directory.Exists(ThemeConfigDir))
                    Directory.CreateDirectory(ThemeConfigDir);
                var json = System.Text.Json.JsonSerializer.Serialize(
                    new { theme = isLight ? "light" : "dark" });
                File.WriteAllText(ThemeConfigPath, json);
            }
            catch { /* best effort */ }
        }

        /// <summary>动态更新自定义主题资源，使 DynamicResource 绑定响应主题切换</summary>
        public static void UpdateThemeResources(bool isLight)
        {
            _currentIsLight = isLight;
            var r = Current.Resources;
            var palette = SystemParameters.HighContrast || _forceHighContrast
                ? HighContrastPalette
                : isLight ? LightPalette : DarkPalette;
            foreach (var (key, value) in palette)
                r[key] = (Color)ColorConverter.ConvertFromString(value);
            if (SystemParameters.HighContrast || _forceHighContrast)
            {
                r["Long.Brush.Accent.Gradient"] = new SolidColorBrush(SystemColors.HighlightColor);
            }
            else
            {
                var accent = (Color)r["Long.Color.Accent.Primary"];
                var cyan = (Color)r["Long.Color.Brand.Cyan"];
                r["Long.Brush.Accent.Gradient"] = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new(accent, 0),
                        new(cyan, 1),
                    }, new Point(0, 0), new Point(1, 1));
            }

            ThemeChanged?.Invoke(isLight);
        }

        private static void SystemParameters_StaticPropertyChanged(
            object? sender,
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(SystemParameters.HighContrast) or nameof(SystemParameters.ClientAreaAnimation)))
                return;

            Current.Dispatcher.BeginInvoke(() =>
            {
                UpdateMotionResources();
                UpdateThemeResources(_currentIsLight);
            });
        }

        public static void UpdateMotionResources()
        {
            var reduceMotion = !SystemParameters.ClientAreaAnimation || _forceReduceMotion;
            Current.Resources["Long.Motion.Fast"] = new Duration(
                reduceMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(100));
            Current.Resources["Long.Motion.Normal"] = new Duration(
                reduceMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(180));
            Current.Resources["Long.Motion.Slow"] = new Duration(
                reduceMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(280));
            Current.Resources["Long.Motion.Loading"] = new Duration(
                reduceMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(1200));
        }

        private static readonly IReadOnlyDictionary<string, string> DarkPalette =
            new Dictionary<string, string>
            {
                ["Long.Color.Background.Base"] = "#0B0D12",
                ["Long.Color.Background.Raised"] = "#11151D",
                ["Long.Color.Surface.Card"] = "#171C27",
                ["Long.Color.Surface.Hover"] = "#202838",
                ["Long.Color.Surface.Pressed"] = "#283247",
                ["Long.Color.Surface.Overlay"] = "#F2131721",
                ["Long.Color.Stroke.Default"] = "#2A3242",
                ["Long.Color.Stroke.Strong"] = "#465269",
                ["Long.Color.Text.Primary"] = "#F4F6FB",
                ["Long.Color.Text.Secondary"] = "#B2BACB",
                ["Long.Color.Text.Muted"] = "#7C879E",
                ["Long.Color.Accent.Primary"] = "#7059F5",
                ["Long.Color.Accent.Hover"] = "#765EE8",
                ["Long.Color.Accent.Pressed"] = "#6248DE",
                ["Long.Color.Accent.Soft"] = "#338B78FF",
                ["Long.Color.Focus"] = "#785CC8FF",
                ["Long.Color.State.Success"] = "#47D18C",
                ["Long.Color.State.Warning"] = "#FFB84D",
                ["Long.Color.State.Danger"] = "#FF647C",
                ["Long.Color.ScrollThumb"] = "#59657A",
                ["Long.Color.ScrollThumbHover"] = "#8290A8",
            };

        private static readonly IReadOnlyDictionary<string, string> LightPalette =
            new Dictionary<string, string>
            {
                ["Long.Color.Background.Base"] = "#F4F6FA",
                ["Long.Color.Background.Raised"] = "#FAFBFD",
                ["Long.Color.Surface.Card"] = "#FFFFFF",
                ["Long.Color.Surface.Hover"] = "#EEF1F8",
                ["Long.Color.Surface.Pressed"] = "#E3E7F1",
                ["Long.Color.Surface.Overlay"] = "#F7FFFFFF",
                ["Long.Color.Stroke.Default"] = "#DCE1EB",
                ["Long.Color.Stroke.Strong"] = "#B8C0CF",
                ["Long.Color.Text.Primary"] = "#171A22",
                ["Long.Color.Text.Secondary"] = "#505B70",
                ["Long.Color.Text.Muted"] = "#667085",
                ["Long.Color.Accent.Primary"] = "#6847F5",
                ["Long.Color.Accent.Hover"] = "#795CFF",
                ["Long.Color.Accent.Pressed"] = "#5637D8",
                ["Long.Color.Accent.Soft"] = "#1F6847F5",
                ["Long.Color.Focus"] = "#665CC8FF",
                ["Long.Color.State.Success"] = "#148A57",
                ["Long.Color.State.Warning"] = "#B56500",
                ["Long.Color.State.Danger"] = "#D93652",
                ["Long.Color.ScrollThumb"] = "#A7B0C0",
                ["Long.Color.ScrollThumbHover"] = "#7F8A9E",
            };

        private static IReadOnlyDictionary<string, string> HighContrastPalette
            => new Dictionary<string, string>
            {
                ["Long.Color.Background.Base"] = SystemColors.WindowColor.ToString(),
                ["Long.Color.Background.Raised"] = SystemColors.WindowColor.ToString(),
                ["Long.Color.Surface.Card"] = SystemColors.WindowColor.ToString(),
                ["Long.Color.Surface.Hover"] = SystemColors.ControlColor.ToString(),
                ["Long.Color.Surface.Pressed"] = SystemColors.HighlightColor.ToString(),
                ["Long.Color.Surface.Overlay"] = SystemColors.WindowColor.ToString(),
                ["Long.Color.Stroke.Default"] = SystemColors.WindowTextColor.ToString(),
                ["Long.Color.Stroke.Strong"] = SystemColors.WindowTextColor.ToString(),
                ["Long.Color.Text.Primary"] = SystemColors.WindowTextColor.ToString(),
                ["Long.Color.Text.Secondary"] = SystemColors.WindowTextColor.ToString(),
                ["Long.Color.Text.Muted"] = SystemColors.GrayTextColor.ToString(),
                ["Long.Color.Accent.Primary"] = SystemColors.HighlightColor.ToString(),
                ["Long.Color.Accent.Hover"] = SystemColors.HighlightColor.ToString(),
                ["Long.Color.Accent.Pressed"] = SystemColors.HighlightColor.ToString(),
                ["Long.Color.Accent.Soft"] = SystemColors.ControlColor.ToString(),
                ["Long.Color.Focus"] = SystemColors.HighlightColor.ToString(),
                ["Long.Color.State.Success"] = SystemColors.WindowTextColor.ToString(),
                ["Long.Color.State.Warning"] = SystemColors.WindowTextColor.ToString(),
                ["Long.Color.State.Danger"] = SystemColors.WindowTextColor.ToString(),
                ["Long.Color.ScrollThumb"] = SystemColors.WindowTextColor.ToString(),
                ["Long.Color.ScrollThumbHover"] = SystemColors.HighlightColor.ToString(),
            };

        #endregion
    }
}
