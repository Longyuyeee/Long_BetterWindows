using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        private bool _docsLoaded;

        public ToolCenterControl()
        {
            InitializeComponent();
            SizeChanged += (_, _) => ApplyResponsiveLayout(ActualWidth);
            RefreshColumnStatus();
            RefreshContextMenuStatus();
            RefreshStartupStatus();
            RefreshMouseGestureControls();
            ShowPage("overview");

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
                    if (_activePage == "plugins")
                        RefreshPluginList();
                    UpdateAboutInfo();
                });
            };
        }

        private void RefreshMouseGestureControls()
        {
            var mode = ServicesInitializer.MouseGestures.Mode;
            foreach (var button in MouseGestureButtons.Children.OfType<Button>())
            {
                var active = string.Equals(
                    button.Tag?.ToString(), mode.ToString(), StringComparison.OrdinalIgnoreCase);
                button.SetResourceReference(
                    FrameworkElement.StyleProperty,
                    active ? "LongButton.Primary" : "LongButton");
            }
            MouseGestureStatusText.Text = mode switch
            {
                MouseGestureMode.MiddleButton => "已启用：按下鼠标中键呼出",
                MouseGestureMode.LongRightPress => $"已启用：静止长按右键 {LongRightPressRecognizer.HoldMilliseconds}ms 呼出",
                _ => "当前关闭，不监听全局鼠标按键",
            };
        }

        private async void MouseGestureMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string value }
                || !Enum.TryParse<MouseGestureMode>(value, out var mode)) return;
            MouseGestureButtons.IsEnabled = false;
            try
            {
                await ServicesInitializer.MouseGestures.SetModeAsync(mode);
                RefreshMouseGestureControls();
            }
            finally { MouseGestureButtons.IsEnabled = true; }
        }

        private void ApplyResponsiveLayout(double width)
        {
            var isNarrow = width < 860;
            NavigationColumn.Width = new GridLength(isNarrow ? 160 : 220);
            PageHeader.Margin = isNarrow
                ? new Thickness(18, 16, 18, 12)
                : new Thickness(32, 20, 32, 16);
            ContentBody.Margin = isNarrow
                ? new Thickness(18, 0, 18, 18)
                : new Thickness(32, 0, 32, 32);

            if (isNarrow)
            {
                OverviewPrimaryColumn.Width = new GridLength(1, GridUnitType.Star);
                OverviewGapColumn.Width = new GridLength(0);
                OverviewStatusColumn.Width = new GridLength(0);
                Grid.SetRow(OverviewStatusCard, 1);
                Grid.SetColumn(OverviewStatusCard, 0);
                OverviewStatusCard.Margin = new Thickness(0, 12, 0, 0);
            }
            else
            {
                OverviewPrimaryColumn.Width = new GridLength(1.3, GridUnitType.Star);
                OverviewGapColumn.Width = new GridLength(16);
                OverviewStatusColumn.Width = new GridLength(1, GridUnitType.Star);
                Grid.SetRow(OverviewStatusCard, 0);
                Grid.SetColumn(OverviewStatusCard, 2);
                OverviewStatusCard.Margin = new Thickness(0);
            }
        }

        private void UpdateAboutInfo()
        {
            var plugins = HostProvider.Instance.PluginStore.GetAll();
            var capCount = Engine.ManifestReader.KnownCapabilities.Count;
            AboutVersion.Text = $"v{App.ProductVersion} · .NET 8.0 · WPF";
            AboutStats.Text = $"{capCount} 项原子能力 · {plugins.Count} 个插件 · 3 种运行时";
            PluginsHeader.Text = $"已安装插件 ({plugins.Count})";
            OverviewPluginCount.Text = plugins.Count.ToString();
            OverviewCommandCount.Text = HostProvider.Instance.PluginStore.Commands.Count.ToString();
            OverviewCapabilityCount.Text = capCount.ToString();
        }

        private async void ClearSearchPreferences_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;
            button.IsEnabled = false;
            await ServicesInitializer.SearchPreferences.ClearAsync();
            await ServicesInitializer.SuperPanelGroups.ClearAsync();
            SearchPreferenceStatusText.Text = "固定、最近使用与自定义分组已清除";
            button.IsEnabled = true;
        }

        #region Navigation

        private string _activePage = "overview";

        internal void OpenMarketForQuality() => ShowPage("market");

        private void Navigation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton { Tag: string page })
                ShowPage(page);
        }

        private void ShowPage(string page)
        {
            (string Key, FrameworkElement Panel, RadioButton Navigation, string Title, string Subtitle)[] pages =
            {
                ("overview", PanelOverview, NavOverview, "概览", "平台状态、关键能力和下一步操作"),
                ("plugins", PanelPlugins, NavPlugins, "插件", "管理已安装插件、运行状态、权限和设置"),
                ("market", PanelMarket, NavMarket, "插件市场", "发现、审查并安装可信的 Long 原生插件"),
                ("system", PanelSystem, NavSystem, "系统集成", "配置 Explorer、启动项和全局快捷键"),
                ("developer", PanelDev, NavDeveloper, "开发者", "创建、调试、打包插件并查阅开发资源"),
                ("settings", PanelSettings, NavSettings, "设置", "外观、动效、本地数据与隐私"),
            };

            foreach (var (key, panel, navigation, title, subtitle) in pages)
            {
                var selected = key == page;
                panel.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
                navigation.IsChecked = selected;
                if (!selected) continue;

                _activePage = key;
                PageTitle.Text = title;
                PageSubtitle.Text = subtitle;
                if (key == "plugins")
                    RefreshPluginList();
                else if (key == "market" && MarketHost.Content == null)
                    MarketHost.Content = new MarketplaceControl();
                else if (key == "developer" && !_docsLoaded)
                {
                    RefreshDocLinks();
                    _docsLoaded = true;
                }
                Helpers.AnimationHelper.FadeInElement(panel, durationMs: 160);
            }
        }

        private void WelcomeDismiss_Click(object sender, RoutedEventArgs e)
        {
            WelcomeBanner.Visibility = Visibility.Collapsed;
            App.MarkOnboarded();
        }

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                var pages = new[] { "overview", "plugins", "market", "system", "developer", "settings" };
                var current = Array.IndexOf(pages, _activePage);
                var direction = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
                ShowPage(pages[(current + direction + pages.Length) % pages.Length]);
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

        private void DesignSystemPreview_Click(object sender, RoutedEventArgs e)
        {
            var preview = new DesignSystemPreview
            {
                Owner = Window.GetWindow(this),
            };
            preview.Show();
        }

        private void OpenPalette_Click(object sender, RoutedEventArgs e)
            => CommandPaletteWindow.ShowPalette();

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

            PluginsLoading.Visibility = Visibility.Collapsed;
            PluginsPanel.Items.Clear();
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

                PluginsPanel.Items.Add(emptyCard);
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
                PluginsPanel.Items.Add(new Border
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
                PluginsPanel.Items.Add(CreatePluginCard(plugin, version));
            }
        }

        private Border CreatePluginCard(PluginEntry plugin, int version)
        {
            var isRunning = plugin.State == Core.PluginState.Running;
            var stateColor = isRunning ? GreenBrush : GrayBrush;
            var stateText = isRunning ? "运行中" : "已停止";
            var hotkey = PluginRegistry.GetPluginHotkey(plugin);
            var infoStack = new StackPanel();
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
            var runtimeLabel = plugin.Manifest.Runtime switch
            {
                "dotnet" => "DLL",
                "webview" => "Web",
                "csharp-script" => "Script",
                _ => plugin.Manifest.Runtime
            };
            var runtimeBadge = new Border
            {
                Margin = new Thickness(6, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = runtimeLabel,
                    FontSize = 9,
                    Foreground = GrayBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                VerticalAlignment = VerticalAlignment.Center,
            };
            runtimeBadge.SetResourceReference(FrameworkElement.StyleProperty, "LongBadge");
            nameRow.Children.Add(runtimeBadge);
            infoStack.Children.Add(nameRow);

            var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            if (hotkey != null)
            {
                var hotkeyBadge = new Border
                {
                    Margin = new Thickness(0, 0, 8, 0),
                    Child = new TextBlock
                    {
                        Text = hotkey,
                        FontSize = 11,
                        FontFamily = (FontFamily)FindResource("Long.Font.Mono"),
                        Foreground = GrayBrush,
                    },
                };
                hotkeyBadge.SetResourceReference(FrameworkElement.StyleProperty, "LongHotkeyBadge");
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

            var caps = plugin.Manifest.Capabilities;
            if (caps.Count > 0)
            {
                var capsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                var showCaps = caps.Take(3).ToList();
                foreach (var cap in showCaps)
                {
                    var capabilityBadge = new Border
                    {
                        Margin = new Thickness(0, 0, 4, 0),
                        Child = new TextBlock
                        {
                            Text = cap,
                            FontSize = 11,
                            Foreground = GrayBrush,
                        },
                    };
                    capabilityBadge.SetResourceReference(FrameworkElement.StyleProperty, "LongBadge");
                    capsRow.Children.Add(capabilityBadge);
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

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            if (plugin.Instance is IHasMainUI hasUI)
            {
                var openBtn = new Button
                {
                    Content = "打开",
                    MinWidth = 62,
                    Margin = new Thickness(0, 0, 6, 0),
                    Tag = hasUI,
                };
                openBtn.SetResourceReference(FrameworkElement.StyleProperty, "LongButton.Primary");
                openBtn.Click += (_, _) => hasUI.ShowMainUI();
                btnPanel.Children.Add(openBtn);
            }

            if (plugin.Instance is IHasSettingsUI)
            {
                var settingsBtn = new Button
                {
                    Width = 38,
                    Margin = new Thickness(0, 0, 6, 0),
                    Tag = plugin,
                };
                settingsBtn.SetResourceReference(FrameworkElement.StyleProperty, "LongIconButton");
                var settingsIcon = new System.Windows.Shapes.Path
                {
                    Data = (Geometry)FindResource("Long.Icon.Settings"),
                    Width = 17,
                    Height = 17,
                };
                settingsIcon.SetResourceReference(FrameworkElement.StyleProperty, "LongIcon");
                settingsBtn.Content = settingsIcon;
                settingsBtn.Click += PluginSettings_Click;
                btnPanel.Children.Add(settingsBtn);
            }

            var btnText = isRunning ? "禁用" : "启用";
            var toggleBtn = new Button
            {
                Content = btnText,
                MinWidth = 62,
                Tag = new ToggleState { PluginId = plugin.Id, Version = version },
            };
            toggleBtn.SetResourceReference(
                FrameworkElement.StyleProperty,
                isRunning ? "LongButton.Danger" : "LongButton");
            toggleBtn.Click += PluginToggle_Click;
            btnPanel.Children.Add(toggleBtn);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(infoStack, 0);
            Grid.SetColumn(btnPanel, 1);
            grid.Children.Add(infoStack);
            grid.Children.Add(btnPanel);

            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 12),
                Child = grid,
            };
            card.SetResourceReference(FrameworkElement.StyleProperty, "LongPluginCard");
            return card;
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

        private void PluginSettings_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not PluginEntry entry) return;
            if (entry.Instance is not IHasSettingsUI hasUI) return;

            try
            {
                var settingsUI = hasUI.CreateSettingsUI();
                if (settingsUI == null) return;

                var popup = new PluginWindowHost(
                    $"{entry.Manifest.Name} · 设置",
                    settingsUI,
                    entry.Manifest.Window)
                {
                    Owner = Window.GetWindow(this),
                    Width = 520,
                    Height = 420,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                };

                popup.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Settings UI error: {ex.Message}");
            }
        }

        private class ToggleState
        {
            public string PluginId { get; init; } = string.Empty;
            public int Version { get; init; }
        }

        #endregion
    }
}
