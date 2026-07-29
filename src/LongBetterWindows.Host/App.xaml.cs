using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;
using LongBetterWindows.Host.Views;
using Serilog;
using Wpf.Ui.Appearance;

namespace LongBetterWindows.Host
{
    public partial class App : Application
    {
        internal static bool KeepPaletteVisibleForQuality
            => Current is App { _startupOptions.OpenPaletteForQuality: true };
        internal static bool KeepSuperPanelVisibleForQuality
            => Current is App { _startupOptions.OpenSuperPanelForQuality: true };
        internal static LpakInstaller? PackageInstaller
            => (Current as App)?._pluginRuntime?.PackageInstaller;

        public static event Action<bool>? ThemeChanged;
        public static bool IsExiting { get; set; }
        public static string ProductVersion
            => typeof(App).Assembly
                   .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                   .InformationalVersion.Split('+')[0]
               ?? typeof(App).Assembly.GetName().Version?.ToString()
               ?? "0.0.0";
        private AppStartupOptions _startupOptions = new();
        private QualityRuntimeService? _qualityRuntime;
        private StartupPerformanceTrace? _startupTrace;
        private PluginPagePerformanceTrace? _pluginPageTrace;
        private int _qualityManagementCardShadowCount;
        private int _pluginRuntimeStarted;
        private PluginRuntimeCoordinator? _pluginRuntime;
        private static bool _currentIsLight;
        private static bool _forceHighContrast;
        private static bool _forceReduceMotion;
        internal bool ShowDesignSystemPreviewRequested => _startupOptions.ShowDesignSystemPreview;
        internal bool ShowMarketForQualityRequested => _startupOptions.OpenMarketForQuality;
        internal bool ShowDiagnosticsForQualityRequested => _startupOptions.OpenDiagnosticsForQuality;
        internal bool ShowPluginsForQualityRequested => _startupOptions.OpenPluginsForQuality;
        internal string? PluginSettingsForQualityRequested
            => _startupOptions.QualityPluginSettingsId;
        internal bool ShowSystemForQualityRequested => _startupOptions.OpenSystemForQuality;
        internal bool ShowSettingsForQualityRequested => _startupOptions.OpenSettingsForQuality;
        internal bool ShowDeveloperForQualityRequested => _startupOptions.OpenDeveloperForQuality;
        internal bool ShowWelcomeForQualityRequested => _startupOptions.ShowWelcomeForQuality;
        internal static bool ShowManagementCardShadowsForQuality
            => Current is App { _startupOptions.QualityManagementCardShadows: true };
        internal static bool IsPluginPagePerformanceTracing
            => Current is App { _pluginPageTrace: not null };
        internal static bool IsLightTheme => _currentIsLight;
        internal bool QualityWorkflowAutomationEnabled
            => !string.IsNullOrWhiteSpace(_startupOptions.QualityWorkflowReviewId)
                || !string.IsNullOrWhiteSpace(_startupOptions.QualityWorkflowsDirectory);
        internal string? QualityWorkflowUpgradePackagePath
            => _startupOptions.QualityWorkflowUpgradePackagePath;
        internal string? QualityTerminalExportDirectory
            => _startupOptions.QualityTerminalExportDirectory;
        internal string? QualityWorkflowsDirectory
            => _startupOptions.QualityWorkflowsDirectory;
        internal string? QualityMarketplaceCatalogPath => _startupOptions.MarketplaceCatalogPath;
        internal string? QualityMarketplaceTrustStorePath => _startupOptions.MarketplaceTrustStorePath;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            Log.Information("Long助手 正在启动...");

