using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using Microsoft.Web.WebView2.Wpf;
using Serilog;

namespace LongBetterWindows.Host.Services;

internal sealed class BackgroundPluginActivityQualityProbe
{
    private const int VisibleMilliseconds = 6_000;
    private const int HiddenMilliseconds = 6_000;
    private const int RestoredMilliseconds = 4_000;
    private static readonly string[] PluginIds =
    [
        "com.long.clipboardhistory",
        "com.long.hardwaremonitor",
        "com.long.portmanager",
    ];

    private readonly Application _application;

    internal BackgroundPluginActivityQualityProbe(Application application)
    {
        _application = application;
    }

    internal async Task RunAsync(MainWindow mainWindow, string reportPath)
    {
        mainWindow.Hide();
        using var messages = new WindowMessageActivityTrace(mainWindow);
        var results = new List<BackgroundPluginActivityResult>();
        foreach (var pluginId in PluginIds)
        {
            Log.Information(
                "Background activity probe starting {PluginId}",
                pluginId);
            results.Add(await ProbePluginAsync(mainWindow, messages, pluginId));
            Log.Information(
                "Background activity probe completed {PluginId}",
                pluginId);
        }

        var combinedIdle = await ProbeCombinedIdleAsync(mainWindow, messages);
        var passed = results.All(result => result.Passed)
            && combinedIdle.Passed;
        var fullPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(
            fullPath,
            JsonSerializer.Serialize(
                new
                {
                    schema_version = 3,
                    captured_at = DateTimeOffset.UtcNow,
                    classification = "development_background_plugin_activity",
                    passed,
                    visible_ms = VisibleMilliseconds,
                    hidden_ms = HiddenMilliseconds,
                    restored_ms = RestoredMilliseconds,
                    limits = new
                    {
                        hidden_cpu_core_percent =
                            BackgroundActivityPolicy.MaximumHiddenCpuCorePercent,
                        hidden_window_messages =
                            BackgroundActivityPolicy.MaximumHiddenWindowMessages,
                        hidden_api_calls = 0,
                        combined_sample_count =
                            BackgroundActivityPolicy.CombinedIdleSampleCount,
                        combined_sample_ms = BackgroundActivityPolicy
                            .CombinedIdleSampleMilliseconds,
                        combined_window_messages = BackgroundActivityPolicy
                            .MaximumCombinedWindowMessages,
                        combined_handle_growth = BackgroundActivityPolicy
                            .MaximumCombinedHandleGrowth,
                        combined_thread_growth = BackgroundActivityPolicy
                            .MaximumCombinedThreadGrowth,
                        combined_private_memory_growth_bytes =
                            BackgroundActivityPolicy
                                .MaximumCombinedPrivateMemoryGrowthBytes,
                    },
                    plugins = results,
                    combined_idle = combinedIdle,
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                }));
        _application.Shutdown(passed ? 0 : 8);
    }

    private async Task<CombinedIdleResult> ProbeCombinedIdleAsync(
        MainWindow mainWindow,
        WindowMessageActivityTrace messages)
    {
        var registry = HostProvider.Instance.PluginStore;
        var webViews = new Dictionary<string, WebView2>(
            StringComparer.OrdinalIgnoreCase);
        var sessionIds = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var pluginId in PluginIds)
        {
            await OpenPluginAsync(pluginId);
            var webView = await WaitForWebViewAsync(mainWindow, pluginId);
            var sessionId = mainWindow.GetPluginRuntimeQualityState().SessionId;
            if (sessionId is null)
            {
                throw new InvalidOperationException(
                    $"Combined idle session was not created: {pluginId}");
            }
            webViews[pluginId] = webView;
            sessionIds[pluginId] = sessionId;
        }

