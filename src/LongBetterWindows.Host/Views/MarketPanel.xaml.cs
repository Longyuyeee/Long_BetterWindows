using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Services;
using Serilog;

namespace LongBetterWindows.Host.Views
{
    public partial class MarketPanel : UserControl
    {
        private List<MarketPlugin> _allPlugins = new();
        private List<MarketPlugin> _filteredPlugins = new();
        private string _selectedCategory = "全部";

        private static readonly SolidColorBrush WhiteBrush = new(Color.FromRgb(248, 250, 252));
        private static readonly SolidColorBrush GrayBrush = new(Color.FromRgb(100, 116, 139));
        private static readonly SolidColorBrush BlueBrush = new(Color.FromRgb(56, 189, 248));
        private static readonly SolidColorBrush DarkBrush = new(Color.FromRgb(30, 41, 59));

        public MarketPanel()
        {
            InitializeComponent();
            Loaded += MarketPanel_Loaded;
        }

        private async void MarketPanel_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadMarketDataAsync();
        }

        private async Task LoadMarketDataAsync()
        {
            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;

                // 加载分类
                var categories = await MarketApiService.GetCategoriesAsync();
                BuildCategoryButtons(categories);

                // 加载所有插件
                _allPlugins = await MarketApiService.GetPluginsAsync();
                _filteredPlugins = _allPlugins;

                // 加载精选插件
                var featured = await MarketApiService.GetFeaturedPluginsAsync();
                RenderFeaturedPlugins(featured);

                // 渲染所有插件
                RenderPlugins(_filteredPlugins);

                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "加载插件市场数据失败");
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void BuildCategoryButtons(List<string> categories)
        {
            CategoryPanel.Children.Clear();

            foreach (var category in categories)
            {
                var btn = new Button
                {
                    Content = category,
                    Height = 32,
                    Padding = new Thickness(16, 0, 16, 0),
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = category == _selectedCategory ? BlueBrush : DarkBrush,
                    Foreground = WhiteBrush,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    FontSize = 13,
                    Tag = category,
                };

                btn.Click += CategoryButton_Click;
                CategoryPanel.Children.Add(btn);
            }
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var category = btn.Tag.ToString()!;

            _selectedCategory = category;

            // 更新按钮样式
            foreach (Button child in CategoryPanel.Children)
            {
                child.Background = child.Tag.ToString() == category ? BlueBrush : DarkBrush;
            }

            // 过滤插件
            if (category == "全部")
            {
                _filteredPlugins = _allPlugins;
            }
            else
            {
                _filteredPlugins = _allPlugins.Where(p => p.Category == category).ToList();
            }

            RenderPlugins(_filteredPlugins);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var keyword = SearchBox.Text.Trim();
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(keyword) ? Visibility.Visible : Visibility.Collapsed;

            if (string.IsNullOrEmpty(keyword))
            {
                _filteredPlugins = _selectedCategory == "全部"
                    ? _allPlugins
                    : _allPlugins.Where(p => p.Category == _selectedCategory).ToList();
            }
            else
            {
                var lowerKeyword = keyword.ToLower();
                _filteredPlugins = _allPlugins
                    .Where(p =>
                        (_selectedCategory == "全部" || p.Category == _selectedCategory) &&
                        (p.Name.ToLower().Contains(lowerKeyword) ||
                         p.Description.ToLower().Contains(lowerKeyword) ||
                         p.Tags.Any(t => t.ToLower().Contains(lowerKeyword))))
                    .ToList();
            }

            RenderPlugins(_filteredPlugins);
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            MarketApiService.ClearCache();
            await LoadMarketDataAsync();
        }

        private void RenderFeaturedPlugins(List<MarketPlugin> plugins)
        {
            FeaturedPluginsPanel.Children.Clear();

            if (plugins.Count == 0)
            {
                FeaturedSection.Visibility = Visibility.Collapsed;
                return;
            }

            FeaturedSection.Visibility = Visibility.Visible;

            foreach (var plugin in plugins)
            {
                var card = CreatePluginCard(plugin, isFeatured: true);
                FeaturedPluginsPanel.Children.Add(card);
            }
        }

        private void RenderPlugins(List<MarketPlugin> plugins)
        {
            PluginsPanel.Children.Clear();

            if (plugins.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                AllPluginsHeader.Visibility = Visibility.Collapsed;
                return;
            }

            EmptyState.Visibility = Visibility.Collapsed;
            AllPluginsHeader.Visibility = Visibility.Visible;
            AllPluginsHeader.Text = $"📦 {_selectedCategory} ({plugins.Count})";

            for (int i = 0; i < plugins.Count; i++)
            {
                var plugin = plugins[i];
                var card = CreatePluginCard(plugin, isFeatured: false);

                // 入场动画
                card.Opacity = 0;
                card.RenderTransform = new TranslateTransform(0, 20);

                var delay = TimeSpan.FromMilliseconds(i * 50);
                AnimateCardEntry(card, delay);

                PluginsPanel.Children.Add(card);
            }
        }

