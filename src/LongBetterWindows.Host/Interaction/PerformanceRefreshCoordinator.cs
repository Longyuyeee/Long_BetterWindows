using System.Windows.Threading;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Interaction
{
    internal sealed class PerformanceRefreshCoordinator : IDisposable
    {
        private readonly PerformanceMonitorService _monitor;
        private readonly DispatcherTimer _timer;

        public PerformanceRefreshCoordinator(
            PerformanceMonitorService? monitor = null,
            TimeSpan? refreshInterval = null)
        {
            _monitor = monitor ?? PerformanceMonitorService.Instance;
            _timer = new DispatcherTimer
            {
                Interval = refreshInterval ?? TimeSpan.FromSeconds(1),
            };
            _timer.Tick += Timer_Tick;
        }

        public event EventHandler<PerformanceSnapshot>? SnapshotAvailable;

        public void Start()
        {
            _monitor.StartMonitoring();
            _timer.Start();
            Refresh();
        }

        public void Stop()
        {
            _timer.Stop();
            _monitor.StopMonitoring();
        }

        public void Refresh()
        {
            SnapshotAvailable?.Invoke(this, new PerformanceSnapshot(
                _monitor.GetSystemSummary(),
                _monitor.GetPluginPerformanceData()));
        }

        public void Dispose()
        {
            Stop();
            _timer.Tick -= Timer_Tick;
        }

        private void Timer_Tick(object? sender, EventArgs e) => Refresh();
    }

    internal sealed record PerformanceSnapshot(
        SystemPerformanceSummary Summary,
        IReadOnlyList<PluginPerformanceData> Plugins);
}
