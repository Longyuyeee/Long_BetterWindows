using System.Windows;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Services;

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
                return Failure("workflow.error.invalidId");

            var application = Application.Current;
            if (application is null)
                return Failure("workflow.error.applicationUnavailable");

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
                return Failure("workflow.error.mainWindowUnavailable");

            if (!mainWindow.IsVisible) mainWindow.Show();
            if (mainWindow.WindowState == WindowState.Minimized)
                mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();

            var error = await mainWindow.OpenWorkflowReviewAsync(
                workflowId,
                expectedStateFingerprint,
                cancellationToken);
            return error is null
                ? PluginCommandResult.Success(
                    ServicesInitializer.I18n.T(
                        "workflow.review.readyForApproval"))
                : PluginCommandResult.Failure(error);
        }

        private static PluginCommandResult Failure(string key)
            => PluginCommandResult.Failure(ServicesInitializer.I18n.T(key));
    }
}
