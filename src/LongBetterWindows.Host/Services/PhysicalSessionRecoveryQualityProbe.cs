using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using Microsoft.Web.WebView2.Wpf;

namespace LongBetterWindows.Host.Services;

internal sealed class PhysicalSessionRecoveryQualityProbe
{
    private const string PluginId = "com.long.hardwaremonitor";
    private readonly Application _application;

    internal PhysicalSessionRecoveryQualityProbe(Application application)
        => _application = application;

    internal async Task RunAsync(MainWindow mainWindow, string reportPath)
    {
        var environment = HostEnvironmentStateService.Current;
        var lockObserved = new TaskCompletionSource<DateTimeOffset>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var unlockObserved = new TaskCompletionSource<DateTimeOffset>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lockSeen = false;
        void AvailabilityChanged(bool available)
        {
            if (!available)
            {
                lockSeen = true;
                lockObserved.TrySetResult(DateTimeOffset.UtcNow);
            }
            else if (lockSeen)
            {
                unlockObserved.TrySetResult(DateTimeOffset.UtcNow);
            }
        }

        environment.InteractionAvailabilityChanged += AvailabilityChanged;
        try
        {
            mainWindow.Show();
            var openStatus = await PluginMainUiLauncher.OpenAsync(
                HostProvider.Instance.PluginStore,
                PluginId);
            if (openStatus != PluginMainUiOpenStatus.Opened)
            {
                throw new InvalidOperationException(
                    $"Physical session recovery plugin could not open: {openStatus}.");
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
                    "Physical session recovery WebView did not become ready.");
            }

            var before = mainWindow.GetPluginRuntimeQualityState();
            var beforeResources = await CaptureResourcesAsync("before_lock");
            var readyPath = Path.ChangeExtension(
                Path.GetFullPath(reportPath),
                ".ready.json");
            Directory.CreateDirectory(Path.GetDirectoryName(readyPath)!);
            WriteNewJson(
                readyPath,
                new
                {
                    schema_version = 1,
                    ready_at = DateTimeOffset.UtcNow,
                    plugin_id = PluginId,
                    session_id = before.SessionId,
                    content_identity = before.ContentIdentity,
                    interaction_available = environment.IsInteractionAvailable,
                });

            var lockedAt = await lockObserved.Task.WaitAsync(
                TimeSpan.FromMinutes(10));
            var unavailableAfterLock = !environment.IsInteractionAvailable;
            var unlockedAt = await unlockObserved.Task.WaitAsync(
                TimeSpan.FromMinutes(10));
            var restored = await WaitUntilAsync(
                () => environment.IsInteractionAvailable,
                10_000);
            await Task.Delay(1_000);
            var after = mainWindow.GetPluginRuntimeQualityState();
            var restoredHostState = restored
                && await WaitForHostVisibilityAsync(webView, expected: true);
            var afterResources = await CaptureResourcesAsync("after_unlock");
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
            var passed = unavailableAfterLock
                && restored
                && restoredHostState
                && identityPreserved
                && surfacePreserved
                && cleanupPassed
                && unlockedAt >= lockedAt;

            WriteNewJson(
                Path.GetFullPath(reportPath),
                new
                {
                    schema_version = 1,
                    captured_at = DateTimeOffset.UtcNow,
                    classification = "physical_session_lock_recovery",
                    plugin_id = PluginId,
                    passed,
                    locked_at = lockedAt,
                    unlocked_at = unlockedAt,
                    unavailable_after_lock = unavailableAfterLock,
                    restored,
                    restored_host_state = restoredHostState,
                    identity_preserved = identityPreserved,
                    surface_preserved = surfacePreserved,
                    cleanup_passed = cleanupPassed,
                    before,
                    after,
                    resources = new[] { beforeResources, afterResources },
                });
            _application.Shutdown(passed ? 0 : 12);
        }
        finally
        {
            environment.InteractionAvailabilityChanged -= AvailabilityChanged;
        }
    }

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
            10_000);

    private static async Task<PhysicalSessionResourceSample>
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

    private sealed record PhysicalSessionResourceSample(
        string Stage,
        int HandleCount,
        int ThreadCount,
        long PrivateMemoryBytes,
        long WorkingSetBytes);
}
