using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LongBetterWindows.Host.Services
{
    internal sealed class StartupPerformanceTrace
    {
#if DEBUG
        private const string BuildConfiguration = "Debug";
#else
        private const string BuildConfiguration = "Release";
#endif
        private readonly object _sync = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly List<StartupPerformanceSample> _samples = new();
        private readonly string _reportPath;

        public StartupPerformanceTrace(string reportPath)
        {
            _reportPath = Path.GetFullPath(reportPath);
        }

        public void Mark(string stage)
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var sample = new StartupPerformanceSample(
                stage,
                Math.Round(_stopwatch.Elapsed.TotalMilliseconds, 1),
                Math.Round(process.TotalProcessorTime.TotalMilliseconds, 1),
                Math.Round(process.WorkingSet64 / 1024d / 1024d, 1),
                Math.Round(process.PrivateMemorySize64 / 1024d / 1024d, 1),
                Math.Round(GC.GetTotalMemory(forceFullCollection: false) / 1024d / 1024d, 1),
                process.Threads.Count);

            lock (_sync)
                _samples.Add(sample);
        }

        public async Task WriteAsync(PluginRuntimeStartResult result)
        {
            StartupPerformanceSample[] samples;
            lock (_sync)
                samples = _samples.ToArray();

            var report = new
            {
                schema_version = 1,
                captured_at = DateTimeOffset.UtcNow,
                configuration = BuildConfiguration,
                process_architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                loaded_plugin_count = result.LoadedPluginCount,
                recovered_transaction_count = result.RecoveredTransactionCount,
                installed_package_count = result.InstalledPackageCount,
                samples = samples.Select(sample => new
                {
                    stage = sample.Stage,
                    elapsed_ms = sample.ElapsedMilliseconds,
                    cpu_ms = sample.CpuMilliseconds,
                    working_set_mb = sample.WorkingSetMegabytes,
                    private_memory_mb = sample.PrivateMemoryMegabytes,
                    managed_heap_mb = sample.ManagedHeapMegabytes,
                    thread_count = sample.ThreadCount,
                }),
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_reportPath)!);
            await File.WriteAllTextAsync(
                _reportPath,
                JsonSerializer.Serialize(
                    report,
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        private sealed record StartupPerformanceSample(
            string Stage,
            double ElapsedMilliseconds,
            double CpuMilliseconds,
            double WorkingSetMegabytes,
            double PrivateMemoryMegabytes,
            double ManagedHeapMegabytes,
            int ThreadCount);
    }
}
