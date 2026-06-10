using System.Windows;
using LongBetterWindows.Host.Services;
using Serilog;

namespace LongBetterWindows.Host
{
    public partial class App : Application
    {
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

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Long窗口·全能助手 已退出。");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
