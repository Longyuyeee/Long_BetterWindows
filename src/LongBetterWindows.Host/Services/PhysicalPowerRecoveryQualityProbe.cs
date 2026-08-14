using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using Microsoft.Web.WebView2.Wpf;

namespace LongBetterWindows.Host.Services;

internal sealed class PhysicalPowerRecoveryQualityProbe
{
    private const string PluginId = "com.long.hardwaremonitor";
    private readonly Application _application;

    internal PhysicalPowerRecoveryQualityProbe(Application application)
        => _application = application;

    internal async Task RunAsync(MainWindow mainWindow, string reportPath)
    {
        var environment = HostEnvironmentStateService.Current;
        var suspendObserved = new TaskCompletionSource<PowerObservation>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeObserved = new TaskCompletionSource<PowerObservation>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var suspendSeen = false;
        void PowerTransitionObserved(HostPowerTransition transition)
        {
            var observation = new PowerObservation(
                DateTimeOffset.UtcNow,
                transition,
                environment.IsInteractionAvailable);
            if (transition == HostPowerTransition.Suspended)
            {
                suspendSeen = true;
                suspendObserved.TrySetResult(observation);
            }
            else if (suspendSeen)
            {
                resumeObserved.TrySetResult(observation);
            }
        }

        environment.PowerTransitionObserved += PowerTransitionObserved;
        try
        {
            mainWindow.Show();
            var openStatus = await PluginMainUiLauncher.OpenAsync(
                HostProvider.Instance.PluginStore,
                PluginId);
            if (openStatus != PluginMainUiOpenStatus.Opened)
            {
                throw new InvalidOperationException(
                    $"Physical power recovery plugin could not open: {openStatus}.");
            }

            var ready = await WaitUntilAsync(
                () => mainWindow.GetPluginRuntimeContentForQuality()
                    is WebView2 { CoreWebView2: not null },
                15_000);
            var webView = mainWindow.GetPluginRuntimeContentForQuality()
                as WebView2;
            if (!ready || webView is null
                || !await WaitForHostVisibilityAsync(webView, expected: true))
            {
                throw new InvalidOperationException(
                    "Physical power recovery WebView did not become ready.");
            }

            var before = mainWindow.GetPluginRuntimeQualityState();
            var beforeResources = await CaptureResourcesAsync("before_suspend");
            var readyPath = Path.ChangeExtension(
                Path.GetFullPath(reportPath),
                ".ready.json");
            Directory.CreateDirectory(Path.GetDirectoryName(readyPath)!);
            WriteNewJson(
                readyPath,
                new
                {
                    schema_version = 1,
                    classification = "physical_sleep_wake_recovery_ready",
                    ready_at = DateTimeOffset.UtcNow,
                    plugin_id = PluginId,
                    session_id = before.SessionId,
                    content_identity = before.ContentIdentity,
                    interaction_available = environment.IsInteractionAvailable,
                });

            var suspended = await suspendObserved.Task.WaitAsync(
                TimeSpan.FromMinutes(10));
            var resumed = await resumeObserved.Task.WaitAsync(
                TimeSpan.FromMinutes(20));
            var unavailableAfterSuspend = !suspended.InteractionAvailable;
            var restored = await WaitUntilAsync(
                () => environment.IsInteractionAvailable,
                15_000);
            await Task.Delay(1_000);
            var after = mainWindow.GetPluginRuntimeQualityState();
            var hostVisibleAfterRestore =
                await WaitForHostVisibilityAsync(webView, expected: true);
            var restoredHostState = IsRestoredHostStateForQuality(
                restored,
                hostVisibleAfterRestore);
            var afterResources = await CaptureResourcesAsync("after_resume");
            var identityPreserved = before.SessionId is not null
                && before.SessionId == after.SessionId
                && before.ContentIdentity != 0
                && before.ContentIdentity == after.ContentIdentity;
            var surfacePreserved = mainWindow.IsVisible
                && after.IsVisible
                && !after.IsDetached;
            var endRequested = await mainWindow.EndPluginRuntimeForQualityAsync();
            var cleanupPassed = endRequested && await WaitUntilAsync(
                () => mainWindow.GetPluginRuntimeQualityState().SessionId is null,
                10_000);
            var passed = unavailableAfterSuspend
                && restored
                && restoredHostState
                && identityPreserved
                && surfacePreserved
                && cleanupPassed
                && resumed.ObservedAt >= suspended.ObservedAt;

            WriteNewJson(
                Path.GetFullPath(reportPath),
                new
                {
                    schema_version = 1,
                    captured_at = DateTimeOffset.UtcNow,
                    classification = "physical_sleep_wake_recovery",
                    event_source = "WM_POWERBROADCAST",
                    plugin_id = PluginId,
                    passed,
                    suspended_at = suspended.ObservedAt,
                    resumed_at = resumed.ObservedAt,
                    resume_kind = resumed.Transition.ToString(),
                    interaction_available_at_resume =
                        resumed.InteractionAvailable,
                    unavailable_after_suspend = unavailableAfterSuspend,
                    restored,
                    restored_host_state = restoredHostState,
                    identity_preserved = identityPreserved,
                    surface_preserved = surfacePreserved,
                    cleanup_passed = cleanupPassed,
                    before,
                    after,
                    resources = new[] { beforeResources, afterResources },
                });
            _application.Shutdown(passed ? 0 : 13);
        }
        finally
        {
            environment.PowerTransitionObserved -= PowerTransitionObserved;
        }
    }

    internal static bool IsRestoredHostStateForQuality(
        bool interactionRestored,
        bool hostVisibleAfterRestore)
        => interactionRestored && hostVisibleAfterRestore;

    private static void WriteNewJson(string path, object value)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read);
        JsonSerializer.Serialize(
            stream,
            value,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });
    }

    private static async Task<bool> WaitForHostVisibilityAsync(
        WebView2 webView,
        bool expected)
        => await WaitUntilAsync(
            async () => string.Equals(
                await webView.CoreWebView2.ExecuteScriptAsync(
                    "window.long?.host?.isVisible?.() === true"),
                expected ? "true" : "false",
                StringComparison.OrdinalIgnoreCase),
            15_000);

    private static async Task<PhysicalPowerResourceSample>
        CaptureResourcesAsync(string stage)
    {
        await Task.Delay(400);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new(
            stage,
            process.HandleCount,
            process.Threads.Count,
            process.PrivateMemorySize64,
            process.WorkingSet64);
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

    private static async Task<bool> WaitUntilAsync(
        Func<Task<bool>> condition,
        int timeoutMilliseconds)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (await condition())
                return true;
            await Task.Delay(40);
        }
        return await condition();
    }

    private sealed record PowerObservation(
        DateTimeOffset ObservedAt,
        HostPowerTransition Transition,
        bool InteractionAvailable);

    private sealed record PhysicalPowerResourceSample(
        string Stage,
        int HandleCount,
        int ThreadCount,
        long PrivateMemoryBytes,
        long WorkingSetBytes);
}
