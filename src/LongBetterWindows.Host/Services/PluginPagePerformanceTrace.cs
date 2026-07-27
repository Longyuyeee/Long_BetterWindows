using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LongBetterWindows.Host.Services
{
    internal readonly record struct PluginPageVisualMetrics(
        int ItemCount,
        int RealizedContainerCount,
        int VisualDescendantCount,
        int AnimatedPropertyCount = 0);

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
        private readonly int _uiThreadId = unchecked((int)GetCurrentThreadId());
        private IReadOnlyList<WindowMessageCheckpoint> _windowMessageCheckpoints = [];
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
            var topThreads = GetTopProcessorThreads(process);
            var sample = new PluginPagePerformanceSample(
                stage,
                Math.Round(_stopwatch.Elapsed.TotalMilliseconds, 1),
                Math.Round(process.TotalProcessorTime.TotalMilliseconds, 1),
                Math.Round(process.WorkingSet64 / 1024d / 1024d, 1),
                Math.Round(process.PrivateMemorySize64 / 1024d / 1024d, 1),
                Math.Round(GC.GetTotalMemory(forceFullCollection: false) / 1024d / 1024d, 1),
                Math.Round(gcInfo.TotalCommittedBytes / 1024d / 1024d, 1),
                process.Threads.Count,
                topThreads,
                visualMetrics?.ItemCount,
                visualMetrics?.RealizedContainerCount,
                visualMetrics?.VisualDescendantCount,
                visualMetrics?.AnimatedPropertyCount);

            lock (_sync)
                _samples.Add(sample);
        }

        public void SetWindowVisibleDuringIdle(bool isVisible)
            => _windowVisibleDuringIdle = isVisible;

        public void SetWindowMessageCheckpoints(
            IReadOnlyList<WindowMessageCheckpoint> checkpoints)
            => _windowMessageCheckpoints = checkpoints.ToArray();

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
                ui_thread_id = _uiThreadId,
                idle_ms = idleMilliseconds,
                loaded_plugin_count = result.LoadedPluginCount,
                command_count = commandCount,
                suppressed_auto_start_plugin_ids =
                    _suppressedAutoStartPluginIds,
                window_visible_during_idle = _windowVisibleDuringIdle,
                running_plugin_ids = runningPluginIds ?? [],
                window_message_checkpoints =
                    _windowMessageCheckpoints.Select(checkpoint => new
                    {
                        stage = checkpoint.Stage,
                        total_count = checkpoint.TotalCount,
                        top_messages = checkpoint.TopMessages.Select(message => new
                        {
                            id = message.Id,
                            name = message.Name,
                            count = message.Count,
                        }),
                    }),
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
                    top_threads = sample.TopThreads.Select(thread => new
                    {
                        id = thread.Id,
                        cpu_ms = thread.CpuMilliseconds,
                    }),
                    item_count = sample.ItemCount,
                    realized_container_count = sample.RealizedContainerCount,
                    visual_descendant_count = sample.VisualDescendantCount,
                    animated_property_count = sample.AnimatedPropertyCount,
                }),
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_reportPath)!);
            await File.WriteAllTextAsync(
                _reportPath,
                JsonSerializer.Serialize(
                    report,
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        private static IReadOnlyList<PluginPageThreadSample> GetTopProcessorThreads(
            Process process)
        {
            var threads = new List<PluginPageThreadSample>();
            foreach (ProcessThread thread in process.Threads)
            {
                try
                {
                    threads.Add(new PluginPageThreadSample(
                        thread.Id,
                        Math.Round(
                            thread.TotalProcessorTime.TotalMilliseconds,
                            1)));
                }
                catch (Exception ex)
                    when (ex is InvalidOperationException or Win32Exception)
                {
                }
            }

            return threads
                .OrderByDescending(thread => thread.CpuMilliseconds)
                .Take(5)
                .ToArray();
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private sealed record PluginPagePerformanceSample(
            string Stage,
            double ElapsedMilliseconds,
            double CpuMilliseconds,
            double WorkingSetMegabytes,
            double PrivateMemoryMegabytes,
            double ManagedHeapMegabytes,
            double GcCommittedMegabytes,
            int ThreadCount,
            IReadOnlyList<PluginPageThreadSample> TopThreads,
            int? ItemCount,
            int? RealizedContainerCount,
            int? VisualDescendantCount,
            int? AnimatedPropertyCount);

        private sealed record PluginPageThreadSample(
            int Id,
            double CpuMilliseconds);
    }
}
