using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using Microsoft.Web.WebView2.Wpf;

namespace LongBetterWindows.Host.Services;

internal sealed class TrayRecoveryQualityProbe
{
    private const string PluginId = "com.long.hardwaremonitor";
    private const int CycleCount = 8;
    private readonly Application _application;

    internal TrayRecoveryQualityProbe(Application application)
        => _application = application;

    internal async Task RunAsync(MainWindow mainWindow, string reportPath)
    {
        mainWindow.Show();
        var openStatus = await PluginMainUiLauncher.OpenAsync(
            HostProvider.Instance.PluginStore,
            PluginId);
        if (openStatus != PluginMainUiOpenStatus.Opened)
            throw new InvalidOperationException(
                $"Tray recovery plugin could not open: {openStatus}.");

        var ready = await WaitUntilAsync(
            () => mainWindow.GetPluginRuntimeContentForQuality()
                is WebView2 { CoreWebView2: not null },
            15_000);
        var webView = mainWindow.GetPluginRuntimeContentForQuality() as WebView2;
        if (!ready || webView is null)
            throw new InvalidOperationException(
                "Tray recovery WebView did not become ready.");

        var samples = new List<TrayRecoveryResourceSample>
        {
            CaptureResources(0, "warm"),
        };
        var cycles = new List<TrayRecoveryCycleResult>();
        for (var cycle = 1; cycle <= CycleCount; cycle++)
        {
            var closeIntercepted = mainWindow.CloseToTrayForQuality();
            var hidden = await WaitUntilAsync(
                () => !mainWindow.IsVisible,
                3_000);
            var hiddenHostState = hidden
                && !await ReadHostVisibilityAsync(webView);
            await Task.Delay(500);

            var primaryActionHandled = mainWindow.RestoreFromTrayForQuality();
            var restored = await WaitUntilAsync(
                () => mainWindow.IsVisible
                    && mainWindow.WindowState == WindowState.Normal,
                3_000);
            var restoredHostState = restored
                && await WaitForHostVisibilityAsync(webView, expected: true);
            samples.Add(CaptureResources(cycle, "restored"));
            cycles.Add(new(
                cycle,
                closeIntercepted,
                hidden,
                hiddenHostState,
                primaryActionHandled,
                restored,
                restoredHostState,
                closeIntercepted
                    && hidden
                    && hiddenHostState
                    && primaryActionHandled
                    && restored
                    && restoredHostState));
        }

        var warm = samples[1];
        var growth = BackgroundActivityPolicy.EvaluateCombinedGrowth(
            warm.HandleCount,
            samples[^1].HandleCount,
            warm.ThreadCount,
            samples[^1].ThreadCount,
            warm.PrivateMemoryBytes,
            samples[^1].PrivateMemoryBytes);
        var trend = BackgroundActivityPolicy.EvaluateCombinedResourceTrend(
            samples.Skip(1).Select(sample => new CombinedResourceSampleAssessment(
                sample.HandleCount,
                sample.ThreadCount,
                sample.PrivateMemoryBytes)).ToArray());
        var endRequested = await mainWindow.EndPluginRuntimeForQualityAsync();
        var cleanupPassed = endRequested && await WaitUntilAsync(
            () => mainWindow.GetPluginRuntimeQualityState().SessionId is null,
            10_000);
        var passed = cycles.Count == CycleCount
            && cycles.All(cycle => cycle.Passed)
            && growth.Passed
            && trend.Passed
            && cleanupPassed;

        var fullPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(
            fullPath,
            JsonSerializer.Serialize(
                new
                {
                    schema_version = 1,
                    captured_at = DateTimeOffset.UtcNow,
                    classification = "development_tray_recovery",
                    plugin_id = PluginId,
                    cycle_count = CycleCount,
                    passed,
                    cleanup_passed = cleanupPassed,
                    warm_baseline_cycle = warm.Cycle,
                    growth,
                    resource_trend = trend,
                    cycles,
                    samples,
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                }));
        _application.Shutdown(passed ? 0 : 11);
    }

    private static async Task<bool> ReadHostVisibilityAsync(WebView2 webView)
    {
        var result = await webView.CoreWebView2.ExecuteScriptAsync(
            "window.long?.host?.isVisible?.() === true");
        return string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> WaitForHostVisibilityAsync(
        WebView2 webView,
        bool expected)
        => await WaitUntilAsync(
            async () => await ReadHostVisibilityAsync(webView) == expected,
            3_000);

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

    private static TrayRecoveryResourceSample CaptureResources(
        int cycle,
        string stage)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new(
            cycle,
            stage,
            process.HandleCount,
            process.Threads.Count,
            process.PrivateMemorySize64,
            process.WorkingSet64);
    }

    private sealed record TrayRecoveryCycleResult(
        int Cycle,
        bool CloseIntercepted,
        bool Hidden,
        bool HiddenHostState,
        bool PrimaryActionHandled,
        bool Restored,
        bool RestoredHostState,
        bool Passed);

    private sealed record TrayRecoveryResourceSample(
        int Cycle,
        string Stage,
        int HandleCount,
        int ThreadCount,
        long PrivateMemoryBytes,
        long WorkingSetBytes);
}
