using System.Windows;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views
{
    internal static class WorkspaceModuleNavigation
    {
        public static async Task<PluginCommandResult> OpenAsync(
            string target,
            CancellationToken cancellationToken = default)
        {
            if (!WorkspaceModuleAddress.TryParse(target, out var address))
            {
                return Failure(
                    "search.error.workspaceAddressInvalid",
                    "工作区模块地址无效。");
            }

            var application = Application.Current;
            if (application is null)
            {
                return Failure(
                    "search.error.applicationUnavailable",
                    "应用程序当前不可用。");
            }

            if (!application.Dispatcher.CheckAccess())
            {
                return await application.Dispatcher.InvokeAsync(
                    () => OpenAsync(address.CanonicalValue, cancellationToken))
                    .Task.Unwrap();
            }

            cancellationToken.ThrowIfCancellationRequested();
            var resolution = await ServicesInitializer.WorkspaceModules.ResolveAsync(
                address,
                cancellationToken);
            if (!resolution.IsSuccess || resolution.Module is null)
            {
                return resolution.Error == WorkspaceModuleResolutionError.ResourceUnsupported
                    ? Failure(
                        "search.error.workspaceResourceUnsupported",
                        "该资源不支持工作区模块。")
                    : Failure(
                        "search.error.workspaceResourceMissing",
                        "工作区资源不存在或已失效。");
            }

            var mainWindow = application.Windows.OfType<MainWindow>().FirstOrDefault()
                ?? application.MainWindow as MainWindow;
            if (mainWindow is null)
            {
                return Failure(
                    "search.error.mainWindowUnavailable",
                    "主窗口当前不可用。");
            }

            if (!mainWindow.IsVisible)
                mainWindow.Show();
            if (mainWindow.WindowState == WindowState.Minimized)
                mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();

            var error = await mainWindow.OpenWorkspaceModuleAsync(
                resolution.Module,
                cancellationToken);
            return error is null
                ? PluginCommandResult.Success(
                    Text("search.result.completed", "操作已完成。"))
                : PluginCommandResult.Failure(error);
        }

        private static PluginCommandResult Failure(string key, string fallback)
            => PluginCommandResult.Failure(Text(key, fallback));

        private static string Text(string key, string fallback)
        {
            var value = ServicesInitializer.I18n.T(key);
            return string.IsNullOrWhiteSpace(value) || value == key
                ? fallback
                : value;
        }
    }
}
