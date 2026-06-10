using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views
{
    public partial class ToolCenterControl : UserControl
    {
        private static readonly SolidColorBrush GreenBrush =
            new(Color.FromRgb(0x34, 0xC7, 0x59));
        private static readonly SolidColorBrush GrayBrush =
            new(Color.FromRgb(0x80, 0x80, 0x80));

        private bool _columnEnabled;
        private bool _contextMenuRegistered;

        public ToolCenterControl()
        {
            InitializeComponent();
            RefreshColumnStatus();
            RefreshContextMenuStatus();
            RefreshPluginList();
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

        private int _pluginCardVersion;

        private void RefreshPluginList()
        {
            _pluginCardVersion++;
            var version = _pluginCardVersion;

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
                Text = "插件",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = GrayBrush,
                Margin = new Thickness(0, 4, 0, 12),
            };
            PluginsPanel.Children.Add(header);

            foreach (var plugin in plugins)
            {
                PluginsPanel.Children.Add(CreatePluginCard(plugin, version));
            }
        }

        private Border CreatePluginCard(PluginEntry plugin, int version)
        {
            var isRunning = plugin.State == Core.PluginState.Running;
            var stateColor = isRunning ? GreenBrush : GrayBrush;
            var stateText = isRunning ? "运行中" : "已停止";

            var hotkey = PluginRegistry.GetPluginHotkey(plugin);
            var capText = plugin.Manifest.Capabilities.Count > 0
                ? string.Join(", ", plugin.Manifest.Capabilities) : "无";

            // 左侧信息区
            var infoStack = new StackPanel();

            // 第一行: 状态点 + 名称
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(new Ellipse
            {
                Width = 8, Height = 8,
                Fill = stateColor,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            nameRow.Children.Add(new TextBlock
            {
                Text = plugin.Manifest.Name,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
            });
            infoStack.Children.Add(nameRow);

            // 第二行: 快捷键 + 状态
            var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };

            if (hotkey != null)
            {
                var hotkeyBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x15, 0x00, 0x7A, 0xFF)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 8, 0),
                    Child = new TextBlock
                    {
                        Text = hotkey,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF)),
                    },
                };
                metaRow.Children.Add(hotkeyBadge);
            }

            metaRow.Children.Add(new TextBlock
            {
                Text = $"{stateText} · v{plugin.Manifest.Version}",
                FontSize = 11,
                Foreground = GrayBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });
            infoStack.Children.Add(metaRow);

            // 右侧按钮
            var btnText = isRunning ? "禁用" : "启用";
            var btnBrush = isRunning
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30))
                : GreenBrush;

            var toggleBtn = new Button
            {
                Content = btnText,
                Width = 56, Height = 28,
                FontSize = 11,
                Foreground = Brushes.White,
                Background = btnBrush,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = new ToggleState { PluginId = plugin.Id, Version = version },
            };
            toggleBtn.Click += PluginToggle_Click;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(infoStack, 0);
            Grid.SetColumn(toggleBtn, 1);
            grid.Children.Add(infoStack);
            grid.Children.Add(toggleBtn);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x10, 0x00, 0x00, 0x00)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Child = grid,
            };
        }

        private async void PluginToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ToggleState state) return;
            btn.IsEnabled = false;

            var registry = HostProvider.Instance.PluginStore;
            var entry = registry.Get(state.PluginId);

            if (entry == null)
            {
                btn.IsEnabled = true;
                return;
            }

            if (entry.State == Core.PluginState.Running)
            {
                await registry.StopPluginAsync(state.PluginId);
            }
            else
            {
                await registry.StartPluginAsync(state.PluginId);
            }

            btn.IsEnabled = true;
            RefreshPluginList();
        }

        private class ToggleState
        {
            public string PluginId { get; init; } = string.Empty;
            public int Version { get; init; }
        }
    }
}
