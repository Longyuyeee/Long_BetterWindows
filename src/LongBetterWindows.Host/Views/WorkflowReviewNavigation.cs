using System.Windows;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Views
{
    internal static class WorkflowReviewNavigation
    {
        public static async Task<PluginCommandResult> OpenAsync(
            string workflowId,
            string? expectedStateFingerprint,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(workflowId))
                return PluginCommandResult.Failure("组合动作标识无效。");

            var application = Application.Current;
            if (application is null)
                return PluginCommandResult.Failure("应用窗口当前不可用。");

            if (!application.Dispatcher.CheckAccess())
            {
                return await application.Dispatcher.InvokeAsync(
                    () => OpenAsync(
                        workflowId,
                        expectedStateFingerprint,
                        cancellationToken)).Task.Unwrap();
            }

            cancellationToken.ThrowIfCancellationRequested();
            var mainWindow = application.Windows.OfType<MainWindow>().FirstOrDefault()
                ?? application.MainWindow as MainWindow;
            if (mainWindow is null)
                return PluginCommandResult.Failure("主窗口当前不可用。");

            if (!mainWindow.IsVisible) mainWindow.Show();
            if (mainWindow.WindowState == WindowState.Minimized)
                mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();

            var error = await mainWindow.OpenWorkflowReviewAsync(
                workflowId,
                expectedStateFingerprint,
                cancellationToken);
            return error is null
                ? PluginCommandResult.Success("请审查权限后确认运行。")
                : PluginCommandResult.Failure(error);
        }
    }
}
