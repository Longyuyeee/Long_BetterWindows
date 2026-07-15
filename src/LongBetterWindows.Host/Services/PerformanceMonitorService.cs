using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LongBetterWindows.Host.Services
{
    /// <summary>
    /// 性能监控服务 - 监控 CPU/内存/API 调用
    /// </summary>
    public class PerformanceMonitorService
    {
        private static readonly PerformanceMonitorService _instance = new();
        public static PerformanceMonitorService Instance => _instance;

        private readonly Dictionary<string, PluginPerformanceData> _pluginData = new();
        private readonly object _lock = new();
        private readonly Process _currentProcess;
        private Timer? _monitorTimer;

        // 性能历史记录（最近 60 个数据点）
        private readonly Queue<double> _cpuHistory = new();
        private readonly Queue<double> _memoryHistory = new();
        private const int MaxHistoryPoints = 60;

        private PerformanceMonitorService()
        {
            _currentProcess = Process.GetCurrentProcess();
        }

        /// <summary>
        /// 启动性能监控
        /// </summary>
        public void StartMonitoring()
        {
            _monitorTimer ??= new Timer(_ => CollectMetrics(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// 停止性能监控
        /// </summary>
        public void StopMonitoring()
        {
            _monitorTimer?.Dispose();
            _monitorTimer = null;
        }

        /// <summary>
        /// 收集系统指标
        /// </summary>
        private void CollectMetrics()
        {
            try
            {
                // CPU 使用率
                var cpuPercent = GetCpuUsage();
                lock (_lock)
                {
                    _cpuHistory.Enqueue(cpuPercent);
                    if (_cpuHistory.Count > MaxHistoryPoints)
                        _cpuHistory.Dequeue();
                }

                // 内存使用
                var memoryMB = _currentProcess.WorkingSet64 / 1024.0 / 1024.0;
                lock (_lock)
                {
                    _memoryHistory.Enqueue(memoryMB);
                    if (_memoryHistory.Count > MaxHistoryPoints)
                        _memoryHistory.Dequeue();
                }
            }
            catch
            {
                // 忽略采集错误
            }
        }

        private DateTime _lastCpuTime = DateTime.MinValue;
        private TimeSpan _lastTotalProcessorTime = TimeSpan.Zero;

        /// <summary>
        /// 获取 CPU 使用率
        /// </summary>
        private double GetCpuUsage()
        {
            var now = DateTime.UtcNow;
            var currentTotalProcessorTime = _currentProcess.TotalProcessorTime;

            if (_lastCpuTime == DateTime.MinValue)
            {
                _lastCpuTime = now;
                _lastTotalProcessorTime = currentTotalProcessorTime;
                return 0;
            }

            var cpuUsedMs = (currentTotalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;
            var totalMsPassed = (now - _lastCpuTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

            _lastCpuTime = now;
            _lastTotalProcessorTime = currentTotalProcessorTime;

            return cpuUsageTotal * 100;
        }

        /// <summary>
        /// 记录 API 调用
        /// </summary>
        public void RecordApiCall(string pluginId, string apiName, TimeSpan duration)
        {
            lock (_lock)
            {
                if (!_pluginData.ContainsKey(pluginId))
                {
                    _pluginData[pluginId] = new PluginPerformanceData { PluginId = pluginId };
                }

                var data = _pluginData[pluginId];
                data.TotalApiCalls++;
                data.TotalApiDuration += duration;

                if (!data.ApiCallCounts.ContainsKey(apiName))
                    data.ApiCallCounts[apiName] = 0;

                data.ApiCallCounts[apiName]++;
            }
        }

        /// <summary>
        /// 获取系统性能摘要
        /// </summary>
        public SystemPerformanceSummary GetSystemSummary()
        {
            lock (_lock)
            {
                return new SystemPerformanceSummary
                {
                    CurrentCpuPercent = _cpuHistory.Count > 0 ? _cpuHistory.Last() : 0,
                    AverageCpuPercent = _cpuHistory.Count > 0 ? _cpuHistory.Average() : 0,
                    CurrentMemoryMB = _memoryHistory.Count > 0 ? _memoryHistory.Last() : 0,
                    CpuHistory = _cpuHistory.ToList(),
                    MemoryHistory = _memoryHistory.ToList(),
                    TotalApiCalls = _pluginData.Values.Sum(p => p.TotalApiCalls),
                    PluginCount = _pluginData.Count
                };
            }
        }

        /// <summary>
        /// 获取所有插件的性能数据
        /// </summary>
        public List<PluginPerformanceData> GetPluginPerformanceData()
        {
            lock (_lock)
            {
                return _pluginData.Values.OrderByDescending(p => p.TotalApiCalls).ToList();
            }
        }

        /// <summary>
        /// 获取单个插件的性能数据
        /// </summary>
        public PluginPerformanceData? GetPluginData(string pluginId)
        {
            lock (_lock)
            {
                return _pluginData.TryGetValue(pluginId, out var data) ? data : null;
            }
        }

        /// <summary>
        /// 重置所有统计数据
        /// </summary>
        public void ResetStatistics()
        {
            lock (_lock)
            {
                _pluginData.Clear();
                _cpuHistory.Clear();
                _memoryHistory.Clear();
            }
        }
    }

    /// <summary>
    /// 系统性能摘要
    /// </summary>
    public class SystemPerformanceSummary
    {
        public double CurrentCpuPercent { get; set; }
        public double AverageCpuPercent { get; set; }
        public double CurrentMemoryMB { get; set; }
        public List<double> CpuHistory { get; set; } = new();
        public List<double> MemoryHistory { get; set; } = new();
        public int TotalApiCalls { get; set; }
        public int PluginCount { get; set; }
    }

    /// <summary>
    /// 插件性能数据
    /// </summary>
    public class PluginPerformanceData
    {
        public string PluginId { get; set; } = "";
        public int TotalApiCalls { get; set; }
        public TimeSpan TotalApiDuration { get; set; }
        public Dictionary<string, int> ApiCallCounts { get; set; } = new();

        public double AverageApiDurationMs =>
            TotalApiCalls > 0 ? TotalApiDuration.TotalMilliseconds / TotalApiCalls : 0;
    }
}
