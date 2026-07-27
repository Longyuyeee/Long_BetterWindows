using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LongBetterWindows.Host.Services
{
    internal readonly record struct PluginPageVisualMetrics(
        int ItemCount,
        int RealizedContainerCount,
        int VisualDescendantCount);

    internal sealed class PluginPagePerformanceTrace
    {
#if DEBUG
        private const string BuildConfiguration = "Debug";
#else
        private const string BuildConfiguration = "Release";
#endif
        private readonly object _sync = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly List<PluginPagePerformanceSample> _samples = new();
        private readonly string _reportPath;
        private readonly string[] _suppressedAutoStartPluginIds;
        private bool _windowVisibleDuringIdle = true;

        public PluginPagePerformanceTrace(
            string reportPath,
            IEnumerable<string>? suppressedAutoStartPluginIds = null)
        {
            _reportPath = Path.GetFullPath(reportPath);
            _suppressedAutoStartPluginIds =
                suppressedAutoStartPluginIds?
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                ?? [];
        }

        public void Mark(
            string stage,
            PluginPageVisualMetrics? visualMetrics = null)
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var gcInfo = GC.GetGCMemoryInfo();
            var sample = new PluginPagePerformanceSample(
                stage,
                Math.Round(_stopwatch.Elapsed.TotalMilliseconds, 1),
                Math.Round(process.TotalProcessorTime.TotalMilliseconds, 1),
                Math.Round(process.WorkingSet64 / 1024d / 1024d, 1),
                Math.Round(process.PrivateMemorySize64 / 1024d / 1024d, 1),
                Math.Round(GC.GetTotalMemory(forceFullCollection: false) / 1024d / 1024d, 1),
                Math.Round(gcInfo.TotalCommittedBytes / 1024d / 1024d, 1),
                process.Threads.Count,
                visualMetrics?.ItemCount,
                visualMetrics?.RealizedContainerCount,
                visualMetrics?.VisualDescendantCount);

            lock (_sync)
                _samples.Add(sample);
        }

        public void SetWindowVisibleDuringIdle(bool isVisible)
            => _windowVisibleDuringIdle = isVisible;

        public async Task WriteAsync(
            PluginRuntimeStartResult result,
            int commandCount,
            int idleMilliseconds,
            IReadOnlyList<string>? runningPluginIds = null)
        {
            PluginPagePerformanceSample[] samples;
            lock (_sync)
                samples = _samples.ToArray();

            var report = new
            {
                schema_version = 1,
                captured_at = DateTimeOffset.UtcNow,
                configuration = BuildConfiguration,
                process_architecture =
                    System.Runtime.InteropServices.RuntimeInformation
                        .ProcessArchitecture.ToString(),
                idle_ms = idleMilliseconds,
                loaded_plugin_count = result.LoadedPluginCount,
                command_count = commandCount,
                suppressed_auto_start_plugin_ids =
                    _suppressedAutoStartPluginIds,
                window_visible_during_idle = _windowVisibleDuringIdle,
                running_plugin_ids = runningPluginIds ?? [],
                samples = samples.Select(sample => new
                {
                    stage = sample.Stage,
                    elapsed_ms = sample.ElapsedMilliseconds,
                    cpu_ms = sample.CpuMilliseconds,
                    working_set_mb = sample.WorkingSetMegabytes,
                    private_memory_mb = sample.PrivateMemoryMegabytes,
                    managed_heap_mb = sample.ManagedHeapMegabytes,
                    gc_committed_mb = sample.GcCommittedMegabytes,
                    thread_count = sample.ThreadCount,
                    item_count = sample.ItemCount,
                    realized_container_count = sample.RealizedContainerCount,
                    visual_descendant_count = sample.VisualDescendantCount,
                }),
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_reportPath)!);
            await File.WriteAllTextAsync(
                _reportPath,
                JsonSerializer.Serialize(
                    report,
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        private sealed record PluginPagePerformanceSample(
            string Stage,
            double ElapsedMilliseconds,
            double CpuMilliseconds,
            double WorkingSetMegabytes,
            double PrivateMemoryMegabytes,
            double ManagedHeapMegabytes,
            double GcCommittedMegabytes,
            int ThreadCount,
            int? ItemCount,
            int? RealizedContainerCount,
            int? VisualDescendantCount);
    }
}
