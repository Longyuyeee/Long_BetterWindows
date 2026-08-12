using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using Microsoft.Web.WebView2.Wpf;

namespace LongBetterWindows.Host.Services
{
    internal sealed class WorkspaceSwitchQualityProbe
    {
        private const int CycleCount = 12;
        private static readonly string[] PluginIds =
        [
            "com.long.base64",
            "com.long.devtoolkit",
            "com.long.clipboardhistory",
        ];
        private static readonly WorkspaceManagementPage[] ManagementPages =
        [
            WorkspaceManagementPage.Overview,
            WorkspaceManagementPage.Plugins,
            WorkspaceManagementPage.Market,
            WorkspaceManagementPage.Settings,
        ];

        private readonly Application _application;

        internal WorkspaceSwitchQualityProbe(Application application)
        {
            _application = application;
        }

        internal async Task RunAsync(
            MainWindow mainWindow,
            string reportPath)
        {
            var registry = HostProvider.Instance.PluginStore;
            foreach (var pluginId in PluginIds)
            {
                if (registry.Get(pluginId) is null)
                {
                    throw new InvalidOperationException(
                        $"Workspace switch plugin was not found: {pluginId}");
                }
            }

            mainWindow.Hide();
            var identities = new Dictionary<string, PluginRuntimeIdentity>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var pluginId in PluginIds)
            {
                await OpenPluginAsync(pluginId);
                var ready = await WaitForEmbeddedPluginAsync(
                    mainWindow,
                    pluginId);
                var state = mainWindow.GetPluginRuntimeQualityState();
                if (!ready || state.SessionId is null)
                {
                    throw new InvalidOperationException(
                        $"Workspace switch plugin did not become ready: {pluginId}");
                }
                identities[pluginId] = new PluginRuntimeIdentity(
                    state.SessionId,
                    state.ContentIdentity);
            }

            foreach (var page in ManagementPages)
                await mainWindow.OpenManagementPageForQualityAsync(page);

            var expectedCounts =
                mainWindow.GetWorkspaceModuleCountsForQuality();
            var samples = new List<ProcessResourceSample>
            {
                await CaptureSettledResourcesAsync(0, "warm"),
            };
            var cycles = new List<WorkspaceSwitchCycleResult>();
            for (var cycle = 1; cycle <= CycleCount; cycle++)
            {
                var managementPassed = true;
                foreach (var page in ManagementPages)
                {
                    await mainWindow.OpenManagementPageForQualityAsync(page);
                    managementPassed &=
                        !mainWindow.GetPluginRuntimeQualityState().IsVisible
                        && mainWindow.GetActiveWorkspaceModuleKeyForQuality()
                            .Kind != "plugin-runtime";
                }

                var pluginIdentityPassed = true;
                foreach (var pluginId in PluginIds)
                {
                    await OpenPluginAsync(pluginId);
                    var ready = await WaitForEmbeddedPluginAsync(
                        mainWindow,
                        pluginId);
                    var state = mainWindow.GetPluginRuntimeQualityState();
                    var expected = identities[pluginId];
                    pluginIdentityPassed &= ready
                        && state.SessionId == expected.SessionId
                        && state.ContentIdentity == expected.ContentIdentity;
                }

                var counts = mainWindow.GetWorkspaceModuleCountsForQuality();
                var moduleCountsPassed =
                    counts.Total == expectedCounts.Total
                    && counts.PluginRuntime == expectedCounts.PluginRuntime;
                samples.Add(await CaptureSettledResourcesAsync(
                    cycle,
                    "switched"));
                cycles.Add(new WorkspaceSwitchCycleResult(
                    cycle,
                    managementPassed,
                    pluginIdentityPassed,
                    moduleCountsPassed,
                    counts.Total,
                    counts.PluginRuntime));
            }

            var growth = EvaluateGrowth(samples.First(), samples.Last());
            var cleanupPassed = await CleanupPluginsAsync(
                mainWindow,
                registry);
            var switchingPassed = cycles.All(cycle =>
                cycle.ManagementPassed
                && cycle.PluginIdentityPassed
                && cycle.ModuleCountsPassed);
            var passed = switchingPassed && growth.Passed && cleanupPassed;
            await WriteReportAsync(
                reportPath,
                expectedCounts,
                identities,
                cycles,
                samples,
                growth,
                switchingPassed,
                cleanupPassed,
                passed);
            _application.Shutdown(passed ? 0 : 7);
        }

        private static async Task OpenPluginAsync(string pluginId)
        {
            var result = await PluginMainUiLauncher.OpenAsync(
                HostProvider.Instance.PluginStore,
                pluginId);
            if (result != PluginMainUiOpenStatus.Opened)
            {
                throw new InvalidOperationException(
                    $"Workspace switch plugin could not open: {pluginId} ({result})");
            }
        }

