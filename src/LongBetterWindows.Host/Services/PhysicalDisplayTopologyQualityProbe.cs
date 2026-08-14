using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using Microsoft.Web.WebView2.Wpf;

namespace LongBetterWindows.Host.Services;

internal sealed class PhysicalDisplayTopologyQualityProbe
{
    private const string PluginId = "com.long.hardwaremonitor";
    private readonly Application _application;

    internal PhysicalDisplayTopologyQualityProbe(Application application)
        => _application = application;

    internal async Task RunAsync(MainWindow mainWindow, string reportPath)
    {
        var environment = HostEnvironmentStateService.Current;
        var displayEventCount = 0;
        void DisplayTopologyChanged() => Interlocked.Increment(
            ref displayEventCount);

        environment.DisplayTopologyChanged += DisplayTopologyChanged;
        try
        {
            mainWindow.Show();
            var openStatus = await PluginMainUiLauncher.OpenAsync(
                HostProvider.Instance.PluginStore,
                PluginId);
            if (openStatus != PluginMainUiOpenStatus.Opened)
            {
                throw new InvalidOperationException(
                    $"Physical display topology plugin could not open: {openStatus}.");
            }

            var ready = await WaitUntilAsync(
                () => mainWindow.GetPluginRuntimeContentForQuality()
                    is WebView2 { CoreWebView2: not null },
                15_000);
            var webView = mainWindow.GetPluginRuntimeContentForQuality()
                as WebView2;
            if (!ready || webView is null
                || !await WaitForHostVisibilityAsync(webView))
            {
                throw new InvalidOperationException(
                    "Physical display topology WebView did not become ready.");
            }

            var initialMonitors = CaptureMonitors();
            if (initialMonitors.Count < 2)
            {
                throw new InvalidOperationException(
                    "Physical display topology recovery requires at least two active monitors.");
            }
            var before = CaptureRuntimeState(
                mainWindow.GetPluginRuntimeQualityState());
            var initialWindow = CaptureWindowPlacement(mainWindow);
            var readyPath = Path.ChangeExtension(
                Path.GetFullPath(reportPath),
                ".ready.json");
            Directory.CreateDirectory(Path.GetDirectoryName(readyPath)!);
            WriteNewJson(
                readyPath,
                new
                {
                    schema_version = 1,
                    classification = "physical_display_topology_recovery_ready",
                    ready_at = DateTimeOffset.UtcNow,
                    plugin_id = PluginId,
                    before,
                    initial_window = initialWindow,
                    monitors = initialMonitors,
                });

            var reduced = await WaitUntilAsync(
                () => Volatile.Read(ref displayEventCount) > 0
                    && QualityMonitorPlacement.EnumerateForQuality().Count
                        < initialMonitors.Count,
                120_000);
            if (!reduced)
                throw new TimeoutException("Reduced display topology was not observed.");

            var reducedAt = DateTimeOffset.UtcNow;
            var reducedEventCount = Volatile.Read(ref displayEventCount);
            var reducedMonitors = CaptureMonitors();
            var reducedWindow = await WaitForWindowOnActiveMonitorAsync(mainWindow);
            var reducedHostState = await WaitForHostVisibilityAsync(webView);
            var reducedReadyPath = Path.ChangeExtension(
                Path.GetFullPath(reportPath),
                ".reduced.ready.json");
            WriteNewJson(
                reducedReadyPath,
                new
                {
                    schema_version = 1,
                    classification = "physical_display_topology_reduced_ready",
                    reduced_at = reducedAt,
                    display_event_count = reducedEventCount,
                    monitors = reducedMonitors,
                    window = reducedWindow,
                    host_visible = reducedHostState,
                });

            var restored = await WaitUntilAsync(
                () => Volatile.Read(ref displayEventCount) > reducedEventCount
                    && QualityMonitorPlacement.EnumerateForQuality().Count
                        >= initialMonitors.Count,
                120_000);
            if (!restored)
                throw new TimeoutException("Restored display topology was not observed.");

            var restoredAt = DateTimeOffset.UtcNow;
            var restoredMonitors = CaptureMonitors();
            var restoredWindow = await WaitForWindowOnActiveMonitorAsync(mainWindow);
            var restoredHostState = await WaitForHostVisibilityAsync(webView);
            await Task.Delay(800);
            var after = CaptureRuntimeState(
                mainWindow.GetPluginRuntimeQualityState());
            var identityPreserved = before.SessionId is not null
                && before.SessionId == after.SessionId
                && before.ContentIdentity != 0
                && before.ContentIdentity == after.ContentIdentity;
            var surfacePreserved = mainWindow.IsVisible
                && after.IsVisible
                && !after.IsDetached;
            var topologyRestored = HasSameDevices(
                initialMonitors,
                restoredMonitors);
            var endRequested = await mainWindow.EndPluginRuntimeForQualityAsync();
            var cleanupPassed = endRequested && await WaitUntilAsync(
                () => mainWindow.GetPluginRuntimeQualityState().SessionId is null,
                10_000);
            var passed = EvaluateForQuality(
                initialMonitors.Count,
                reducedMonitors.Count,
                restoredMonitors.Count,
                Volatile.Read(ref displayEventCount),
                reducedWindow.IntersectsWorkArea,
                restoredWindow.IntersectsWorkArea,
                reducedHostState,
                restoredHostState,
                topologyRestored,
                identityPreserved,
                surfacePreserved,
                cleanupPassed);

            WriteNewJson(
                Path.GetFullPath(reportPath),
                new
                {
                    schema_version = 1,
                    captured_at = DateTimeOffset.UtcNow,
                    classification = "physical_display_topology_recovery",
                    event_source = "WM_DISPLAYCHANGE",
                    plugin_id = PluginId,
                    passed,
                    reduced_at = reducedAt,
                    restored_at = restoredAt,
                    display_event_count = Volatile.Read(ref displayEventCount),
                    topology_restored = topologyRestored,
                    reduced_host_state = reducedHostState,
                    restored_host_state = restoredHostState,
                    identity_preserved = identityPreserved,
                    surface_preserved = surfacePreserved,
                    cleanup_passed = cleanupPassed,
                    before,
                    after,
                    initial_window = initialWindow,
                    reduced_window = reducedWindow,
                    restored_window = restoredWindow,
                    initial_monitors = initialMonitors,
                    reduced_monitors = reducedMonitors,
                    restored_monitors = restoredMonitors,
                });
            _application.Shutdown(passed ? 0 : 14);
        }
        finally
        {
            environment.DisplayTopologyChanged -= DisplayTopologyChanged;
        }
    }