            _startupOptions = AppStartupOptions.Parse(e.Args);
            if (!string.IsNullOrWhiteSpace(
                    _startupOptions.QualityPluginPagePerformanceReportPath))
            {
                _pluginPageTrace = new PluginPagePerformanceTrace(
                    _startupOptions.QualityPluginPagePerformanceReportPath,
                    _startupOptions.QualitySkippedAutoStartPluginIds);
                _pluginPageTrace.Mark("quality_options_parsed");
            }
            if (!string.IsNullOrWhiteSpace(_startupOptions.QualityStartupReportPath))
            {
                _startupTrace = new StartupPerformanceTrace(
                    _startupOptions.QualityStartupReportPath);
                _startupTrace.Mark("options_parsed");
            }
            _startupTrace?.Mark("services_initialize_begin");
            ServicesInitializer.Initialize(_startupOptions.QualityWorkflowsDirectory);
            _startupTrace?.Mark("services_initialize_end");
            ServicesInitializer.I18n.Initialize(_startupOptions.LanguageOverride);
            ServicesInitializer.InitializeWorkspace();
            ServicesInitializer.I18n.ApplyTo(Resources);
            _startupTrace?.Mark("localization_applied");
            Log.Information("所有服务已初始化。");

            _qualityRuntime = new QualityRuntimeService(this);
            _forceHighContrast = _startupOptions.ForceHighContrast;
            _forceReduceMotion = _startupOptions.ForceReduceMotion;
            Log.Information(
                "Quality accessibility mode: HighContrast={HighContrast}, ReducedMotion={ReducedMotion}",
                SystemParameters.HighContrast || _forceHighContrast,
                !SystemParameters.ClientAreaAnimation || _forceReduceMotion);

            // 命令行主题覆盖仅对本次进程生效，供视觉回归与诊断使用。
            var themeOverride = _startupOptions.ThemeOverride;
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
            _startupTrace?.Mark("theme_applied");

            // 检查命令行 --note 参数（右键菜单触发）
            if (_startupOptions.ShowDesignSystemPreview)
                Log.Information("已请求 Long Design System 预览窗口");

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                Log.Fatal(args.ExceptionObject as Exception, "未处理的 AppDomain 异常");
            };