        private static Task<bool> WaitForEmbeddedPluginAsync(
            MainWindow mainWindow,
            string pluginId)
            => WaitUntilAsync(
                () =>
                {
                    var state = mainWindow.GetPluginRuntimeQualityState();
                    var active =
                        mainWindow.GetActiveWorkspaceModuleKeyForQuality();
                    return state.IsVisible
                        && !state.IsDetached
                        && state.SessionId is not null
                        && state.ContentIdentity != 0
                        && active.Kind == "plugin-runtime"
                        && string.Equals(
                            active.ResourceId,
                            pluginId,
                            StringComparison.OrdinalIgnoreCase)
                        && mainWindow.GetPluginRuntimeContentForQuality()
                            is WebView2 { CoreWebView2: not null };
                },
                15_000);

        private static async Task<bool> CleanupPluginsAsync(
            MainWindow mainWindow,
            PluginRegistry registry)
        {
            var passed = true;
            foreach (var pluginId in PluginIds)
            {
                await OpenPluginAsync(pluginId);
                var ready = await WaitForEmbeddedPluginAsync(
                    mainWindow,
                    pluginId);
                var state = mainWindow.GetPluginRuntimeQualityState();
                var endRequested = ready
                    && await mainWindow.EndPluginRuntimeForQualityAsync();
                var entry = registry.Get(pluginId)!;
                passed &= endRequested && await WaitUntilAsync(
                    () => entry.State == PluginState.Stopped
                        && (state.SessionId is null
                            || ServicesInitializer.PluginSessions
                                .GetBySessionId(state.SessionId) is null),
                    15_000);
            }
            return passed && mainWindow
                .GetWorkspaceModuleCountsForQuality().PluginRuntime == 0;
        }

        private async Task<ProcessResourceSample> CaptureSettledResourcesAsync(
            int cycle,
            string stage)
        {
            await _application.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ContextIdle);
            await Task.Delay(400);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            return new ProcessResourceSample(
                cycle,
                stage,
                DateTimeOffset.UtcNow,
                process.HandleCount,
                process.Threads.Count,
                process.PrivateMemorySize64,
                process.WorkingSet64,
                GC.GetTotalMemory(forceFullCollection: false));
        }

        private static WebViewLifecycleGrowthResult EvaluateGrowth(
            ProcessResourceSample warm,
            ProcessResourceSample final)
            => WebViewLifecycleGrowthPolicy.Evaluate(
                warm.HandleCount,
                final.HandleCount,
                warm.ThreadCount,
                final.ThreadCount,
                warm.PrivateMemoryBytes,
                final.PrivateMemoryBytes);

        private static async Task WriteReportAsync(
            string reportPath,
            (int Total, int PluginRuntime) expectedCounts,
            IReadOnlyDictionary<string, PluginRuntimeIdentity> identities,
            IReadOnlyList<WorkspaceSwitchCycleResult> cycles,
            IReadOnlyList<ProcessResourceSample> samples,
            WebViewLifecycleGrowthResult growth,
            bool switchingPassed,
            bool cleanupPassed,
            bool passed)
        {
            var fullPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(
                fullPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schema_version = 1,
                        captured_at = DateTimeOffset.UtcNow,
                        classification = "development_workspace_switch",
                        plugin_ids = PluginIds,
                        management_pages = ManagementPages
                            .Select(page => page.ToString())
                            .ToArray(),
                        cycle_count = CycleCount,
                        expected_module_count = expectedCounts.Total,
                        expected_plugin_runtime_module_count =
                            expectedCounts.PluginRuntime,
                        passed,
                        switching_passed = switchingPassed,
                        growth_passed = growth.Passed,
                        cleanup_passed = cleanupPassed,
                        growth = new
                        {
                            handle_count = growth.HandleCount,
                            thread_count = growth.ThreadCount,
                            private_memory_bytes = growth.PrivateMemoryBytes,
                        },
                        limits = new
                        {
                            handle_count =
                                WebViewLifecycleGrowthPolicy.MaximumHandleGrowth,
                            thread_count =
                                WebViewLifecycleGrowthPolicy.MaximumThreadGrowth,
                            private_memory_bytes = WebViewLifecycleGrowthPolicy
                                .MaximumPrivateMemoryGrowthBytes,
                        },
                        identities,
                        cycles,
                        samples,
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        private static async Task<bool> WaitUntilAsync(
            Func<bool> condition,
            int timeoutMilliseconds)
        {
            var deadline = Environment.TickCount64 + timeoutMilliseconds;
            while (Environment.TickCount64 < deadline)
            {
                if (condition())
                    return true;
                await Task.Delay(40);
            }
            return condition();
        }

        private sealed record ProcessResourceSample(
            int Cycle,
            string Stage,
            DateTimeOffset CapturedAt,
            int HandleCount,
            int ThreadCount,
            long PrivateMemoryBytes,
            long WorkingSetBytes,
            long ManagedMemoryBytes);

        private sealed record PluginRuntimeIdentity(
            string SessionId,
            int ContentIdentity);

        private sealed record WorkspaceSwitchCycleResult(
            int Cycle,
            bool ManagementPassed,
            bool PluginIdentityPassed,
            bool ModuleCountsPassed,
            int ModuleCount,
            int PluginRuntimeModuleCount);
    }
}
