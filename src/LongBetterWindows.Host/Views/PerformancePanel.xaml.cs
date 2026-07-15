using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views
{
    public partial class PerformancePanel : UserControl
    {
        private DispatcherTimer? _refreshTimer;
        private readonly SolidColorBrush BlueBrush = new(Color.FromRgb(59, 130, 246));
        private readonly SolidColorBrush GreenBrush = new(Color.FromRgb(16, 185, 129));
        private readonly SolidColorBrush OrangeBrush = new(Color.FromRgb(249, 115, 22));
        private readonly SolidColorBrush GrayBrush = new(Color.FromRgb(148, 163, 184));
        private readonly SolidColorBrush CardBgBrush = new(Color.FromRgb(30, 41, 59));
        private readonly SolidColorBrush LightTextBrush = new(Color.FromRgb(248, 250, 252));

        public PerformancePanel()
        {
            InitializeComponent();
            Loaded += PerformancePanel_Loaded;
            Unloaded += PerformancePanel_Unloaded;
        }

        private void PerformancePanel_Loaded(object sender, RoutedEventArgs e)
        {
            // 启动性能监控
            PerformanceMonitorService.Instance.StartMonitoring();

            // 启动 UI 刷新定时器（每秒更新一次）
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();

            RefreshUI();
        }

        private void PerformancePanel_Unloaded(object sender, RoutedEventArgs e)
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;
        }

        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            RefreshUI();
        }

        private void RefreshUI()
        {
            var summary = PerformanceMonitorService.Instance.GetSystemSummary();

            // 更新系统性能指标
            CpuPercentText.Text = $"{summary.CurrentCpuPercent:F1}%";
            CpuAvgText.Text = $"平均: {summary.AverageCpuPercent:F1}%";
            MemoryText.Text = $"{summary.CurrentMemoryMB:F0} MB";
            ApiCallsText.Text = summary.TotalApiCalls.ToString();
            PluginCountText.Text = $"{summary.PluginCount} 个插件";

            // 更新 CPU 指示器颜色
            if (summary.CurrentCpuPercent > 80)
                CpuPercentText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // 红色
            else if (summary.CurrentCpuPercent > 50)
                CpuPercentText.Foreground = OrangeBrush; // 橙色
            else
                CpuPercentText.Foreground = BlueBrush; // 蓝色

            // 绘制 CPU 历史图表
            DrawCpuChart(summary.CpuHistory);

            // 更新插件性能排行
            RefreshPluginPerformance();
        }

        private void DrawCpuChart(List<double> cpuHistory)
        {
            CpuChartCanvas.Children.Clear();

            if (cpuHistory.Count < 2)
                return;

            var width = CpuChartCanvas.ActualWidth;
            var height = CpuChartCanvas.ActualHeight;

            if (width <= 0 || height <= 0)
                return;

            var maxValue = Math.Max(cpuHistory.Max(), 10);
            var stepX = width / (cpuHistory.Count - 1);

            // 绘制网格线
            DrawGridLines(width, height);

            // 绘制折线
            var polyline = new Polyline
            {
                Stroke = BlueBrush,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };

            for (int i = 0; i < cpuHistory.Count; i++)
            {
                var x = i * stepX;
                var y = height - (cpuHistory[i] / maxValue * height);
                polyline.Points.Add(new Point(x, y));
            }

            CpuChartCanvas.Children.Add(polyline);

            // 绘制渐变填充
            var polygon = new Polygon
            {
                Fill = new LinearGradientBrush(
                    Color.FromArgb(80, 59, 130, 246),
                    Color.FromArgb(10, 59, 130, 246),
                    new Point(0, 0),
                    new Point(0, 1)),
                Points = new PointCollection(polyline.Points)
            };
            polygon.Points.Add(new Point(width, height));
            polygon.Points.Add(new Point(0, height));

            CpuChartCanvas.Children.Insert(0, polygon);
        }

        private void DrawGridLines(double width, double height)
        {
            var gridBrush = new SolidColorBrush(Color.FromArgb(30, 148, 163, 184));

            // 横线（每 20%）
            for (int i = 1; i <= 4; i++)
            {
                var y = height * i / 5.0;
                var line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = gridBrush,
                    StrokeThickness = 1
                };
                CpuChartCanvas.Children.Add(line);
            }
        }

        private void RefreshPluginPerformance()
        {
            var pluginData = PerformanceMonitorService.Instance.GetPluginPerformanceData();
            var plugins = HostProvider.Instance.PluginStore.GetAll();

            PluginPerformancePanel.Children.Clear();

            if (pluginData.Count == 0)
            {
                NoDataText.Visibility = Visibility.Visible;
                return;
            }

            NoDataText.Visibility = Visibility.Collapsed;

            foreach (var data in pluginData.Take(10)) // 只显示前 10 个
            {
                var plugin = plugins.FirstOrDefault(p => p.Manifest.Id == data.PluginId);
                var pluginName = plugin?.Manifest.Name ?? data.PluginId;

                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // 插件名称
                var nameText = new TextBlock
                {
                    Text = pluginName,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = LightTextBrush,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(nameText, 0);
                grid.Children.Add(nameText);

                // API 调用次数
                var callsStack = new StackPanel
                {
                    Margin = new Thickness(0, 0, 16, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                callsStack.Children.Add(new TextBlock
                {
                    Text = data.TotalApiCalls.ToString(),
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = BlueBrush,
                    HorizontalAlignment = HorizontalAlignment.Right
                });
                callsStack.Children.Add(new TextBlock
                {
                    Text = "API 调用",
                    FontSize = 10,
                    Foreground = GrayBrush,
                    HorizontalAlignment = HorizontalAlignment.Right
                });
                Grid.SetColumn(callsStack, 1);
                grid.Children.Add(callsStack);

                // 平均耗时
                var durationStack = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center
                };
                durationStack.Children.Add(new TextBlock
                {
                    Text = $"{data.AverageApiDurationMs:F1}ms",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = GreenBrush,
                    HorizontalAlignment = HorizontalAlignment.Right
                });
                durationStack.Children.Add(new TextBlock
                {
                    Text = "平均耗时",
                    FontSize = 10,
                    Foreground = GrayBrush,
                    HorizontalAlignment = HorizontalAlignment.Right
                });
                Grid.SetColumn(durationStack, 2);
                grid.Children.Add(durationStack);

                card.Child = grid;
                PluginPerformancePanel.Children.Add(card);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshUI();
        }
    }
}
