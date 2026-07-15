using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LongBetterWindows.Host.Services;
using System.Linq;
using System;

namespace LongBetterWindows.Host.Views
{
    public partial class CapabilityDetailPanel : UserControl
    {
        public CapabilityDetailPanel()
        {
            InitializeComponent();
        }

        public void LoadCapabilities(string pluginId, System.Collections.Generic.List<string> capabilities)
        {
            CapabilitiesPanel.Children.Clear();

            if (capabilities.Count == 0)
            {
                CapabilitiesPanel.Children.Add(new TextBlock
                {
                    Text = "该插件未声明任何权限",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                    Margin = new Thickness(0, 8, 0, 0),
                });
                StatsText.Text = "无 API 调用记录";
                return;
            }

            foreach (var cap in capabilities)
            {
                var info = CapabilityMetadata.GetInfo(cap);
                var item = CreateCapabilityItem(info, cap);
                CapabilitiesPanel.Children.Add(item);
            }

            // 加载统计信息
            var stats = CapabilityUsageTracker.Instance.GetStats(pluginId);
            if (stats != null && stats.TotalCalls > 0)
            {
                var lastCall = stats.LastCallTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未知";
                var topCalls = string.Join("\n", stats.CapabilityCalls
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(5)
                    .Select(kvp => $"  • {CapabilityMetadata.GetInfo(kvp.Key).DisplayName}: {kvp.Value} 次"));

                StatsText.Text = $"总调用次数: {stats.TotalCalls}\n最后调用: {lastCall}\n\n最常用权限:\n{topCalls}";
            }
            else
            {
                StatsText.Text = "暂无 API 调用记录";
            }
        }

        private Border CreateCapabilityItem(CapabilityInfo info, string capabilityKey)
        {
            var levelColor = info.Level switch
            {
                SecurityLevel.Safe => Color.FromRgb(0x10, 0xB9, 0x81),      // 绿色
                SecurityLevel.Medium => Color.FromRgb(0xF5, 0x9E, 0x0B),    // 黄色
                SecurityLevel.High => Color.FromRgb(0xEF, 0x44, 0x44),      // 红色
                _ => Color.FromRgb(0x6B, 0x72, 0x80)                        // 灰色
            };

            var levelText = info.Level switch
            {
                SecurityLevel.Safe => "安全",
                SecurityLevel.Medium => "中等",
                SecurityLevel.High => "高危",
                _ => "未知"
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 图标
            var icon = new TextBlock
            {
                Text = info.Icon,
                FontSize = 20,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(icon, 0);

            // 信息
            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = info.DisplayName,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = info.Description,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = capabilityKey,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                Margin = new Thickness(0, 4, 0, 0),
                FontFamily = new FontFamily("Consolas"),
            });
            Grid.SetColumn(infoStack, 1);

            // 安全等级标签
            var levelBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x20, levelColor.R, levelColor.G, levelColor.B)),
                BorderBrush = new SolidColorBrush(levelColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = levelText,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(levelColor),
                },
            };
            Grid.SetColumn(levelBadge, 2);

            grid.Children.Add(icon);
            grid.Children.Add(infoStack);
            grid.Children.Add(levelBadge);

            return new Border
            {
                Style = (Style)FindResource("CapItemStyle"),
                Child = grid,
            };
        }
    }
}
