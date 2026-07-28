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

        internal void ApplyLanguage()
        {
            if (string.IsNullOrWhiteSpace(_pluginId))
                return;
            LoadCapabilities(
                _pluginId,
                _pluginName,
                _capabilities);
        }

        private string _pluginId = string.Empty;
        private string _pluginName = string.Empty;
        private IReadOnlyList<string> _capabilities = [];

        public void LoadCapabilities(
            string pluginId,
            string pluginName,
            IReadOnlyList<string> capabilities)
        {
            _pluginId = pluginId;
            _pluginName = pluginName;
            _capabilities = capabilities.ToArray();
            PluginNameText.Text = pluginName;
            PermissionSummaryText.Text = capabilities.Count == 0
                ? Text("plugins.capabilities.none", "No host capabilities required")
                : string.Format(
                    Text(
                        "plugins.capabilities.count",
                        "{0} host capabilities declared"),
                    capabilities.Count);

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
            NoCapabilitiesText.Text = Text(
                "plugins.capabilities.empty",
                "This plugin declares no host capabilities.");
            NoCapabilitiesText.Visibility = items.Length == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            StatsText.Text = BuildUsageSummary(pluginId);
        }

        private string BuildUsageSummary(string pluginId)
        {
            var stats = CapabilityUsageTracker.Instance.GetStatsSnapshot(pluginId);
            if (stats is null || stats.TotalCalls == 0)
                return Text(
                    "plugins.usage.empty",
                    "No capability calls in this host session.");

            var lastCall = stats.LastCallTime?.ToString("yyyy-MM-dd HH:mm:ss")
                ?? Text("plugins.usage.unknown", "Unknown");
            var topCalls = string.Join("\n", stats.CapabilityCalls
                .OrderByDescending(item => item.Value)
                .Take(5)
                .Select(item => string.Format(
                    Text("plugins.usage.item", "{0} · {1} calls"),
                    CapabilityMetadata.GetInfo(item.Key).DisplayName,
                    item.Value)));
            return string.Format(
                Text(
                    "plugins.usage.summary",
                    "{0} total calls\nLast call {1}\n\n{2}"),
                stats.TotalCalls,
                lastCall,
                topCalls);
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

        private string GetLevelText(SecurityLevel level) => level switch
        {
            SecurityLevel.Safe => Text("plugins.capabilities.level.safe", "Standard"),
            SecurityLevel.Medium => Text("plugins.capabilities.level.medium", "Sensitive"),
            SecurityLevel.High => Text("plugins.capabilities.level.high", "High privilege"),
            _ => Text("plugins.capabilities.level.unknown", "Unknown"),
        };

        private static string Text(string key, string fallback)
        {
            var value = ServicesInitializer.I18n.T(key);
            return string.IsNullOrWhiteSpace(value) || value == key
                ? fallback
                : value;
        }

        private sealed record CapabilityDetailItem(
            string CapabilityKey,
            string DisplayName,
            string Description,
            string LevelText,
            Brush LevelBrush);
    }
}
