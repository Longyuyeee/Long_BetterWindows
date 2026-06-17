using System.IO;
using System.Windows;
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

            // 非 --note 模式：正常启动，安装 .lpak → 扫描插件
            if (_directNotePath == null)
            {
                var scanner = new PluginScanner();
                var installer = new LpakInstaller(scanner);

                _ = Task.Run(async () =>
                {
                    // 先安装 .lpak 包
                    var installed = await installer.InstallAllFromDirectoryAsync();
                    if (installed > 0)
                        Log.Information("安装了 {Count} 个 .lpak 插件", installed);

                    // 再扫描已解压的插件
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
                        var count = scanner.LoadedPlugins.Count;
                        Log.Information("插件加载完成，共 {Count} 个", count);
                    }
                }, TaskScheduler.Default);
            }
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
            catch { /* ignore corrupt config */ }
            return null;
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

        #endregion
    }
}
