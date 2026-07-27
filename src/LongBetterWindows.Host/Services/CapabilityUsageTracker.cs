using System.Collections.Generic;

namespace LongBetterWindows.Host.Services
{
    /// <summary>
    /// 权限使用统计服务
    /// </summary>
    public class CapabilityUsageTracker
    {
        private static readonly CapabilityUsageTracker _instance = new();
        public static CapabilityUsageTracker Instance => _instance;

        private readonly Dictionary<string, PluginUsageStats> _usageByPlugin = new();
        private readonly object _lock = new();

        private CapabilityUsageTracker() { }

        /// <summary>
        /// 记录 API 调用
        /// </summary>
        public void RecordApiCall(string pluginId, string capability, string apiMethod)
        {
            lock (_lock)
            {
                if (!_usageByPlugin.TryGetValue(pluginId, out var stats))
                {
                    stats = new PluginUsageStats { PluginId = pluginId };
                    _usageByPlugin[pluginId] = stats;
                }

                if (!stats.CapabilityCalls.TryGetValue(capability, out var count))
                {
                    count = 0;
                }
                stats.CapabilityCalls[capability] = count + 1;

                if (!stats.ApiMethodCalls.TryGetValue(apiMethod, out var methodCount))
                {
                    methodCount = 0;
                }
                stats.ApiMethodCalls[apiMethod] = methodCount + 1;

                stats.TotalCalls++;
                stats.LastCallTime = DateTime.Now;
            }
        }

        /// <summary>
        /// 获取插件的使用统计
        /// </summary>
        public PluginUsageStats? GetStats(string pluginId)
        {
            lock (_lock)
            {
                return _usageByPlugin.TryGetValue(pluginId, out var stats) ? stats : null;
            }
        }

        /// <summary>获取不会被后台调用继续修改的统计快照。</summary>
        internal PluginUsageStats? GetStatsSnapshot(string pluginId)
        {
            lock (_lock)
            {
                if (!_usageByPlugin.TryGetValue(pluginId, out var stats))
                    return null;

                var snapshot = new PluginUsageStats
                {
                    PluginId = stats.PluginId,
                    TotalCalls = stats.TotalCalls,
                    LastCallTime = stats.LastCallTime,
                };
                foreach (var item in stats.CapabilityCalls)
                    snapshot.CapabilityCalls[item.Key] = item.Value;
                foreach (var item in stats.ApiMethodCalls)
                    snapshot.ApiMethodCalls[item.Key] = item.Value;
                return snapshot;
            }
        }

        /// <summary>
        /// 清除插件的统计数据
        /// </summary>
        public void ClearStats(string pluginId)
        {
            lock (_lock)
            {
                _usageByPlugin.Remove(pluginId);
            }
        }

        /// <summary>
        /// 获取所有插件的统计
        /// </summary>
        public Dictionary<string, PluginUsageStats> GetAllStats()
        {
            lock (_lock)
            {
                return new Dictionary<string, PluginUsageStats>(_usageByPlugin);
            }
        }
    }

    /// <summary>
    /// 插件使用统计
    /// </summary>
    public class PluginUsageStats
    {
        public string PluginId { get; init; } = string.Empty;
        public int TotalCalls { get; set; }
        public DateTime? LastCallTime { get; set; }
        public Dictionary<string, int> CapabilityCalls { get; } = new();
        public Dictionary<string, int> ApiMethodCalls { get; } = new();
    }
}
