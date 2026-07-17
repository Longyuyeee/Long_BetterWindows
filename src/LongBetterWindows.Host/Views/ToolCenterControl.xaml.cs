using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LongBetterWindows.Host.Controls;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace LongBetterWindows.Host.Views
{
    public partial class ToolCenterControl : UserControl
    {
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

            // 默认激活「系统」标签
            ActivateTab(TabSystem);

            // 同步主题按钮状态
            var currentTheme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
            _isLightMode = currentTheme == Wpf.Ui.Appearance.ApplicationTheme.Light;

            // 首次运行显示欢迎横幅
            if (App.IsFirstRun())
                WelcomeBanner.Visibility = Visibility.Visible;

            // 动态更新关于信息
            UpdateAboutInfo();

            // 订阅插件变化事件，自动刷新列表
            HostProvider.Instance.PluginStore.PluginsChanged += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    RefreshPluginList();
                    UpdateAboutInfo();
                });
            };

            // 启动时检查插件更新
            CheckPluginUpdatesAsync();
        }

        private async void CheckPluginUpdatesAsync()
        {
            try
            {
                var updates = await PluginUpdateService.CheckUpdatesAsync();
                if (updates.Count > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        // 显示更新提示横幅
                        ShowUpdateBanner(updates.Count);
                    });
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "检查插件更新失败");
            }
        }

        private void ShowUpdateBanner(int updateCount)
        {
            // 创建更新提示横幅（在插件列表顶部）
            if (PluginsPanel.Children.Count > 0 &&
                PluginsPanel.Children[0] is Border banner &&
                banner.Tag?.ToString() == "UpdateBanner")
            {
                return; // 已存在，不重复显示
            }

            var updateBanner = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 16),
                Tag = "UpdateBanner"
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            var icon = new TextBlock
            {
                Text = "🔄",
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var text = new TextBlock
            {
                Text = $"发现 {updateCount} 个插件更新可用",
                Foreground = Brushes.White,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };

            var updateBtn = new Button
            {
                Content = "查看更新",
                Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 4, 12, 4),
                Cursor = Cursors.Hand,
                FontSize = 13
            };
            updateBtn.Click += ShowUpdateDialog_Click;

            var closeBtn = new Button
            {
                Content = "✕",
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 4, 8, 4),
                Cursor = Cursors.Hand,
                FontSize = 14,
                Margin = new Thickness(8, 0, 0, 0)
            };
            closeBtn.Click += (s, e) => PluginsPanel.Children.Remove(updateBanner);

            stack.Children.Add(icon);
            stack.Children.Add(text);
            stack.Children.Add(updateBtn);
            stack.Children.Add(closeBtn);

            updateBanner.Child = stack;
            PluginsPanel.Children.Insert(0, updateBanner);
        }

        private async void ShowUpdateDialog_Click(object sender, RoutedEventArgs e)
        {
            var updates = await PluginUpdateService.CheckUpdatesAsync();

            var dialog = new Window
            {
                Title = "插件更新",
                Width = 500,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42))
            };

            var mainStack = new StackPanel { Margin = new Thickness(20) };

            var header = new TextBlock
            {
                Text = $"发现 {updates.Count} 个插件更新",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 16)
            };
            mainStack.Children.Add(header);

            var scrollViewer = new ScrollViewer
            {
                Height = 250,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var updateStack = new StackPanel();
            foreach (var update in updates)
            {
                var itemBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var itemStack = new StackPanel();

                var nameText = new TextBlock
                {
                    Text = update.PluginName,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White
                };

                var versionText = new TextBlock
                {
                    Text = $"{update.CurrentVersion} → {update.NewVersion}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    Margin = new Thickness(0, 4, 0, 0)
                };

                itemStack.Children.Add(nameText);
                itemStack.Children.Add(versionText);
                itemBorder.Child = itemStack;
                updateStack.Children.Add(itemBorder);
            }

            scrollViewer.Content = updateStack;
            mainStack.Children.Add(scrollViewer);

            var buttonStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var updateAllBtn = new Button
            {
                Content = "全部更新",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };
            updateAllBtn.Click += async (s, ev) =>
            {
                dialog.Close();
                await UpdateAllPluginsAsync(updates);
            };

            var cancelBtn = new Button
            {
                Content = "稍后再说",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            cancelBtn.Click += (s, ev) => dialog.Close();

            buttonStack.Children.Add(updateAllBtn);
            buttonStack.Children.Add(cancelBtn);
            mainStack.Children.Add(buttonStack);

            dialog.Content = mainStack;
            dialog.ShowDialog();
        }

        private async Task UpdateAllPluginsAsync(List<PluginUpdateInfo> updates)
        {
            foreach (var update in updates)
            {
                try
                {
                    if (update.MarketPlugin != null)
                    {
                        await PluginInstallService.UpdatePluginAsync(
                            update.PluginId,
                            update.MarketPlugin);
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "更新插件失败: {PluginId}", update.PluginId);
                }
            }

            System.Windows.MessageBox.Show("所有插件更新完成", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshPluginList();
        }

        private void UpdateAboutInfo()
        {
            var plugins = HostProvider.Instance.PluginStore.GetAll();
            var capCount = Engine.ManifestReader.KnownCapabilities.Count;
            var ver = typeof(App).Assembly.GetName().Version;
            AboutVersion.Text = $"v{ver?.Major ?? 0}.{ver?.Minor ?? 0}.{ver?.Build ?? 0} · .NET 8.0 · WPF";
            AboutStats.Text = $"{capCount} 项原子能力 · {plugins.Count} 个插件 · 3 种运行时";
            PluginsHeader.Text = $"已安装插件 ({plugins.Count})";
        }

        #region Tab Navigation

        private Button? _activeTab;

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button tab || tab == _activeTab) return;

            // 停用旧标签
            if (_activeTab != null) DeactivateTab(_activeTab);

            // 激活新标签
            ActivateTab(tab);

            // 切换面板（带淡入过渡）
            SwitchPanel(tab);
        }

        private void SwitchPanel(Button tab)
        {
            var panels = new[] {
                (TabSystem, PanelSystem),
                (TabPlugins, PanelPlugins),
                (TabDev, PanelDev)
            };

            // ✅ 找到当前显示的面板，先淡出
            StackPanel? oldPanel = null;
            StackPanel? newPanel = null;

            foreach (var (tabBtn, panel) in panels)
            {
                if (panel.Visibility == Visibility.Visible && tabBtn != tab)
                    oldPanel = panel;
                if (tabBtn == tab)
                    newPanel = panel;
            }

            if (newPanel == null) return;

            // 如果有旧面板，先淡出再切换
            if (oldPanel != null)
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };

                fadeOut.Completed += (s, e) =>
                {
                    oldPanel.Visibility = Visibility.Collapsed;
                    oldPanel.Opacity = 1; // 恢复透明度

                    // 显示新面板，并用位移强化页面层级变化。
                    newPanel.Visibility = Visibility.Visible;
                    Helpers.AnimationHelperEnhanced.SlideInFromBottom(newPanel, durationMs: 280);
                };

                oldPanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
            else
            {
                // 没有旧面板，直接显示
                newPanel.Visibility = Visibility.Visible;
                Helpers.AnimationHelperEnhanced.SlideInFromBottom(newPanel, durationMs: 280);
            }
        }

        private void ActivateTab(Button tab)
        {
            _activeTab = tab;
            tab.Background = BlueBrush;
            tab.Foreground = WhiteBrush;
            tab.FontWeight = FontWeights.SemiBold;
        }

        private void DeactivateTab(Button tab)
        {
            tab.Background = Brushes.Transparent;
            tab.Foreground = GrayBrush;
            tab.FontWeight = FontWeights.Normal;
        }

        private void WelcomeDismiss_Click(object sender, RoutedEventArgs e)
        {
            WelcomeBanner.Visibility = Visibility.Collapsed;
            App.MarkOnboarded();
        }

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Tab)
            {
                e.Handled = true;
                // 循环切换面板
                if (_activeTab == TabSystem)
                    Tab_Click(TabPlugins, new RoutedEventArgs());
                else if (_activeTab == TabPlugins)
                    Tab_Click(TabDev, new RoutedEventArgs());
                else
                    Tab_Click(TabSystem, new RoutedEventArgs());
            }
        }

        #endregion

        #region Shared Brush helpers (from Colors.xaml)

        private Brush GreenBrush => (Brush)FindResource("SuccessGreenBrush");
        private Brush GrayBrush => (Brush)FindResource("TextSecondaryBrush");
        private Brush LightTextBrush => (Brush)FindResource("TextPrimaryBrush");
        private Brush CardBgBrush => (Brush)FindResource("CardBackgroundBrush");
        private Brush BlueBrush => (Brush)FindResource("AccentBlueBrush");
        private Brush RedBrush => (Brush)FindResource("DangerRedBrush");
        private Brush WhiteBrush => (Brush)FindResource("WhiteBrush");

        #endregion

        private void DevTools_Click(object sender, RoutedEventArgs e)
        {
            PluginDevTools.Open(Window.GetWindow(this)!);
        }

        private static bool _isLightMode;
        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            _isLightMode = !_isLightMode;

            // 使用 WPF-UI 主题管理器切换整个 Application
            var theme = _isLightMode
                ? Wpf.Ui.Appearance.ApplicationTheme.Light
                : Wpf.Ui.Appearance.ApplicationTheme.Dark;
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(theme);
            App.SaveThemeSetting(_isLightMode);
            App.UpdateThemeResources(_isLightMode);

            // 同步窗口背景色
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.Background = _isLightMode
                    ? (Brush)FindResource("LightSurfaceBrush")
                    : (Brush)FindResource("SurfaceBackgroundBrush");
            }

            if (sender is Button btn)
                btn.Content = _isLightMode ? "暗色" : "亮色";
        }

        private void RefreshDocLinks()
        {
            DocLinksPanel.Children.Clear();

            var docsDir = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "docs"));

            if (!Directory.Exists(docsDir))
                docsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs");

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
                    Foreground = BlueBrush,
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

            try
            {
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
                        ColumnStatusText.Text = "移除失败: " + (result.ErrorMessage ?? "未知错误");
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
                        ColumnStatusText.Text = "注入失败: " + (result.ErrorMessage ?? "未知错误");
                }
            }
            catch (Exception ex)
            {
                ColumnStatusText.Text = "操作异常: " + ex.Message;
                ColumnStatusText.Foreground = RedBrush;
            }

            ColumnButton.Content = _columnEnabled ? "移除" : "一键开启";
            ColumnButton.IsEnabled = true;
        }

        private async void ContextMenuButton_Click(object sender, RoutedEventArgs e)
        {
            ContextMenuButton.IsEnabled = false;
            ContextMenuStatusText.Text = "处理中...";

            try
            {
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
                        ContextMenuStatusText.Text = "移除失败: " + (result.ErrorMessage ?? "未知错误");
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
                        ContextMenuStatusText.Text = "注册失败: " + (result.ErrorMessage ?? "未知错误");
                }
            }
            catch (Exception ex)
            {
                ContextMenuStatusText.Text = "操作异常: " + ex.Message;
                ContextMenuStatusText.Foreground = RedBrush;
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

        #region Plugin List

        private int _pluginCardVersion;

        private void PluginSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshPluginList(); // 重新过滤渲染
        }

        private void RefreshPlugins_Click(object sender, RoutedEventArgs e)
        {
            PluginSearchBox.Text = "";
            RefreshPluginList();
        }

        private void RefreshPluginList()
        {
            _pluginCardVersion++;
            var version = _pluginCardVersion;

            // 显示骨架屏
            PluginsPanel.Children.Clear();
            PluginsPanel.Visibility = Visibility.Visible;
            PluginsLoading.Visibility = Visibility.Collapsed;

            for (int i = 0; i < 3; i++)
            {
                PluginsPanel.Children.Add(new SkeletonCard());
            }

            // 延迟加载实际内容
            Task.Delay(300).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => LoadPluginContent(version));
            });
        }

        private void LoadPluginContent(int version)
        {
            PluginsPanel.Children.Clear();
            var plugins = HostProvider.Instance.PluginStore.GetAll();

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
                                FontFamily = new FontFamily("Consolas"),
                                Foreground = LightTextBrush,
                                Margin = new Thickness(0, 2, 0, 0),
                            },
                            new TextBlock
                            {
                                Text = "模板: empty / hotkey / full",
                                FontSize = 11,
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

            // 搜索过滤
            var filter = PluginSearchBox?.Text.Trim().ToLowerInvariant() ?? "";
            var filtered = string.IsNullOrEmpty(filter)
                ? plugins
                : plugins.Where(p => p.Manifest.Name.ToLowerInvariant().Contains(filter)).ToList();

            if (filtered.Count == 0 && !string.IsNullOrEmpty(filter))
            {
                PluginsPanel.Children.Add(new Border
                {
                    Background = CardBgBrush,
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(18, 14, 18, 14),
                    Child = new TextBlock
                    {
                        Text = $"没有找到匹配 [{filter}] 的插件",
                        FontSize = 13,
                        Foreground = GrayBrush,
                    },
                });
                return;
            }

            foreach (var plugin in filtered)
            {
                PluginsPanel.Children.Add(CreatePluginCard(plugin, version));
            }
        }

        private UIElement CreatePluginCard(PluginEntry plugin, int version)
        {
            var isRunning = plugin.State == Core.PluginState.Running;
            var stateColor = isRunning ? GreenBrush : GrayBrush;
            var stateText = isRunning ? "运行中" : "已停止";

            var hotkey = PluginRegistry.GetPluginHotkey(plugin);

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
            // 运行时类型徽章
            var runtimeLabel = plugin.Manifest.Runtime switch
            {
                "dotnet" => "DLL",
                "webview" => "Web",
                "csharp-script" => "Script",
                _ => plugin.Manifest.Runtime
            };
            var runtimeColor = plugin.Manifest.Runtime switch
            {
                "dotnet" => BlueBrush,
                "webview" => GreenBrush,
                _ => GrayBrush
            };
            nameRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x15, 0x99, 0x99, 0x99)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(6, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = runtimeLabel,
                    FontSize = 9,
                    Foreground = runtimeColor,
                    VerticalAlignment = VerticalAlignment.Center,
                },
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
                        FontSize = 11,
                        Foreground = BlueBrush,
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

            // 第三行: 能力标签
            var caps = plugin.Manifest.Capabilities;
            if (caps.Count > 0)
            {
                var capsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                var showCaps = caps.Take(3).ToList();
                foreach (var cap in showCaps)
                {
                    capsRow.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(0x10, 0x99, 0x99, 0x99)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(4, 1, 4, 1),
                        Margin = new Thickness(0, 0, 4, 0),
                        Child = new TextBlock
                        {
                            Text = cap,
                            FontSize = 11,
                            Foreground = GrayBrush,
                        },
                    });
                }
                if (caps.Count > 3)
                {
                    capsRow.Children.Add(new TextBlock
                    {
                        Text = $"+{caps.Count - 3}",
                        FontSize = 10,
                        Foreground = GrayBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }
                infoStack.Children.Add(capsRow);
            }

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
                    Foreground = WhiteBrush,
                    Background = BlueBrush,
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
                Foreground = WhiteBrush,
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

            // 创建主卡片容器（包含状态条）
            var cardContainer = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10),
            };

            // 顶部彩色状态条
            var statusBar = new Border
            {
                Height = 3,
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(10, 10, 0, 0),
            };

            // 根据运行状态设置渐变颜色
            if (isRunning)
            {
                var gradient = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0),
                };
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x10, 0xB9, 0x81), 0));
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x34, 0xD3, 0x99), 1));
                statusBar.Background = gradient;
            }
            else
            {
                statusBar.Background = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63));
            }

            // 主卡片
            var card = new Border
            {
                Background = CardBgBrush,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18, 14, 18, 14),
                Child = grid,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new TranslateTransform(0, 0),
                Cursor = Cursors.Hand,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 16,
                    ShadowDepth = 2,
                    Opacity = 0.25,
                    Direction = 270,
                },
            };

            cardContainer.Children.Add(card);
            cardContainer.Children.Add(statusBar);

            // ♥ 卡片 hover 微动效 — 轻微上浮 + 背景变亮 + 阴影增强
            card.MouseEnter += (_, _) =>
            {
                var upAnim = new DoubleAnimation(0, -3, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var bgAnim = new ColorAnimation(
                    ((SolidColorBrush)CardBgBrush).Color,
                    Color.FromRgb(0x38, 0x38, 0x3C),
                    TimeSpan.FromMilliseconds(200));

                // 增强阴影效果
                if (card.Effect is System.Windows.Media.Effects.DropShadowEffect shadow)
                {
                    var shadowAnim = new DoubleAnimation(0.25, 0.4, TimeSpan.FromMilliseconds(200));
                    shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, shadowAnim);
                }

                ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.YProperty, upAnim);
                card.Background.BeginAnimation(SolidColorBrush.ColorProperty, bgAnim);
            };
            card.MouseLeave += (_, _) =>
            {
                var downAnim = new DoubleAnimation(-3, 0, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var bgAnim = new ColorAnimation(
                    Color.FromRgb(0x38, 0x38, 0x3C),
                    ((SolidColorBrush)CardBgBrush).Color,
                    TimeSpan.FromMilliseconds(250));

                // 恢复阴影效果
                if (card.Effect is System.Windows.Media.Effects.DropShadowEffect shadow)
                {
                    var shadowAnim = new DoubleAnimation(0.4, 0.25, TimeSpan.FromMilliseconds(250));
                    shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, shadowAnim);
                }

                ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.YProperty, downAnim);
                card.Background.BeginAnimation(SolidColorBrush.ColorProperty, bgAnim);
            };

            return cardContainer;
        }

        private async void PluginToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ToggleState state) return;
            btn.IsEnabled = false;

            try
            {
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Plugin toggle error: {ex.Message}");
            }

            btn.IsEnabled = true;
            RefreshPluginList();
        }

        private void PluginCapabilities_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not PluginEntry plugin) return;

            var dialog = new Window
            {
                Title = $"{plugin.Manifest.Name} - 权限详情",
                Width = 500,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1F, 0x22)),
                Content = new CapabilityDetailPanel(),
            };

            var panel = (CapabilityDetailPanel)dialog.Content;
            panel.LoadCapabilities(plugin.Id, plugin.Manifest.Capabilities);

            dialog.ShowDialog();
        }

        private void PluginSettings_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not PluginEntry entry) return;
            if (entry.Instance is not IHasSettingsUI hasUI) return;

            try
            {
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Settings UI error: {ex.Message}");
            }
        }

        #region Drag & Drop Installation

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropZone.BorderBrush = (Brush)FindResource("AccentBlueBrush");
                DropZone.Background = new SolidColorBrush(Color.FromArgb(30, 102, 126, 234));
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            DropZone.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 58, 58, 61));
            DropZone.Background = new SolidColorBrush(Color.FromArgb(255, 26, 26, 29));
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            DropZone_DragLeave(sender, e);

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0)
                return;

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();

                if (ext == ".csx" || ext == ".js" || ext == ".ts")
                {
                    try
                    {
                        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
                        if (!Directory.Exists(pluginsDir))
                            Directory.CreateDirectory(pluginsDir);

                        var destPath = Path.Combine(pluginsDir, Path.GetFileName(file));
                        File.Copy(file, destPath, overwrite: true);

                        FloatingHudWindow.ShowToast($"✅ {Path.GetFileName(file)} 已添加\n稍后自动加载...");
                    }
                    catch (Exception ex)
                    {
                        FloatingHudWindow.ShowToast($"❌ 复制失败: {ex.Message}");
                    }
                }
                else if (ext == ".lpak")
                {
                    _ = InstallLpakAsync(file);
                }
                else
                {
                    FloatingHudWindow.ShowToast($"⚠️ 不支持的文件类型: {ext}");
                }
            }
        }

        private void CreateScript_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ScriptCreationDialog();
            if (dialog.ShowDialog() == true)
            {
                var scriptPath = dialog.ScriptPath;
                var template = dialog.SelectedTemplate;
                var language = dialog.SelectedLanguage;

                try
                {
                    var pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
                    if (!Directory.Exists(pluginsDir))
                        Directory.CreateDirectory(pluginsDir);

                    var content = GetScriptTemplate(template, language);
                    var ext = language switch
                    {
                        "C#" => ".csx",
                        "JavaScript" => ".js",
                        "TypeScript" => ".ts",
                        _ => ".js"
                    };

                    var fileName = $"{scriptPath}{ext}";
                    var fullPath = Path.Combine(pluginsDir, fileName);

                    File.WriteAllText(fullPath, content);

                    FloatingHudWindow.ShowToast($"✅ {fileName} 已创建\n稍后自动加载...");

                    // 打开编辑器（如果需要）
                    if (dialog.OpenInEditor)
                    {
                        DevTools_Click(sender, e);
                    }
                }
                catch (Exception ex)
                {
                    FloatingHudWindow.ShowToast($"❌ 创建失败: {ex.Message}");
                }
            }
        }

        private string GetScriptTemplate(string template, string language)
        {
            if (language == "C#")
            {
                return template switch
                {
                    "热键插件" => @"// @capabilities system.hotkey, system.clipboard, system.notification
// 热键插件示例
await Host.HotKey.RegisterAsync(""Ctrl+Shift+T"", async () => {
    var time = DateTime.Now.ToString(""HH:mm:ss"");
    await Host.Clipboard.SetTextAsync(time);
    await Host.Notification.ShowAsync(""时间: "" + time, ""success"");
});",
                    "笔记插件" => @"// @capabilities system.hotkey, shell.selection, system.notification
// 笔记插件 - 为选中的文件添加备注
await Host.HotKey.RegisterAsync(""Ctrl+Shift+N"", async () => {
    var items = await Host.ShellSelection.GetSelectedItemsAsync();
    if (items.IsSuccess && items.Data.Count > 0)
    {
        var filePath = items.Data[0];
        // TODO: 打开笔记编辑界面
        await Host.Notification.ShowAsync(""笔记功能开发中"", ""info"");
    }
});",
                    _ => @"// @capabilities system.notification
// 空白脚本
await Host.Notification.ShowAsync(""Hello from C# script!"", ""success"");
"
                };
            }
            else // JavaScript/TypeScript
            {
                return template switch
                {
                    "热键插件" => @"// @capabilities system.hotkey, system.clipboard, system.notification
// 热键插件示例
long.hotkey.register('Ctrl+Shift+T', async () => {
    const time = new Date().toLocaleString('zh-CN');
    await long.clipboard.setText(time);
    await long.notification.show(`⏰ ${time}`, 'success');
});
console.log('⏰ 时间插件已加载');",
                    "笔记插件" => @"// @capabilities system.hotkey, shell.selection, system.notification
// 笔记插件 - 为选中的文件添加备注
long.hotkey.register('Ctrl+Shift+N', async () => {
    const items = await long.shell.getSelectedItems();
    if (items.success && items.data.length > 0) {
        const filePath = items.data[0];
        // TODO: 打开笔记编辑界面
        await long.notification.show('笔记功能开发中', 'info');
    }
});
console.log('📝 笔记插件已加载');",
                    _ => @"// @capabilities system.notification
// 空白脚本
long.notification.show('Hello from JavaScript!', 'success');
console.log('插件已加载');
"
                };
            }
        }

        private async Task InstallLpakAsync(string lpakFilePath)
        {
            try
            {
                var pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
                if (!Directory.Exists(pluginsDir))
                    Directory.CreateDirectory(pluginsDir);

                var installer = new Services.LpakInstallerService(pluginsDir);

                FloatingHudWindow.ShowToast($"📦 正在安装 {Path.GetFileName(lpakFilePath)}...");

                var result = await installer.InstallAsync(lpakFilePath);

                if (result.IsSuccess && result.Manifest != null)
                {
                    FloatingHudWindow.ShowToast(
                        $"✅ {result.Manifest.Name} 安装成功\n" +
                        $"版本: {result.Manifest.Version}\n" +
                        $"稍后自动加载...");
                }
                else
                {
                    FloatingHudWindow.ShowToast($"❌ 安装失败\n{result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                FloatingHudWindow.ShowToast($"❌ 安装失败: {ex.Message}");
                Serilog.Log.Error(ex, "安装 .lpak 失败");
            }
        }

        #endregion

        private class ToggleState
        {
            public string PluginId { get; init; } = string.Empty;
            public int Version { get; init; }
        }

        #endregion
    }
}
