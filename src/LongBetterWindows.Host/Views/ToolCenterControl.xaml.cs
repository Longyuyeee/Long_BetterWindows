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
        private static readonly SolidColorBrush GreenBrush =
            new(Color.FromRgb(0x34, 0xC7, 0x59));
        private static readonly SolidColorBrush GrayBrush =
            new(Color.FromRgb(0x80, 0x80, 0x80));

        private readonly FolderNoteTool _folderNoteTool = new();
        private bool _columnEnabled;
        private bool _contextMenuRegistered;
        private readonly DispatcherTimer _pluginRefreshTimer;

        public ToolCenterControl()
        {
            InitializeComponent();
            UpdateUI();
            RefreshColumnStatus();
            RefreshContextMenuStatus();
            RefreshPluginList();

            _pluginRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3),
            };
            _pluginRefreshTimer.Tick += (_, _) => RefreshPluginList();
            _pluginRefreshTimer.Start();
        }

        private async void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleButton.IsEnabled = false;

            if (_folderNoteTool.IsEnabled)
                await _folderNoteTool.DisableAsync();
            else
                await _folderNoteTool.EnableAsync();

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
                    ColumnStatusText.Foreground = GrayBrush;
                }
                else
                    ColumnStatusText.Text = "移除失败";
            }
            else
            {
                var result = await ServicesInitializer.ColumnInjection
                    .EnableCommentColumnAsync();
                if (result.IsSuccess)
                {
                    _columnEnabled = true;
                    ColumnStatusText.Text = "备注列已启用 · Explorer 已刷新";
                    ColumnStatusText.Foreground = GreenBrush;
                }
                else
                    ColumnStatusText.Text = "注入失败";
            }

            ColumnButton.Content = _columnEnabled ? "移除" : "一键开启";
            ColumnButton.IsEnabled = true;
        }

        private async void ContextMenuButton_Click(object sender, RoutedEventArgs e)
        {
            ContextMenuButton.IsEnabled = false;
            ContextMenuStatusText.Text = "处理中...";

            if (_contextMenuRegistered)
            {
                var result = await ServicesInitializer.ContextMenu.UnregisterAsync();
                if (result.IsSuccess)
                {
                    _contextMenuRegistered = false;
                    ContextMenuStatusText.Text = "已移除";
                    ContextMenuStatusText.Foreground = GrayBrush;
                }
                else
                    ContextMenuStatusText.Text = "移除失败";
            }
            else
            {
                var result = await ServicesInitializer.ContextMenu.RegisterAsync();
                if (result.IsSuccess)
                {
                    _contextMenuRegistered = true;
                    ContextMenuStatusText.Text = "已注册 · 右键文件夹即可使用";
                    ContextMenuStatusText.Foreground = GreenBrush;
                }
                else
                    ContextMenuStatusText.Text = "注册失败";
            }

            ContextMenuButton.Content = _contextMenuRegistered ? "移除" : "注册";
            ContextMenuButton.IsEnabled = true;
        }

        private void RefreshColumnStatus()
        {
            _columnEnabled = ServicesInitializer.ColumnInjection.IsCommentColumnEnabled;
            ColumnButton.Content = _columnEnabled ? "移除" : "一键开启";

            if (_columnEnabled)
            {
                ColumnStatusText.Text = "已启用";
                ColumnStatusText.Foreground = GreenBrush;
            }
            else
            {
                ColumnStatusText.Text = "未启用";
                ColumnStatusText.Foreground = GrayBrush;
            }
        }

        private void RefreshContextMenuStatus()
        {
            _contextMenuRegistered = ServicesInitializer.ContextMenu.IsRegistered;
            ContextMenuButton.Content = _contextMenuRegistered ? "移除" : "注册";

            if (_contextMenuRegistered)
            {
                ContextMenuStatusText.Text = "已注册";
                ContextMenuStatusText.Foreground = GreenBrush;
            }
            else
            {
                ContextMenuStatusText.Text = "未注册";
                ContextMenuStatusText.Foreground = GrayBrush;
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
                Text = "外部插件",
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 12),
            };
            PluginsPanel.Children.Add(header);

            foreach (var plugin in plugins)
            {
                PluginsPanel.Children.Add(CreatePluginCard(plugin));
            }
        }

        private static Border CreatePluginCard(PluginEntry plugin)
        {
            var stateColor = plugin.State switch
            {
                Core.PluginState.Running => GreenBrush,
                Core.PluginState.Error => new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30)),
                _ => GrayBrush,
            };

            var stateText = plugin.State switch
            {
                Core.PluginState.Running => "运行中",
                Core.PluginState.Loaded => "已加载",
                Core.PluginState.Error => "错误",
                Core.PluginState.Disabled => "已禁用",
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
                Foreground = GrayBrush,
                Margin = new Thickness(0, 2, 0, 4),
            });

            stack.Children.Add(new TextBlock
            {
                Text = stateText,
                FontSize = 11,
                Foreground = stateColor,
            });

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
                StatusText.Foreground = GreenBrush;
            }
            else
            {
                ToggleButton.Content = "启用";
                StatusText.Text = "未启用";
                StatusText.Foreground = GrayBrush;
            }
        }
    }
}
