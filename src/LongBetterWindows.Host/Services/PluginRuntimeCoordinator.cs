using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    internal sealed class PluginRuntimeCoordinator : IDisposable
    {
        private readonly PluginScanner _scanner;
        private readonly PluginRegistry _registry;
        private readonly I18nService _i18n;
        private readonly StartupPerformanceTrace? _startupTrace;
        private bool _disposed;

        public PluginRuntimeCoordinator(
            string? pluginsDirectory = null,
            PluginRegistry? registry = null,
            I18nService? i18n = null,
            StartupPerformanceTrace? startupTrace = null)
        {
            _i18n = i18n ?? ServicesInitializer.I18n;
            _startupTrace = startupTrace;
            _scanner = new PluginScanner(
                pluginsDirectory,
                () => _i18n.CurrentLanguage);
            _registry = registry ?? HostProvider.Instance.PluginStore;
            PackageInstaller = new LpakInstaller(_scanner, pluginsDirectory);
            _i18n.LanguageChanged += OnLanguageChanged;
            _startupTrace?.Mark("plugin_runtime_constructed");
        }

        public LpakInstaller PackageInstaller { get; }

        public async Task<PluginRuntimeStartResult> StartAsync(
            PluginRuntimeStartRequest request)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _startupTrace?.Mark("transaction_recovery_begin");
            var recovered = await PackageInstaller.RecoverInterruptedTransactionsAsync();
            _startupTrace?.Mark("transaction_recovery_end");
            if (recovered > 0)
                Log.Warning(
                    "Recovered {Count} interrupted plugin transactions during startup",
                    recovered);

            _startupTrace?.Mark("package_install_begin");
            var installed = await PackageInstaller.InstallAllFromDirectoryAsync();
            _startupTrace?.Mark("package_install_end");
            if (installed > 0)
                Log.Information("Installed {Count} .lpak plugins during startup", installed);

            _startupTrace?.Mark("plugin_scan_begin");
            await _scanner.ScanAsync();
            _startupTrace?.Mark("plugin_scan_end");
            Log.Information(
                "Plugin runtime started with {Count} loaded plugins",
                _scanner.LoadedPlugins.Count);

            var exitCode = await ExecuteRequestedCommandAsync(_registry, request);
            return new PluginRuntimeStartResult(
                _scanner.LoadedPlugins.Count,
                recovered,
                installed,
                exitCode);
        }

        internal static async Task<int?> ExecuteRequestedCommandAsync(
            PluginRegistry registry,
            PluginRuntimeStartRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CommandKey))
                return null;

            var descriptor = registry.Commands.Get(request.CommandKey);
            if (descriptor == null)
            {
                Log.Error(
                    "Requested startup command does not exist: {CommandKey}",
                    request.CommandKey);
                return request.ExitAfterCommand ? 2 : null;
            }

            var inputType = !string.IsNullOrEmpty(request.CommandText)
                            && descriptor.Command.AcceptedInputs.Contains(AcceptedInputType.Text)
                ? AcceptedInputType.Text
                : AcceptedInputType.None;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Log.Information(
                "Executing requested startup command: {CommandKey}",
                request.CommandKey);
            var result = await new CommandExecutor(registry).ExecuteAsync(
                descriptor.Key,
                new PluginCommandInvocation
                {
                    CommandId = descriptor.Command.Id,
                    InputType = inputType,
                    Text = inputType == AcceptedInputType.Text ? request.CommandText : null,
                });
            stopwatch.Stop();

            Log.Information(
                "Startup command {CommandKey} completed: Success={Success}, ElapsedMs={ElapsedMs:F1}",
                request.CommandKey,
                result.IsSuccess,
                stopwatch.Elapsed.TotalMilliseconds);
            return request.ExitAfterCommand ? result.IsSuccess ? 0 : 3 : null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _i18n.LanguageChanged -= OnLanguageChanged;
            _scanner.Dispose();
        }

        private void OnLanguageChanged(string language)
            => _ = NotifyLanguageChangedAsync(language);

        private async Task NotifyLanguageChangedAsync(string language)
        {
            try
            {
                if (!_disposed)
                    await _scanner.NotifyLanguageChangedAsync(language);
            }
            catch (ObjectDisposedException) when (_disposed)
            {
            }
            catch (Exception exception)
            {
                Log.Warning(
                    exception,
                    "Plugin language broadcast failed: {Language}",
                    language);
            }
        }
    }

    internal sealed record PluginRuntimeStartRequest(
        string? CommandKey,
        string? CommandText,
        bool ExitAfterCommand);

    internal sealed record PluginRuntimeStartResult(
        int LoadedPluginCount,
        int RecoveredTransactionCount,
        int InstalledPackageCount,
        int? ExitCode);
}