        private Border CreatePluginCard(MarketPlugin plugin, bool isFeatured)
        {
            var cardWidth = isFeatured ? 400 : 280;

            var card = new Border
            {
                Width = cardWidth,
                Height = 180,
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 16, 16),
                Padding = new Thickness(16),
                Cursor = System.Windows.Input.Cursors.Hand,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 16,
                    ShadowDepth = 4,
                    Opacity = 0.25,
                    Color = Colors.Black,
                },
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 内容区域
            var contentStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            // 顶部信息行
            var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameText = new TextBlock
            {
                Text = plugin.Name,
                Foreground = WhiteBrush,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(nameText, 0);

            var ratingPanel = new StackPanel { Orientation = Orientation.Horizontal };
            ratingPanel.Children.Add(new TextBlock { Text = "⭐", FontSize = 12 });
            ratingPanel.Children.Add(new TextBlock
            {
                Text = plugin.Rating.ToString("F1"),
                Foreground = WhiteBrush,
                FontSize = 12,
                Margin = new Thickness(4, 0, 0, 0),
            });
            Grid.SetColumn(ratingPanel, 1);

            topGrid.Children.Add(nameText);
            topGrid.Children.Add(ratingPanel);
            contentStack.Children.Add(topGrid);

            // 作者和下载量
            var metaText = new TextBlock
            {
                Text = $"by {plugin.Author} • {plugin.Downloads} 次下载",
                Foreground = GrayBrush,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8),
            };
            contentStack.Children.Add(metaText);

            // 描述
            var descText = new TextBlock
            {
                Text = plugin.Description,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 40,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 8),
            };
            contentStack.Children.Add(descText);

            // 标签
            var tagsPanel = new WrapPanel();
            foreach (var tag in plugin.Tags.Take(3))
            {
                tagsPanel.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 0, 6, 0),
                    Child = new TextBlock
                    {
                        Text = tag,
                        Foreground = GrayBrush,
                        FontSize = 10,
                    }
                });
            }
            contentStack.Children.Add(tagsPanel);

            Grid.SetRow(contentStack, 0);
            grid.Children.Add(contentStack);

            // 底部按钮
            var installBtn = new Button
            {
                Content = "安装",
                Height = 32,
                Background = BlueBrush,
                Foreground = WhiteBrush,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Tag = plugin,
            };
            installBtn.Click += InstallButton_Click;

            Grid.SetRow(installBtn, 1);
            grid.Children.Add(installBtn);

            card.Child = grid;

            // 悬停动画
            card.MouseEnter += (s, e) =>
            {
                var anim = new DoubleAnimation(1.02, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                card.RenderTransform = new ScaleTransform(1, 1);
                card.RenderTransformOrigin = new Point(0.5, 0.5);
                ((ScaleTransform)card.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                ((ScaleTransform)card.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, anim);

                if (card.Effect is System.Windows.Media.Effects.DropShadowEffect shadow)
                {
                    shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
                        new DoubleAnimation(0.4, TimeSpan.FromMilliseconds(200)));
                }
            };

            card.MouseLeave += (s, e) =>
            {
                var anim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                if (card.RenderTransform is ScaleTransform st)
                {
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                }

                if (card.Effect is System.Windows.Media.Effects.DropShadowEffect shadow)
                {
                    shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
                        new DoubleAnimation(0.25, TimeSpan.FromMilliseconds(200)));
                }
            };

            return card;
        }

        private void AnimateCardEntry(Border card, TimeSpan delay)
        {
            var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var translateAnim = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(300))
            {
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            card.BeginAnimation(OpacityProperty, opacityAnim);
            ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.YProperty, translateAnim);
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var plugin = (MarketPlugin)btn.Tag;

            // 检查是否已安装
            if (PluginInstallService.IsPluginInstalled(plugin.Id))
            {
                MessageBox.Show($"插件 {plugin.Name} 已经安装", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                btn.Content = "✓ 已安装";
                btn.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                btn.IsEnabled = false;
                return;
            }

            btn.IsEnabled = false;
            var originalContent = btn.Content;
            btn.Content = "安装中...";

            try
            {
                var progress = new Progress<double>(percent =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        btn.Content = $"下载中 {percent:F0}%";
                    });
                });

                var success = await PluginInstallService.InstallFromMarketAsync(
                    plugin,
                    progress,
                    status => Dispatcher.Invoke(() => btn.Content = status));

                if (success)
                {
                    btn.Content = "✓ 已安装";
                    btn.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                    MessageBox.Show($"插件 {plugin.Name} 安装成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    btn.Content = originalContent;
                    btn.IsEnabled = true;
                    MessageBox.Show($"安装失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "安装插件失败: {PluginId}", plugin.Id);
                btn.Content = originalContent;
                btn.IsEnabled = true;
                MessageBox.Show($"安装失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
