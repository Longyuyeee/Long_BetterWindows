using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace LongBetterWindows.Host.Views
{
    public partial class ToolCenterControl : UserControl
    {
        private static readonly SolidColorBrush GreenBrush =
            new(Color.FromRgb(0x34, 0xC7, 0x59));
        private static readonly SolidColorBrush GrayBrush =
            new(Color.FromRgb(0x99, 0x99, 0x99));
        private static readonly SolidColorBrush LightTextBrush =
            new(Color.FromRgb(0xE8, 0xE8, 0xE8));
        private static readonly SolidColorBrush CardBgBrush =
            new(Color.FromRgb(0x2D, 0x2D, 0x30));
        private static readonly SolidColorBrush BlueBrush =
            new(Color.FromRgb(0x00, 0x7A, 0xFF));
        private static readonly SolidColorBrush RedBrush =
            new(Color.FromRgb(0xFF, 0x3B, 0x30));

        private bool _columnEnabled;
        private bool _contextMenuRegistered;
        private bool _startupEnabled;

        public ToolCenterControl()
        {
            InitializeComponent();
            RefreshColumnStatus();
            RefreshContextMenuStatus();
            RefreshStartupStatus();
            RefreshDocLinks();
            RefreshPluginList();
        }

        private void DevTools_Click(object sender, RoutedEventArgs e)
        {
            PluginDevTools.Open(Window.GetWindow(this)!);
        }

        private static bool _isLightMode;
        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            _isLightMode = !_isLightMode;
            var bg = _isLightMode ? "#F5F5F7" : "#1E1F22";
            var window = Window.GetWindow(this);
            if (window != null)
                window.Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(bg));
            if (sender is Button btn)
                btn.Content = _isLightMode ? "暗色" : "亮色";
        }

        private void RefreshDocLinks()
        {
            DocLinksPanel.Children.Clear();

            // 查找 docs 目录：从 bin/Debug/net8.0-windows 向上 5 级到 repo 根
            var docsDir = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "docs"));

            if (!Directory.Exists(docsDir))
            {
                // 尝试直接在 BaseDirectory/docs
                docsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs");
            }

            if (!Directory.Exists(docsDir))
            {
                DocLinksPanel.Children.Add(new TextBlock
                {
                    Text = "文档目录未找到",
                    FontSize = 11,
                    Foreground = GrayBrush,
                });
                return;
            }

            var docFiles = Directory.GetFiles(docsDir, "*.md")
                .OrderBy(f => f)
                .ToList();

            foreach (var file in docFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file)
                    .Replace("设计文档_", "")
                    .Replace("第一部分_", "Part1 ")
                    .Replace("第二部分_", "Part2 ")
                    .Replace("第四部分_", "Part4 ")
                    .Replace("插件开发指南", "插件开发指南 ⭐")
                    .Replace("基础能力_API_手册", "API 手册");

                var link = new TextBlock
                {
                    Text = name,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF)),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 2, 0, 2),
                    Tag = file,
                };
                link.MouseLeftButtonDown += (_, _) =>
                {
                    var path = (string)link.Tag;
                    var content = File.ReadAllText(path);
                    DocViewer.ShowDoc(
                        Window.GetWindow(this)!,
                        Path.GetFileNameWithoutExtension(path),
                        content);
                };

                DocLinksPanel.Children.Add(link);
            }
        }

        private void StartupButton_Click(object sender, RoutedEventArgs e)
        {
            _startupEnabled = !_startupEnabled;
            ServicesInitializer.Startup.SetAutoStart(_startupEnabled);
            RefreshStartupStatus();
        }

        private void RefreshStartupStatus()
        {
            _startupEnabled = ServicesInitializer.Startup.IsAutoStartEnabled;
            StartupButton.Content = _startupEnabled ? "禁用" : "启用";

            if (_startupEnabled)
            {
                StartupStatusText.Text = "已启用";
                StartupStatusText.Foreground = GreenBrush;
            }
            else
            {
                StartupStatusText.Text = "未启用";
                StartupStatusText.Foreground = GrayBrush;
            }
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

            var header = new TextBlock
            {
                Text = "插件",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = GrayBrush,
                Margin = new Thickness(0, 4, 0, 12),
            };
            PluginsPanel.Children.Add(header);

            if (plugins.Count == 0)
            {
                PluginsPanel.Visibility = Visibility.Visible;

                var emptyCard = new Border
                {
                    Background = CardBgBrush,
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(20, 20, 20, 20),
                    Child = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "暂无插件",
                                FontSize = 14,
                                FontWeight = FontWeights.Medium,
                                Foreground = BlueBrush,
                            },
                            new TextBlock
                            {
                                Text = "使用脚手架快速创建:",
                                FontSize = 11,
                                Foreground = GrayBrush,
                                Margin = new Thickness(0, 8, 0, 4),
                            },
                            new TextBlock
                            {
                                Text = ".\\new-plugin.ps1 -Name \"名称\" -Id \"com.example.id\"",
                                FontSize = 11,
                                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                                Foreground = LightTextBrush,
                                Margin = new Thickness(0, 2, 0, 0),
                            },
                            new TextBlock
                            {
                                Text = "模板: empty / hotkey / full",
                                FontSize = 10,
                                Foreground = GrayBrush,
                                Margin = new Thickness(0, 4, 0, 0),
                            },
                        },
                    },
                };

                PluginsPanel.Children.Add(emptyCard);
                return;
            }

            PluginsPanel.Visibility = Visibility.Visible;

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
                Foreground = LightTextBrush,
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

            // 右侧按钮区域
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // 插件有主 UI 时显示「打开」按钮
            if (plugin.Instance is IHasMainUI hasUI)
            {
                var openBtn = new Button
                {
                    Content = "打开",
                    Width = 48, Height = 24,
                    FontSize = 11,
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF)),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 6, 0),
                    Tag = hasUI,
                };
                openBtn.Click += (_, _) => hasUI.ShowMainUI();
                btnPanel.Children.Add(openBtn);
            }

            // 插件有自定义设置 UI 时显示齿轮按钮
            if (plugin.Instance is IHasSettingsUI)
            {
                var settingsBtn = new Button
                {
                    Content = "\u2699",
                    Width = 28, Height = 28,
                    FontSize = 14,
                    Foreground = GrayBrush,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 6, 0),
                    Tag = plugin,
                };
                settingsBtn.Click += PluginSettings_Click;
                btnPanel.Children.Add(settingsBtn);
            }

            var btnText = isRunning ? "禁用" : "启用";
            var btnBrush = isRunning ? RedBrush : GreenBrush;

            var toggleBtn = new Button
            {
                Content = btnText,
                Width = 56, Height = 28,
                FontSize = 11,
                Foreground = Brushes.White,
                Background = btnBrush,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Tag = new ToggleState { PluginId = plugin.Id, Version = version },
            };
            toggleBtn.Click += PluginToggle_Click;
            btnPanel.Children.Add(toggleBtn);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(infoStack, 0);
            Grid.SetColumn(btnPanel, 1);
            grid.Children.Add(infoStack);
            grid.Children.Add(btnPanel);

            return new Border
            {
                Background = CardBgBrush,
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

        private void PluginSettings_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not PluginEntry entry) return;
            if (entry.Instance is not IHasSettingsUI hasUI) return;

            var settingsUI = hasUI.CreateSettingsUI();
            if (settingsUI == null) return;

            var popup = new Window
            {
                Title = $"{entry.Manifest.Name} - 设置",
                Content = settingsUI,
                Width = 420,
                Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false,
            };

            popup.ShowDialog();
        }

        private class ToggleState
        {
            public string PluginId { get; init; } = string.Empty;
            public int Version { get; init; }
        }
    }
}