            DispatcherUnhandledException += (sender, args) =>
            {
                Log.Error(args.Exception, "未处理的 UI 线程异常");
                args.Handled = true;
            };
            _startupTrace?.Mark("app_startup_end");

        }

        internal static void MarkStartupStage(string stage)
            => (Current as App)?._startupTrace?.Mark(stage);

        internal static void MarkPluginPageStage(
            string stage,
            PluginPageVisualMetrics? visualMetrics = null)
            => (Current as App)?._pluginPageTrace?.Mark(stage, visualMetrics);

        internal static void RecordManagementCardShadowCount(int count)
        {
            if (Current is App app)
                app._qualityManagementCardShadowCount = count;
        }

        internal void StartPluginRuntime()
        {
            if (_startupOptions.IsDirectNoteMode ||
                Interlocked.Exchange(ref _pluginRuntimeStarted, 1) != 0)
                return;

            _startupTrace?.Mark("plugin_runtime_scheduled");
            // Wait until the main window handle and hotkey service are ready.
            Dispatcher.BeginInvoke(
                new Action(() => _ = RunPluginStartupAsync()),
                DispatcherPriority.ApplicationIdle);
        }

        private async Task RunPluginStartupAsync()
        {
            try
            {
                _startupTrace?.Mark("plugin_runtime_begin");
                _pluginRuntime = new PluginRuntimeCoordinator(
                    _startupOptions.RequestedPluginsDirectory,
                    startupTrace: _startupTrace,
                    suppressedAutoStartPluginIds:
                        _startupOptions.QualitySkippedAutoStartPluginIds.Count > 0
                            ? _startupOptions.QualitySkippedAutoStartPluginIds
                            : null);
                var runtimeResult = await _pluginRuntime.StartAsync(
                    new PluginRuntimeStartRequest(
                        _startupOptions.RequestedCommandKey,
                        _startupOptions.RequestedCommandText,
                        _startupOptions.ExitAfterCommand,
                        _startupOptions.QualityCommandReportPath,
                        _startupOptions.QualityCommandFixturePath,
                        _startupOptions.RequestedCommandPaths));
                _startupTrace?.Mark("plugin_runtime_ready");
                if (runtimeResult.ExitCode is int exitCode)
                {
                    Shutdown(exitCode);
                    return;
                }

                if (MainWindow is Window qualityWindow
                    && (_startupOptions.QualityCaptureWidth > 0
                        || _startupOptions.QualityCaptureHeight > 0))
                {
                    qualityWindow.WindowState = WindowState.Normal;
                    if (_startupOptions.QualityCaptureWidth > 0)
                        qualityWindow.Width = _startupOptions.QualityCaptureWidth;
                    if (_startupOptions.QualityCaptureHeight > 0)
                        qualityWindow.Height = _startupOptions.QualityCaptureHeight;
                    qualityWindow.UpdateLayout();
                    await Dispatcher.InvokeAsync(
                        () => { },
                        DispatcherPriority.Render);
                }

                if (_startupOptions.OpenPaletteForQuality)
                    CommandPaletteWindow.ShowPalette();
                if (_startupOptions.OpenSuperPanelForQuality)
                {
                    if (_startupOptions.UseLiveContextForQuality)
                        SuperPanelWindow.ShowPanel();
                    else
                        SuperPanelWindow.ShowPanelForQuality(
                            _startupOptions.UseEmptyContextForQuality);
                }
                if (!string.IsNullOrWhiteSpace(
                        _startupOptions.QualityPluginSettingsId))
                {
                    if (MainWindow is not MainWindow pluginSettingsWindow
                        || !await pluginSettingsWindow.OpenPluginSettingsForQualityAsync(
                            _startupOptions.QualityPluginSettingsId))
                    {
                        throw new InvalidOperationException(
                            "Quality plugin settings module could not open.");
                    }
                    await Dispatcher.InvokeAsync(
                        () => { },
                        DispatcherPriority.Loaded);
                }
                if (!string.IsNullOrWhiteSpace(_startupOptions.QualityWorkflowReviewId)
                    && MainWindow is MainWindow workflowWindow)
                {
                    var workflowId = _startupOptions.QualityWorkflowReviewId;
                    _ = Dispatcher.BeginInvoke(
                        new Action(() => _ = OpenQualityWorkflowReviewAsync(
                            workflowWindow,
                            workflowId)),
                        DispatcherPriority.ApplicationIdle);
                }
                else if (!string.IsNullOrWhiteSpace(_startupOptions.QualityWorkflowEditorId)
                    && MainWindow is MainWindow workflowEditorWindow)
                {
                    var workflowId = _startupOptions.QualityWorkflowEditorId;
                    _ = Dispatcher.BeginInvoke(
                        new Action(() => _ = OpenQualityWorkflowEditorAsync(
                            workflowEditorWindow,
                            workflowId)),
                        DispatcherPriority.ApplicationIdle);
                }

                if (!string.IsNullOrWhiteSpace(
                        _startupOptions.QualityPluginPagePerformanceReportPath))
                {
                    if (MainWindow is not MainWindow performanceProbeWindow
                        || _pluginPageTrace is null)
                    {
                        throw new InvalidOperationException(
                            "Plugin page performance probe requires the main window.");
                    }
                    var idleMilliseconds =
                        _startupOptions.QualityIdleMilliseconds > 0
                            ? _startupOptions.QualityIdleMilliseconds
                            : 9_000;
                    await _qualityRuntime!.RunPluginPagePerformanceProbeAsync(
                        performanceProbeWindow,
                        _pluginPageTrace,
                        runtimeResult,
                        idleMilliseconds,
                        _startupOptions.QualityHideWindowDuringIdle);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(
                        _startupOptions.QualityPluginPageReleaseReportPath))
                {
                    if (MainWindow is not MainWindow releaseProbeWindow)
                        throw new InvalidOperationException(
                            "Plugin page release probe requires the main window.");
                    await _qualityRuntime!.RunPluginPageReleaseProbeAsync(
                        releaseProbeWindow,
                        _startupOptions.QualityPluginPageReleaseReportPath);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(
                        _startupOptions.QualityPluginRuntimeSessionReportPath))
                {
                    if (MainWindow is not MainWindow runtimeSessionWindow)
                    {
                        throw new InvalidOperationException(
                            "Plugin runtime session probe requires the main window.");
                    }
                    await _qualityRuntime!.RunPluginRuntimeSessionProbeAsync(
                        runtimeSessionWindow,
                        _startupOptions.QualityPluginRuntimeSessionReportPath);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(
                        _startupOptions.QualityTaskbarIdentityReportPath))
                {
                    await _qualityRuntime!.RunTaskbarIdentityProbeAsync(
                        _startupOptions.QualityTaskbarIdentityReportPath);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(
                        _startupOptions.QualityUiServiceThemeReportPath))
                {
                    await _qualityRuntime!.RunUiServiceThemeProbeAsync(
                        _startupOptions.QualityUiServiceThemeReportPath);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(
                        _startupOptions.QualityThemedMessageDialogReportPath))
                {
                    await _qualityRuntime!.RunThemedMessageDialogProbeAsync(
                        _startupOptions.QualityThemedMessageDialogReportPath);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(
                        _startupOptions.QualityPluginSettingsReportPath))
                {
                    await _qualityRuntime!.RunPluginSettingsProbeAsync(
                        _startupOptions.QualityPluginSettingsReportPath);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(
                        _startupOptions.QualityPluginSettingsPersistenceReportPath))
                {
                    await _qualityRuntime!.RunPluginSettingsPersistenceProbeAsync(
                        _startupOptions.QualityPluginSettingsPersistenceReportPath);
                    return;
                }

                if (_startupTrace is not null)
                {
                    await _startupTrace.WriteAsync(
                        runtimeResult,
                        _startupOptions.QualityManagementCardShadows,
                        _qualityManagementCardShadowCount);
                    Shutdown(0);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(_startupOptions.QualityCapturePath))
                    await _qualityRuntime!.CaptureAsync(
                        _startupOptions,
                        _currentIsLight,
                        SystemParameters.HighContrast || _forceHighContrast,
                        !SystemParameters.ClientAreaAnimation || _forceReduceMotion);

                if (_startupOptions.QualityIdleMilliseconds > 0)
                    await _qualityRuntime!.RunIdleProbeAsync(
                        _startupOptions.QualityIdleMilliseconds,
                        runtimeResult.LoadedPluginCount,
                        HostProvider.Instance.PluginStore.Commands.Count,
                        SystemParameters.HighContrast || _forceHighContrast,
                        !SystemParameters.ClientAreaAnimation || _forceReduceMotion);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "插件加载失败");
                if (!string.IsNullOrWhiteSpace(_startupOptions.QualityCapturePath)
                    || !string.IsNullOrWhiteSpace(
                        _startupOptions.QualityPluginPageReleaseReportPath)
                    || !string.IsNullOrWhiteSpace(
                        _startupOptions.QualityPluginPagePerformanceReportPath)
                    || !string.IsNullOrWhiteSpace(
                        _startupOptions.QualityPluginRuntimeSessionReportPath)
                    || !string.IsNullOrWhiteSpace(
                        _startupOptions.QualityTaskbarIdentityReportPath)
                    || !string.IsNullOrWhiteSpace(
                        _startupOptions.QualityUiServiceThemeReportPath)
                    || !string.IsNullOrWhiteSpace(
                        _startupOptions.QualityThemedMessageDialogReportPath)
                    || !string.IsNullOrWhiteSpace(
                        _startupOptions.QualityPluginSettingsReportPath)
                    || !string.IsNullOrWhiteSpace(
                        _startupOptions.QualityPluginSettingsPersistenceReportPath)
                    || !string.IsNullOrWhiteSpace(
                        _startupOptions.QualityStartupReportPath)
                    || !string.IsNullOrWhiteSpace(
                        _startupOptions.QualityCommandReportPath))
                {
                    Shutdown(3);
                    return;
                }
                await ServicesInitializer.Notification.ShowAsync(
                    ServicesInitializer.I18n.T("pluginLoad.error.title"),
                    ServicesInitializer.I18n.T("pluginLoad.error.message"));
            }
        }

        private static async Task OpenQualityWorkflowReviewAsync(
            MainWindow workflowWindow,
            string workflowId)
        {
            Log.Information("Opening quality workflow review: {WorkflowId}", workflowId);
            var error = await workflowWindow.OpenWorkflowReviewAsync(workflowId);
            if (error is null)
            {
                Log.Information("Quality workflow review opened: {WorkflowId}", workflowId);
                return;
            }

            Log.Error(
                "Quality workflow review could not be opened: {WorkflowId}; {Error}",
                workflowId,
                error);
        }

        private static async Task OpenQualityWorkflowEditorAsync(
            MainWindow workflowWindow,
            string workflowId)
        {
            Log.Information("Opening quality workflow editor: {WorkflowId}", workflowId);
            var error = await workflowWindow.OpenWorkflowEditorAsync(workflowId);
            if (error is null)
            {
                Log.Information("Quality workflow editor opened: {WorkflowId}", workflowId);
                return;
            }

            Log.Error(
                "Quality workflow editor could not be opened: {WorkflowId}; {Error}",
                workflowId,
                error);
        }

        protected override async void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            if (_startupOptions.DirectNotePath != null)
            {
                var path = _startupOptions.DirectNotePath!;

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
                FloatingHudWindow.ShowToast(
                    ServicesInitializer.I18n.T("folderNote.error.folderMissing"));
                return;
            }

            var noteResult = await ServicesInitializer.ADS
                .ReadAsync(folderPath, "long_note");

            string? existingNote = null;
            if (noteResult.IsSuccess && noteResult.Data != null)
            {
                existingNote = noteResult.Data;
            }
            else if (noteResult.ErrorCode != ApiErrorCode.StreamNotFound)
            {
                Log.Warning(
                    "右键备注读取失败: {Path}, {Error}",
                    folderPath,
                    noteResult.ErrorMessage);
                FloatingHudWindow.ShowToast(
                    ServicesInitializer.I18n.T(
                        "folderNote.error.loadFailed"));
                return;
            }

            Dispatcher.Invoke(() =>
            {
                var area = SystemParameters.WorkArea;
                double x = area.Left + (area.Width - 320) / 2;
                double y = area.Top + (area.Height - 150) / 2;

                FloatingHudWindow.ShowAt(
                    x,
                    y,
                    existingNote,
                    folderPath,
                    async (text) =>
                    {
                        var result = string.IsNullOrEmpty(text)
                            ? await ServicesInitializer.ADS.DeleteAsync(
                                folderPath,
                                "long_note")
                            : await ServicesInitializer.ADS.WriteAsync(
                                folderPath,
                                "long_note",
                                text);
                        if (!result.IsSuccess)
                        {
                            Log.Warning(
                                "右键备注保存失败: {Path}, {Error}",
                                folderPath,
                                result.ErrorMessage);
                            throw new InvalidOperationException(
                                ServicesInitializer.I18n.T(
                                    "folderNote.error.saveFailed"));
                        }

                        Log.Information("右键备注已保存: {Path}", folderPath);
                    },
                    new FloatingHudLocalization(
                        ServicesInitializer.I18n.T("folderNote.hud.title"),
                        ServicesInitializer.I18n.T(
                            "folderNote.hud.inputAutomationName"),
                        ServicesInitializer.I18n.T("folderNote.hud.emptyHint"),
                        ServicesInitializer.I18n.T(
                            "folderNote.hud.modifiedHint")));
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
            HostProvider.Instance.PluginStore.ShutdownAllAsync().GetAwaiter().GetResult();
            _pluginRuntime?.Dispose();
            // 清理服务资源
            ServicesInitializer.DisposeAll();
            Log.Information("Long助手 已退出。");
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
            UpdateThemeBrushResources(r, palette);
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

        private static void UpdateThemeBrushResources(
            ResourceDictionary resources,
            IReadOnlyDictionary<string, string> palette)
        {
            var brushColors = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Long.Brush.Background.Base"] = "Long.Color.Background.Base",
                ["Long.Brush.Background.Raised"] = "Long.Color.Background.Raised",
                ["Long.Brush.Surface.Card"] = "Long.Color.Surface.Card",
                ["Long.Brush.Surface.Hover"] = "Long.Color.Surface.Hover",
                ["Long.Brush.Surface.Pressed"] = "Long.Color.Surface.Pressed",
                ["Long.Brush.Surface.Overlay"] = "Long.Color.Surface.Overlay",
                ["Long.Brush.Stroke.Default"] = "Long.Color.Stroke.Default",
                ["Long.Brush.Stroke.Strong"] = "Long.Color.Stroke.Strong",
                ["Long.Brush.Text.Primary"] = "Long.Color.Text.Primary",
                ["Long.Brush.Text.Secondary"] = "Long.Color.Text.Secondary",
                ["Long.Brush.Text.Muted"] = "Long.Color.Text.Muted",
                ["Long.Brush.Accent.Primary"] = "Long.Color.Accent.Primary",
                ["Long.Brush.Accent.Hover"] = "Long.Color.Accent.Hover",
                ["Long.Brush.Accent.Pressed"] = "Long.Color.Accent.Pressed",
                ["Long.Brush.Accent.Soft"] = "Long.Color.Accent.Soft",
                ["Long.Brush.Focus"] = "Long.Color.Focus",
                ["Long.Brush.State.Success"] = "Long.Color.State.Success",
                ["Long.Brush.State.Warning"] = "Long.Color.State.Warning",
                ["Long.Brush.State.Danger"] = "Long.Color.State.Danger",
                ["Long.Brush.ScrollThumb"] = "Long.Color.ScrollThumb",
                ["Long.Brush.ScrollThumbHover"] = "Long.Color.ScrollThumbHover",
                ["AccentBlueBrush"] = "Long.Color.Accent.Primary",
                ["AccentBlueHoverBrush"] = "Long.Color.Accent.Hover",
                ["AccentBluePressedBrush"] = "Long.Color.Accent.Pressed",
                ["SuccessGreenBrush"] = "Long.Color.State.Success",
                ["SuccessGreenHoverBrush"] = "Long.Color.State.Success",
                ["DangerRedBrush"] = "Long.Color.State.Danger",
                ["DangerRedHoverBrush"] = "Long.Color.State.Danger",
                ["SurfaceBackgroundBrush"] = "Long.Color.Background.Base",
                ["CardBackgroundBrush"] = "Long.Color.Surface.Card",
                ["CardHoverBrush"] = "Long.Color.Surface.Hover",
                ["TitleBarBrush"] = "Long.Color.Background.Raised",
                ["SecondaryButtonBrush"] = "Long.Color.Surface.Card",
                ["SecondaryButtonHoverBrush"] = "Long.Color.Surface.Hover",
                ["TextPrimaryBrush"] = "Long.Color.Text.Primary",
                ["TextSecondaryBrush"] = "Long.Color.Text.Secondary",
                ["TextMutedBrush"] = "Long.Color.Text.Muted",
                ["TextDimBrush"] = "Long.Color.Text.Muted",
                ["DividerBrush"] = "Long.Color.Stroke.Default",
                ["ScrollThumbBrush"] = "Long.Color.ScrollThumb",
                ["ScrollThumbHoverBrush"] = "Long.Color.ScrollThumbHover",
            };

            foreach (var (brushKey, colorKey) in brushColors)
            {
                var brush = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(palette[colorKey]));
                brush.Freeze();
                resources[brushKey] = brush;
            }
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
