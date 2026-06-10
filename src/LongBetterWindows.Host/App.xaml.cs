using System.IO;
using System.Windows;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;
using LongBetterWindows.Host.Views;
using Serilog;

namespace LongBetterWindows.Host
{
    public partial class App : Application
    {
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

            // 非 --note 模式：正常启动，扫描插件
            if (_directNotePath == null)
            {
                var scanner = new PluginScanner();
                _ = scanner.ScanAsync().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        Log.Error(t.Exception, "插件扫描失败");
                    else
                        Log.Information("插件扫描完成，加载 {Count} 个插件", scanner.LoadedPlugins.Count);
                });
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

                FloatingHudWindow.ShowAt(x, y, existingNote, async (text) =>
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
    }
}
