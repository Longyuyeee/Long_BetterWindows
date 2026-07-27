using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            StartupPerformanceTrace? startupTrace = null,
            IReadOnlySet<string>? suppressedAutoStartPluginIds = null)
        {
            _i18n = i18n ?? ServicesInitializer.I18n;
            _startupTrace = startupTrace;
            _scanner = new PluginScanner(
                pluginsDirectory,
                () => _i18n.CurrentLanguage,
                _startupTrace is null
                    ? null
                    : stage => _startupTrace.Mark(stage),
                suppressedAutoStartPluginIds is null
                    ? null
                    : id => suppressedAutoStartPluginIds.Contains(id));
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
                await WriteCommandReportAsync(
                    request,
                    null,
                    AcceptedInputType.None,
                    PluginCommandResult.Failure("Requested command was not found."),
                    0,
                    2);
                return request.ExitAfterCommand ? 2 : null;
            }

            var inputType = !string.IsNullOrEmpty(request.CommandText)
                            && descriptor.Command.AcceptedInputs.Contains(AcceptedInputType.Text)
                ? AcceptedInputType.Text
                : AcceptedInputType.None;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            if (!string.IsNullOrWhiteSpace(request.QualityCommandReportPath))
                CapabilityUsageTracker.Instance.ClearStats(descriptor.PluginId);
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
            var exitCode = result.IsSuccess ? 0 : 3;

            Log.Information(
                "Startup command {CommandKey} completed: Success={Success}, ElapsedMs={ElapsedMs:F1}",
                request.CommandKey,
                result.IsSuccess,
                stopwatch.Elapsed.TotalMilliseconds);
            await WriteCommandReportAsync(
                request,
                descriptor,
                inputType,
                result,
                stopwatch.Elapsed.TotalMilliseconds,
                exitCode);
            return request.ExitAfterCommand ? exitCode : null;
        }

        private static async Task WriteCommandReportAsync(
            PluginRuntimeStartRequest request,
            CommandDescriptor? descriptor,
            AcceptedInputType inputType,
            PluginCommandResult result,
            double elapsedMilliseconds,
            int exitCode)
        {
            if (string.IsNullOrWhiteSpace(request.QualityCommandReportPath))
                return;

            var fullPath = Path.GetFullPath(request.QualityCommandReportPath);
            var directory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException(
                    "Quality command report path has no parent directory.");
            Directory.CreateDirectory(directory);

            var inputText = inputType == AcceptedInputType.Text
                ? request.CommandText ?? string.Empty
                : string.Empty;
            var outputs = result.Outputs.ToDictionary(
                item => item.Key,
                item => new QualityCommandOutput(
                    item.Value.Type.ToString().ToLowerInvariant(),
                    item.Value.Value),
                StringComparer.Ordinal);
            var usage = descriptor is null
                ? null
                : CapabilityUsageTracker.Instance.GetStatsSnapshot(descriptor.PluginId);
            var report = new QualityCommandExecutionReport(
                1,
                DateTimeOffset.UtcNow,
                request.CommandKey ?? string.Empty,
                descriptor?.PluginId,
                descriptor?.Command.Id,
                inputType.ToString().ToLowerInvariant(),
                inputText.Length,
                inputText.Length == 0
                    ? null
                    : Convert.ToHexString(
                        SHA256.HashData(Encoding.UTF8.GetBytes(inputText)))
                        .ToLowerInvariant(),
                result.IsSuccess,
                result.Message,
                result.KeepPaletteOpen,
                outputs,
                usage?.CapabilityCalls
                    ?? new Dictionary<string, int>(StringComparer.Ordinal),
                usage?.ApiMethodCalls
                    ?? new Dictionary<string, int>(StringComparer.Ordinal),
                Math.Round(elapsedMilliseconds, 3),
                exitCode);

            var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(
                    temporaryPath,
                    JsonSerializer.Serialize(
                        report,
                        new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporaryPath, fullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
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
        bool ExitAfterCommand,
        string? QualityCommandReportPath = null);

    internal sealed record PluginRuntimeStartResult(
        int LoadedPluginCount,
        int RecoveredTransactionCount,
        int InstalledPackageCount,
        int? ExitCode);

    internal sealed record QualityCommandExecutionReport(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("recorded_at")] DateTimeOffset RecordedAt,
        [property: JsonPropertyName("command_key")] string CommandKey,
        [property: JsonPropertyName("plugin_id")] string? PluginId,
        [property: JsonPropertyName("command_id")] string? CommandId,
        [property: JsonPropertyName("input_type")] string InputType,
        [property: JsonPropertyName("input_text_length")] int InputTextLength,
        [property: JsonPropertyName("input_text_sha256")] string? InputTextSha256,
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("keep_palette_open")] bool KeepPaletteOpen,
        [property: JsonPropertyName("outputs")]
            IReadOnlyDictionary<string, QualityCommandOutput> Outputs,
        [property: JsonPropertyName("capability_calls")]
            IReadOnlyDictionary<string, int> CapabilityCalls,
        [property: JsonPropertyName("api_method_calls")]
            IReadOnlyDictionary<string, int> ApiMethodCalls,
        [property: JsonPropertyName("elapsed_ms")] double ElapsedMilliseconds,
        [property: JsonPropertyName("exit_code")] int ExitCode);

    internal sealed record QualityCommandOutput(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("value")] string Value);
}