    internal static bool EvaluateForQuality(
        int initialMonitorCount,
        int reducedMonitorCount,
        int restoredMonitorCount,
        int displayEventCount,
        bool reducedWindowOnMonitor,
        bool restoredWindowOnMonitor,
        bool reducedHostState,
        bool restoredHostState,
        bool topologyRestored,
        bool identityPreserved,
        bool surfacePreserved,
        bool cleanupPassed)
        => initialMonitorCount >= 2
            && reducedMonitorCount == 1
            && restoredMonitorCount >= initialMonitorCount
            && displayEventCount >= 2
            && reducedWindowOnMonitor
            && restoredWindowOnMonitor
            && reducedHostState
            && restoredHostState
            && topologyRestored
            && identityPreserved
            && surfacePreserved
            && cleanupPassed;

    private static IReadOnlyList<MonitorSnapshot> CaptureMonitors()
        => QualityMonitorPlacement.EnumerateForQuality()
            .Select(monitor => new MonitorSnapshot(
                monitor.DeviceName,
                monitor.Bounds,
                monitor.WorkArea,
                monitor.IsPrimary,
                monitor.Dpi))
            .OrderBy(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool HasSameDevices(
        IReadOnlyList<MonitorSnapshot> expected,
        IReadOnlyList<MonitorSnapshot> actual)
        => expected.Select(monitor => monitor.DeviceName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(
                actual.Select(monitor => monitor.DeviceName)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

    private static async Task<WindowPlacementSnapshot>
        WaitForWindowOnActiveMonitorAsync(MainWindow mainWindow)
    {
        WindowPlacementSnapshot? result = null;
        var ready = await WaitUntilAsync(
            () =>
            {
                result = CaptureWindowPlacement(mainWindow);
                return result.IntersectsWorkArea;
            },
            15_000);
        return ready && result is not null
            ? result
            : result ?? CaptureWindowPlacement(mainWindow);
    }

    private static WindowPlacementSnapshot CaptureWindowPlacement(
        MainWindow mainWindow)
    {
        var described = QualityMonitorPlacement.TryDescribe(
            mainWindow,
            out var monitor,
            out var monitorError);
        var handle = new WindowInteropHelper(mainWindow).Handle;
        var native = new WindowNativeApi();
        var bounds = default(NativeWindowRect);
        var boundsError = handle == IntPtr.Zero ? -1 : 0;
        var hasBounds = handle != IntPtr.Zero
            && native.TryGetWindowRect(
                handle,
                out bounds,
                out boundsError);
        var intersects = described
            && monitor is not null
            && hasBounds
            && Intersects(bounds, monitor.WorkArea);
        return new(
            described ? monitor?.DeviceName : null,
            hasBounds ? bounds : null,
            monitor?.WorkArea,
            intersects,
            described ? null : monitorError,
            hasBounds ? null : boundsError);
    }

    private static bool Intersects(NativeWindowRect left, NativeWindowRect right)
        => left.Left < right.Right
            && left.Right > right.Left
            && left.Top < right.Bottom
            && left.Bottom > right.Top;

    private static RuntimeStateSnapshot CaptureRuntimeState((
        string? ModuleKey,
        string? SessionId,
        int ContentIdentity,
        bool IsVisible,
        bool IsDetached) state)
        => new(
            state.ModuleKey,
            state.SessionId,
            state.ContentIdentity,
            state.IsVisible,
            state.IsDetached);

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

    private static async Task<bool> WaitForHostVisibilityAsync(WebView2 webView)
        => await WaitUntilAsync(
            async () => string.Equals(
                await webView.CoreWebView2.ExecuteScriptAsync(
                    "window.long?.host?.isVisible?.() === true"),
                "true",
                StringComparison.OrdinalIgnoreCase),
            15_000);

    private static async Task<bool> WaitUntilAsync(
        Func<bool> condition,
        int timeoutMilliseconds)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(50);
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
            await Task.Delay(50);
        }
        return await condition();
    }

    private sealed record RuntimeStateSnapshot(
        string? ModuleKey,
        string? SessionId,
        int ContentIdentity,
        bool IsVisible,
        bool IsDetached);

    private sealed record MonitorSnapshot(
        string DeviceName,
        NativeWindowRect Bounds,
        NativeWindowRect WorkArea,
        bool IsPrimary,
        uint Dpi);

    private sealed record WindowPlacementSnapshot(
        string? MonitorDeviceName,
        NativeWindowRect? WindowBounds,
        NativeWindowRect? MonitorWorkArea,
        bool IntersectsWorkArea,
        string? MonitorError,
        int? BoundsError);
}
