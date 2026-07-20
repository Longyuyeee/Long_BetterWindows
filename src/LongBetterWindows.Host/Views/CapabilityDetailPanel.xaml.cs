using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views
{
    public partial class CapabilityDetailPanel : UserControl
    {
        public CapabilityDetailPanel()
        {
            InitializeComponent();
        }

        public void LoadCapabilities(
            string pluginId,
            string pluginName,
            IReadOnlyList<string> capabilities)
        {
            PluginNameText.Text = pluginName;
            PermissionSummaryText.Text = capabilities.Count == 0
                ? "无需访问宿主能力"
                : $"已声明 {capabilities.Count} 项宿主能力";

            var items = capabilities.Select(capability =>
            {
                var info = CapabilityMetadata.GetInfo(capability);
                return new CapabilityDetailItem(
                    capability,
                    info.DisplayName,
                    info.Description,
                    GetLevelText(info.Level),
                    GetLevelBrush(info.Level));
            }).ToArray();

            CapabilityItems.ItemsSource = items;
            NoCapabilitiesText.Visibility = items.Length == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            StatsText.Text = BuildUsageSummary(pluginId);
        }

        private string BuildUsageSummary(string pluginId)
        {
            var stats = CapabilityUsageTracker.Instance.GetStats(pluginId);
            if (stats is null || stats.TotalCalls == 0)
                return "本次宿主会话暂无能力调用记录。";

            var lastCall = stats.LastCallTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未知";
            var topCalls = string.Join("\n", stats.CapabilityCalls
                .OrderByDescending(item => item.Value)
                .Take(5)
                .Select(item => $"{CapabilityMetadata.GetInfo(item.Key).DisplayName} · {item.Value} 次"));
            return $"总调用 {stats.TotalCalls} 次\n最后调用 {lastCall}\n\n{topCalls}";
        }

        private Brush GetLevelBrush(SecurityLevel level)
        {
            var key = level switch
            {
                SecurityLevel.Safe => "Long.Brush.State.Success",
                SecurityLevel.Medium => "Long.Brush.State.Warning",
                SecurityLevel.High => "Long.Brush.State.Danger",
                _ => "Long.Brush.Text.Muted",
            };
            return (Brush)FindResource(key);
        }

        private static string GetLevelText(SecurityLevel level) => level switch
        {
            SecurityLevel.Safe => "常规",
            SecurityLevel.Medium => "敏感",
            SecurityLevel.High => "高权限",
            _ => "未知",
        };

        private sealed record CapabilityDetailItem(
            string CapabilityKey,
            string DisplayName,
            string Description,
            string LevelText,
            Brush LevelBrush);
    }
}
