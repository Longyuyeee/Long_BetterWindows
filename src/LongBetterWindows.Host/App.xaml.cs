using System.IO;
using System.Windows;
using System.Windows.Media;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;
using LongBetterWindows.Host.Views;
using Serilog;
using Wpf.Ui.Appearance;

namespace LongBetterWindows.Host
{
    public partial class App : Application
    {
        public static bool IsExiting { get; set; }
        private string? _directNotePath;
        private bool _isDirectNoteMode;
        private int _pluginRuntimeStarted;
        private PluginScanner? _scanner;

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

            // 优先使用用户保存的主题偏好，否则跟随系统
            var themeSetting = ReadThemeSetting();
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

            // 检查命令行 --note 参数（右键菜单触发）
            for (int i = 0; i < e.Args.Length; i++)
            {
                if (e.Args[i] == "--note" && i + 1 < e.Args.Length)
                {
                    _directNotePath = e.Args[i + 1];
                    _isDirectNoteMode = true;
                    break;
                }
            }

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

            _scanner = new PluginScanner();
            var scanner = _scanner;
            var installer = new LpakInstaller(scanner);

            _ = Task.Run(async () =>
            {
                // 先安装 .lpak 包
                var installed = await installer.InstallAllFromDirectoryAsync();
                if (installed > 0)
                    Log.Information("安装了 {Count} 个 .lpak 插件", installed);

                // 主窗口句柄与热键服务就绪后再扫描和启动插件。
                await scanner.ScanAsync();
                Log.Information("插件加载完成，共 {Count} 个", scanner.LoadedPlugins.Count);
            }).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Log.Error(t.Exception, "插件加载失败");
                    Dispatcher.Invoke(async () =>
                        await ServicesInitializer.Notification.ShowAsync("插件加载出错", "部分插件未能正确加载，请查看日志。"));
                }
                else
                {
                    Log.Information("插件加载完成，共 {Count} 个", scanner.LoadedPlugins.Count);
                }
            }, TaskScheduler.Default);
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
            // 清理服务资源
            _scanner?.Dispose();
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
            var r = Current.Resources;
            var c = (Color c) => new SolidColorBrush(c);

            if (isLight)
            {
                r["SurfaceBackgroundBrush"] = r["LightSurfaceBrush"];
                r["CardBackgroundBrush"] = r["LightCardBrush"];
                r["TextPrimaryBrush"] = r["LightTextPrimaryBrush"];
                r["TextSecondaryBrush"] = r["LightTextSecondaryBrush"];
                r["TextMutedBrush"] = r["LightTextSecondaryBrush"];
                r["TextDimBrush"] = c(Color.FromRgb(0x99, 0x99, 0x99));
                r["SecondaryButtonBrush"] = c(Color.FromRgb(0xE8, 0xE8, 0xEB));
                r["SecondaryButtonHoverBrush"] = c(Color.FromRgb(0xD8, 0xD8, 0xDB));
                r["DividerBrush"] = c(Color.FromRgb(0xDD, 0xDD, 0xDD));
                r["TitleBarBrush"] = c(Color.FromRgb(0xF0, 0xF0, 0xF2));
                r["ScrollThumbBrush"] = c(Color.FromRgb(0xBB, 0xBB, 0xBB));
                r["ScrollThumbHoverBrush"] = c(Color.FromRgb(0x99, 0x99, 0x99));
            }
            else
            {
                r["SurfaceBackgroundBrush"] = c(Color.FromRgb(0x1E, 0x1F, 0x22));
                r["CardBackgroundBrush"] = c(Color.FromRgb(0x2D, 0x2D, 0x30));
                r["TextPrimaryBrush"] = c(Color.FromRgb(0xE8, 0xE8, 0xE8));
                r["TextSecondaryBrush"] = c(Color.FromRgb(0x99, 0x99, 0x99));
                r["TextMutedBrush"] = c(Color.FromRgb(0x88, 0x88, 0x88));
                r["TextDimBrush"] = c(Color.FromRgb(0x77, 0x77, 0x77));
                r["SecondaryButtonBrush"] = c(Color.FromRgb(0x3A, 0x3A, 0x3D));
                r["SecondaryButtonHoverBrush"] = c(Color.FromRgb(0x4A, 0x4A, 0x4D));
                r["DividerBrush"] = c(Color.FromRgb(0x3A, 0x3A, 0x3D));
                r["TitleBarBrush"] = c(Color.FromRgb(0x2D, 0x2D, 0x30));
                r["ScrollThumbBrush"] = c(Color.FromRgb(0x55, 0x55, 0x55));
                r["ScrollThumbHoverBrush"] = c(Color.FromRgb(0x77, 0x77, 0x77));
            }
        }

        #endregion
    }
}