        await mainWindow.OpenManagementPageForQualityAsync(
            WorkspaceManagementPage.Overview);
        var hostStates = new Dictionary<string, bool>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var pair in webViews)
        {
            hostStates[pair.Key] = await WaitForHostVisibilityAsync(
                pair.Value,
                expected: false);
        }
        var allHostsHidden = hostStates.Values.All(hidden => hidden);
        await Task.Delay(500);

        await MeasureActivityAsync(
            webViews.Values,
            BackgroundActivityPolicy.CombinedIdleSampleMilliseconds);

        var baseline = CaptureProcesses(webViews.Values);
        var samples = new List<CombinedIdleSample>();
        for (var sample = 1;
             sample <= BackgroundActivityPolicy.CombinedIdleSampleCount;
             sample++)
        {
            var apiBefore = PluginIds.ToDictionary(
                pluginId => pluginId,
                SnapshotApiCalls,
                StringComparer.OrdinalIgnoreCase);
            var startStage = $"combined:{sample}:start";
            var endStage = $"combined:{sample}:end";
            messages.Mark(startStage);
            var activity = await MeasureActivityAsync(
                webViews.Values,
                BackgroundActivityPolicy.CombinedIdleSampleMilliseconds);
            messages.Mark(endStage);
            var apiCallsByPlugin = PluginIds.ToDictionary(
                pluginId => pluginId,
                pluginId => GetApiCallDelta(
                    apiBefore[pluginId],
                    SnapshotApiCalls(pluginId),
                    _ => true),
                StringComparer.OrdinalIgnoreCase);
            var apiCalls = apiCallsByPlugin.Values.Sum();
            samples.Add(new CombinedIdleSample(
                sample,
                activity.CpuCorePercent,
                GetMessageDelta(messages.Checkpoints, startStage, endStage),
                apiCalls,
                activity.ProcessCount,
                activity.HandleCount,
                activity.ThreadCount,
                activity.PrivateMemoryBytes,
                activity.WorkingSetBytes,
                apiCallsByPlugin));
        }

        var final = CaptureProcesses(webViews.Values);
        var growth = BackgroundActivityPolicy.EvaluateCombinedGrowth(
            baseline.Values.Sum(item => item.HandleCount),
            final.Values.Sum(item => item.HandleCount),
            baseline.Values.Sum(item => item.ThreadCount),
            final.Values.Sum(item => item.ThreadCount),
            baseline.Values.Sum(item => item.PrivateMemoryBytes),
            final.Values.Sum(item => item.PrivateMemoryBytes));

        var stopRequested = true;
        foreach (var pluginId in PluginIds)
        {
            stopRequested &= await registry.StopPluginAsync(
                pluginId,
                persistAutoStart: false);
        }
        var cleanupPassed = stopRequested && await WaitUntilAsync(
            () => PluginIds.All(pluginId =>
                    registry.Get(pluginId)?.State == PluginState.Stopped)
                && sessionIds.Values.All(sessionId =>
                    ServicesInitializer.PluginSessions
                        .GetBySessionId(sessionId) is null)
                && mainWindow.GetWorkspaceModuleCountsForQuality()
                    .PluginRuntime == 0,
            15_000);
        var assessments = samples.Select(sample =>
            new CombinedIdleSampleAssessment(
                sample.CpuCorePercent,
                sample.WindowMessages,
                sample.ApiCalls)).ToArray();
        var passed = BackgroundActivityPolicy.EvaluateCombinedIdle(
            assessments,
            growth,
            allHostsHidden,
            cleanupPassed);
        return new CombinedIdleResult(
            passed,
            allHostsHidden,
            cleanupPassed,
            growth,
            samples,
            hostStates);
    }

    private async Task<BackgroundPluginActivityResult> ProbePluginAsync(
        MainWindow mainWindow,
        WindowMessageActivityTrace messages,
        string pluginId)
    {
        var registry = HostProvider.Instance.PluginStore;
        var entry = registry.Get(pluginId)
            ?? throw new InvalidOperationException(
                $"Background activity plugin was not found: {pluginId}");
        var visibleStatsBefore = SnapshotApiCalls(pluginId);
        await OpenPluginAsync(pluginId);
        var webView = await WaitForWebViewAsync(mainWindow, pluginId);
        await Task.Delay(500);

        messages.Mark($"{pluginId}:visible:start");
        var visibleActivity = await MeasureActivityAsync(
            webView,
            VisibleMilliseconds);
        messages.Mark($"{pluginId}:visible:end");
        var visibleStatsAfter = SnapshotApiCalls(pluginId);

        await mainWindow.OpenManagementPageForQualityAsync(
            WorkspaceManagementPage.Overview);
        var hiddenHostState = await WaitForHostVisibilityAsync(
            webView,
            expected: false);
        await Task.Delay(500);
        var hiddenStatsBefore = SnapshotApiCalls(pluginId);
        messages.Mark($"{pluginId}:hidden:start");
        var hiddenActivity = await MeasureActivityAsync(
            webView,
            HiddenMilliseconds);
        messages.Mark($"{pluginId}:hidden:end");
        var hiddenStatsAfter = SnapshotApiCalls(pluginId);

        var restoredStatsBefore = SnapshotApiCalls(pluginId);
        await OpenPluginAsync(pluginId);
        var restoredReady = await WaitForActivePluginAsync(
            mainWindow,
            pluginId);
        var restoredHostState = restoredReady
            && await WaitForHostVisibilityAsync(webView, expected: true);
        messages.Mark($"{pluginId}:restored:start");
        var restoredActivity = await MeasureActivityAsync(
            webView,
            RestoredMilliseconds);
        messages.Mark($"{pluginId}:restored:end");
        var restoredStatsAfter = SnapshotApiCalls(pluginId);

        var checkpoints = messages.Checkpoints;
        var hiddenMessages = GetMessageDelta(
            checkpoints,
            $"{pluginId}:hidden:start",
            $"{pluginId}:hidden:end");
        var hiddenApiCalls = GetApiCallDelta(
            hiddenStatsBefore,
            hiddenStatsAfter,
            _ => true);
        var hiddenPerformanceCalls = GetApiCallDelta(
            hiddenStatsBefore,
            hiddenStatsAfter,
            method => method.StartsWith(
                "performance.",
                StringComparison.Ordinal));
        var hiddenClipboardReads = GetApiCallDelta(
            hiddenStatsBefore,
            hiddenStatsAfter,
            method => method == "clipboard.getText");
        var visiblePerformanceCalls = GetApiCallDelta(
            visibleStatsBefore,
            visibleStatsAfter,
            method => method.StartsWith(
                "performance.",
                StringComparison.Ordinal));
        var restoredPerformanceCalls = GetApiCallDelta(
            restoredStatsBefore,
            restoredStatsAfter,
            method => method.StartsWith(
                "performance.",
                StringComparison.Ordinal));
        var visibleActivityCalls = GetActivityCallDelta(
            pluginId,
            visibleStatsBefore,
            visibleStatsAfter);
        var restoredActivityCalls = GetActivityCallDelta(
            pluginId,
            restoredStatsBefore,
            restoredStatsAfter);
        var activityPassed = BackgroundActivityPolicy.Evaluate(
            pluginId,
            hiddenActivity.CpuCorePercent,
            hiddenMessages,
            hiddenApiCalls,
            hiddenHostState,
            restoredHostState);
        var functionalPassed = visibleActivityCalls > 0
            && restoredActivityCalls > 0;

        var state = mainWindow.GetPluginRuntimeQualityState();
        var endRequested = await mainWindow.EndPluginRuntimeForQualityAsync();
        var cleanupPassed = endRequested && await WaitUntilAsync(
            () => entry.State == PluginState.Stopped
                && (state.SessionId is null
                    || ServicesInitializer.PluginSessions
                        .GetBySessionId(state.SessionId) is null),
            15_000);
        return new BackgroundPluginActivityResult(
            pluginId,
            activityPassed && functionalPassed && cleanupPassed,
            activityPassed,
            functionalPassed,
            cleanupPassed,
            hiddenHostState,
            restoredHostState,
            hiddenMessages,
            hiddenApiCalls,
            hiddenPerformanceCalls,
            hiddenClipboardReads,
            visibleActivityCalls,
            restoredActivityCalls,
            visiblePerformanceCalls,
            restoredPerformanceCalls,
            visibleActivity,
            hiddenActivity,
            restoredActivity);
    }

    private static async Task OpenPluginAsync(string pluginId)
    {
        var status = await PluginMainUiLauncher.OpenAsync(
            HostProvider.Instance.PluginStore,
            pluginId);
        if (status != PluginMainUiOpenStatus.Opened)
            throw new InvalidOperationException(
                $"Background activity plugin could not open: {pluginId} ({status})");
    }

    private static async Task<WebView2> WaitForWebViewAsync(
        MainWindow mainWindow,
        string pluginId)
    {
        WebView2? webView = null;
        var ready = await WaitUntilAsync(
            () =>
            {
                if (!IsActivePlugin(mainWindow, pluginId))
                    return false;
                webView = mainWindow.GetPluginRuntimeContentForQuality()
                    as WebView2;
                return webView?.CoreWebView2 is not null;
            },
            15_000);
        if (!ready || webView?.CoreWebView2 is null)
        {
            throw new InvalidOperationException(
                $"Background activity WebView did not become ready: {pluginId}");
        }
        return webView;
    }

    private static Task<bool> WaitForActivePluginAsync(
        MainWindow mainWindow,
        string pluginId)
        => WaitUntilAsync(
            () => IsActivePlugin(mainWindow, pluginId),
            15_000);

    private static bool IsActivePlugin(
        MainWindow mainWindow,
        string pluginId)
    {
        var state = mainWindow.GetPluginRuntimeQualityState();
        var active = mainWindow.GetActiveWorkspaceModuleKeyForQuality();
        return state.IsVisible
            && !state.IsDetached
            && state.SessionId is not null
            && active.Kind == "plugin-runtime"
            && string.Equals(
                active.ResourceId,
                pluginId,
                StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> WaitForHostVisibilityAsync(
        WebView2 webView,
        bool expected)
    {
        var expectedJson = expected ? "true" : "false";
        var deadline = Environment.TickCount64 + 5_000;
        while (Environment.TickCount64 < deadline)
        {
            if (webView.CoreWebView2 is not null
                && await webView.ExecuteScriptAsync(
                    "window.long?.host?.isVisible?.() === true")
                    == expectedJson)
            {
                return true;
            }
            await Task.Delay(40);
        }
        return webView.CoreWebView2 is not null
            && await webView.ExecuteScriptAsync(
                "window.long?.host?.isVisible?.() === true")
                == expectedJson;
    }

    private async Task<BackgroundProcessActivity> MeasureActivityAsync(
        WebView2 webView,
        int milliseconds)
        => await MeasureActivityAsync([webView], milliseconds);

    private async Task<BackgroundProcessActivity> MeasureActivityAsync(
        IEnumerable<WebView2> webViews,
        int milliseconds)
    {
        await _application.Dispatcher.InvokeAsync(
            () => { },
            DispatcherPriority.ContextIdle);
        var before = CaptureProcesses(webViews);
        await Task.Delay(milliseconds);
        await _application.Dispatcher.InvokeAsync(
            () => { },
            DispatcherPriority.ContextIdle);
        var after = CaptureProcesses(webViews);
        var common = before.Keys.Intersect(after.Keys).ToArray();
        var cpuMilliseconds = common.Sum(processId => Math.Max(
            0,
            after[processId].CpuMilliseconds
                - before[processId].CpuMilliseconds));
        return new BackgroundProcessActivity(
            milliseconds,
            Math.Round(cpuMilliseconds, 1),
            Math.Round(cpuMilliseconds / milliseconds * 100, 2),
            after.Count,
            after.Values.Sum(item => item.HandleCount),
            after.Values.Sum(item => item.ThreadCount),
            after.Values.Sum(item => item.PrivateMemoryBytes),
            after.Values.Sum(item => item.WorkingSetBytes));
    }

    private static Dictionary<int, ProcessActivitySnapshot> CaptureProcesses(
        WebView2 webView)
        => CaptureProcesses([webView]);

    private static Dictionary<int, ProcessActivitySnapshot> CaptureProcesses(
        IEnumerable<WebView2> webViews)
    {
        var webViewProcessIds = webViews.SelectMany(webView =>
        {
            try
            {
                return webView.CoreWebView2.Environment
                    .GetProcessInfos()
                    .Select(info => checked((int)info.ProcessId))
                    .ToArray();
            }
            catch (Exception exception)
                when (exception is COMException or InvalidOperationException)
            {
                return [];
            }
        });
        var processIds = webViewProcessIds
            .Append(Environment.ProcessId)
            .Distinct();
        var result = new Dictionary<int, ProcessActivitySnapshot>();
        foreach (var processId in processIds)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                process.Refresh();
                result[processId] = new ProcessActivitySnapshot(
                    process.TotalProcessorTime.TotalMilliseconds,
                    process.HandleCount,
                    process.Threads.Count,
                    process.PrivateMemorySize64,
                    process.WorkingSet64);
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    or InvalidOperationException
                    or Win32Exception)
            {
            }
        }
        return result;
    }

    private static IReadOnlyDictionary<string, int> SnapshotApiCalls(
        string pluginId)
        => CapabilityUsageTracker.Instance.GetStatsSnapshot(pluginId)?
            .ApiMethodCalls
            .ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<string, int>();

    private static int GetApiCallDelta(
        IReadOnlyDictionary<string, int> before,
        IReadOnlyDictionary<string, int> after,
        Func<string, bool> predicate)
        => after.Where(pair => predicate(pair.Key)).Sum(pair =>
            pair.Value - before.GetValueOrDefault(pair.Key));

    private static int GetActivityCallDelta(
        string pluginId,
        IReadOnlyDictionary<string, int> before,
        IReadOnlyDictionary<string, int> after)
    {
        var prefix = pluginId switch
        {
            "com.long.hardwaremonitor" => "performance.",
            "com.long.portmanager" => "networkPort.",
            "com.long.clipboardhistory" => "clipboard.",
            _ => string.Empty,
        };
        return GetApiCallDelta(
            before,
            after,
            method => method.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static int GetMessageDelta(
        IReadOnlyList<WindowMessageCheckpoint> checkpoints,
        string fromStage,
        string toStage)
    {
        var before = checkpoints.Last(item => item.Stage == fromStage);
        var after = checkpoints.Last(item => item.Stage == toStage);
        return Math.Max(0, after.TotalCount - before.TotalCount);
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

    private sealed record ProcessActivitySnapshot(
        double CpuMilliseconds,
        int HandleCount,
        int ThreadCount,
        long PrivateMemoryBytes,
        long WorkingSetBytes);

    private sealed record BackgroundPluginActivityResult(
        string PluginId,
        bool Passed,
        bool ActivityPassed,
        bool FunctionalPassed,
        bool CleanupPassed,
        bool HiddenHostState,
        bool RestoredHostState,
        int HiddenWindowMessages,
        int HiddenApiCalls,
        int HiddenPerformanceCalls,
        int HiddenClipboardReads,
        int VisibleActivityCalls,
        int RestoredActivityCalls,
        int VisiblePerformanceCalls,
        int RestoredPerformanceCalls,
        BackgroundProcessActivity Visible,
        BackgroundProcessActivity Hidden,
        BackgroundProcessActivity Restored);

    private sealed record BackgroundProcessActivity(
        int ElapsedMilliseconds,
        double CpuMilliseconds,
        double CpuCorePercent,
        int ProcessCount,
        int HandleCount,
        int ThreadCount,
        long PrivateMemoryBytes,
        long WorkingSetBytes);

    private sealed record CombinedIdleSample(
        int Sample,
        double CpuCorePercent,
        int WindowMessages,
        int ApiCalls,
        int ProcessCount,
        int HandleCount,
        int ThreadCount,
        long PrivateMemoryBytes,
        long WorkingSetBytes,
        IReadOnlyDictionary<string, int> ApiCallsByPlugin);

    private sealed record CombinedIdleResult(
        bool Passed,
        bool AllHostsHidden,
        bool CleanupPassed,
        WebViewLifecycleGrowthResult Growth,
        IReadOnlyList<CombinedIdleSample> Samples,
        IReadOnlyDictionary<string, bool> HostStates);
}
