using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;
using LongBetterWindows.Host.Tools;

namespace LongBetterWindows.Host.Views
{
    public partial class ToolCenterControl : UserControl
    {
        private readonly FolderNoteTool _folderNoteTool = new();
        private bool _columnEnabled;
        private readonly DispatcherTimer _pluginRefreshTimer;

        public ToolCenterControl()
        {
            InitializeComponent();
            UpdateUI();
            RefreshColumnStatus();
            RefreshPluginList();

            _pluginRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2),
            };
            _pluginRefreshTimer.Tick += (_, _) => RefreshPluginList();
            _pluginRefreshTimer.Start();
        }

        private async void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleButton.IsEnabled = false;

            if (_folderNoteTool.IsEnabled)
            {
                await _folderNoteTool.DisableAsync();
            }
            else
            {
                await _folderNoteTool.EnableAsync();
            }

            UpdateUI();
            ToggleButton.IsEnabled = true;
        }

        private async void ColumnButton_Click(object sender, RoutedEventArgs e)
        {
            ColumnButton.IsEnabled = false;
            ColumnStatusText.Text = "处理中...";

            if (_columnEnabled)
            {
                var result = await ServicesInitializer.ColumnInjection
                    .DisableCommentColumnAsync();

                if (result.IsSuccess)
                {
                    _columnEnabled = false;
                    ColumnStatusText.Text = "备注列已移除";
                }
                else
                {
                    ColumnStatusText.Text = "移除失败";
                }
            }
            else
            {
                var result = await ServicesInitializer.ColumnInjection
                    .EnableCommentColumnAsync();

                if (result.IsSuccess)
                {
                    _columnEnabled = true;
                    ColumnStatusText.Text = "备注列已启用 · Explorer 已刷新";
                    ColumnStatusText.Foreground = new SolidColorBrush(
                        Color.FromRgb(0x34, 0xC7, 0x59));
                }
                else
                {
                    ColumnStatusText.Text = "注入失败";
                }
            }

            ColumnButton.Content = _columnEnabled ? "移除" : "一键开启";
            ColumnButton.IsEnabled = true;
        }

        private void RefreshColumnStatus()
        {
            _columnEnabled = ServicesInitializer.ColumnInjection.IsCommentColumnEnabled;

            ColumnButton.Content = _columnEnabled ? "移除" : "一键开启";

            if (_columnEnabled)
            {
                ColumnStatusText.Text = "已启用";
                ColumnStatusText.Foreground = new SolidColorBrush(
                    Color.FromRgb(0x34, 0xC7, 0x59));
            }
            else
            {
                ColumnStatusText.Text = "未启用";
                ColumnStatusText.Foreground = new SolidColorBrush(
                    Color.FromRgb(0x80, 0x80, 0x80));
            }
        }

        private void RefreshPluginList()
        {
            PluginsPanel.Children.Clear();

            var plugins = HostProvider.Instance.PluginStore.GetAll();

            if (plugins.Count == 0)
            {
                PluginsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            PluginsPanel.Visibility = Visibility.Visible;

            var header = new TextBlock
            {
                Text = "已加载插件",
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 12),
            };
            PluginsPanel.Children.Add(header);

            foreach (var plugin in plugins)
            {
                var card = CreatePluginCard(plugin);
                PluginsPanel.Children.Add(card);
            }
        }

        private Border CreatePluginCard(LongBetterWindows.Host.Engine.PluginEntry plugin)
        {
            var stateColor = plugin.State switch
            {
                LongBetterWindows.Host.Core.PluginState.Running => Color.FromRgb(0x34, 0xC7, 0x59),
                LongBetterWindows.Host.Core.PluginState.Error => Color.FromRgb(0xFF, 0x3B, 0x30),
                _ => Color.FromRgb(0x80, 0x80, 0x80),
            };

            var stateText = plugin.State switch
            {
                LongBetterWindows.Host.Core.PluginState.Running => "运行中",
                LongBetterWindows.Host.Core.PluginState.Loaded => "已加载",
                LongBetterWindows.Host.Core.PluginState.Error => "错误",
                LongBetterWindows.Host.Core.PluginState.Disabled => "已禁用",
                _ => "未知",
            };

            var capabilitiesText = plugin.Manifest.Capabilities.Count > 0
                ? string.Join(", ", plugin.Manifest.Capabilities)
                : "无";

            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = $"{plugin.Manifest.Name} ({plugin.Manifest.Id})",
                FontSize = 12,
                FontWeight = FontWeights.Medium,
            });

            stack.Children.Add(new TextBlock
            {
                Text = $"v{plugin.Manifest.Version} · 能力: {capabilitiesText}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                Margin = new Thickness(0, 2, 0, 4),
            });

            var stateBlock = new TextBlock
            {
                Text = stateText,
                FontSize = 11,
                Foreground = new SolidColorBrush(stateColor),
            };
            stack.Children.Add(stateBlock);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x10, 0x00, 0x00, 0x00)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 6),
                Child = stack,
            };
        }

        private void UpdateUI()
        {
            if (_folderNoteTool.IsEnabled)
            {
                ToggleButton.Content = "禁用";
                StatusText.Text = "已启用 · 热键 Alt+M";
                StatusText.Foreground = new SolidColorBrush(
                    Color.FromRgb(0x34, 0xC7, 0x59));
            }
            else
            {
                ToggleButton.Content = "启用";
                StatusText.Text = "未启用";
                StatusText.Foreground = new SolidColorBrush(
                    Color.FromRgb(0x80, 0x80, 0x80));
            }
        }
    }
}
