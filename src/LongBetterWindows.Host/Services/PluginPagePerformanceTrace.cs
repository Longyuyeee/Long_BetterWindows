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
        private string _idleMode = "visible";

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

        public void SetIdleMode(string idleMode)
        {
            if (string.IsNullOrWhiteSpace(idleMode))
                throw new ArgumentException("Idle mode is required.", nameof(idleMode));
            _idleMode = idleMode;
        }

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
                schema_version = 2,
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
                idle_mode = _idleMode,
                logical_processor_count = Environment.ProcessorCount,
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
                window_message_intervals = CreateWindowMessageIntervals(
                    _windowMessageCheckpoints),
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
                sample_intervals = CreateSampleIntervals(samples),
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_reportPath)!);
            await File.WriteAllTextAsync(
                _reportPath,
                JsonSerializer.Serialize(
                    report,
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        private static IReadOnlyList<object> CreateSampleIntervals(
            IReadOnlyList<PluginPagePerformanceSample> samples)
        {
            var intervals = new List<object>(Math.Max(0, samples.Count - 1));
            for (var index = 1; index < samples.Count; index++)
            {
                var previous = samples[index - 1];
                var current = samples[index];
                var elapsed = Math.Max(
                    0,
                    current.ElapsedMilliseconds - previous.ElapsedMilliseconds);
                var cpu = Math.Max(
                    0,
                    current.CpuMilliseconds - previous.CpuMilliseconds);
                intervals.Add(new
                {
                    from_stage = previous.Stage,
                    to_stage = current.Stage,
                    elapsed_ms = Math.Round(elapsed, 1),
                    cpu_ms = Math.Round(cpu, 1),
                    cpu_core_percent = elapsed > 0
                        ? Math.Round(cpu / elapsed * 100, 2)
                        : 0,
                    cpu_machine_percent = elapsed > 0
                        ? Math.Round(
                            cpu / elapsed / Environment.ProcessorCount * 100,
                            2)
                        : 0,
                    working_set_delta_mb = Math.Round(
                        current.WorkingSetMegabytes
                            - previous.WorkingSetMegabytes,
                        1),
                    private_memory_delta_mb = Math.Round(
                        current.PrivateMemoryMegabytes
                            - previous.PrivateMemoryMegabytes,
                        1),
                    managed_heap_delta_mb = Math.Round(
                        current.ManagedHeapMegabytes
                            - previous.ManagedHeapMegabytes,
                        1),
                    thread_count_delta = current.ThreadCount - previous.ThreadCount,
                });
            }
            return intervals;
        }

        private static IReadOnlyList<object> CreateWindowMessageIntervals(
            IReadOnlyList<WindowMessageCheckpoint> checkpoints)
        {
            var intervals = new List<object>(Math.Max(0, checkpoints.Count - 1));
            for (var index = 1; index < checkpoints.Count; index++)
            {
                var previous = checkpoints[index - 1];
                var current = checkpoints[index];
                intervals.Add(new
                {
                    from_stage = previous.Stage,
                    to_stage = current.Stage,
                    total_count_delta = Math.Max(
                        0,
                        current.TotalCount - previous.TotalCount),
                });
            }
            return intervals;
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
