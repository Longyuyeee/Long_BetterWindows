using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Host.Views
{
    public partial class PerformancePanel : UserControl
    {
        private readonly PerformanceRefreshCoordinator _coordinator = new();
        private IReadOnlyList<double> _cpuHistory = Array.Empty<double>();

        public PerformancePanel()
        {
            InitializeComponent();
            _coordinator.SnapshotAvailable += Coordinator_SnapshotAvailable;
            Loaded += (_, _) => _coordinator.Start();
            Unloaded += (_, _) => _coordinator.Stop();
        }

        private void Coordinator_SnapshotAvailable(object? sender, PerformanceSnapshot snapshot)
        {
            var summary = snapshot.Summary;
            CpuPercentText.Text = $"{summary.CurrentCpuPercent:F1}%";
            CpuAvgText.Text = $"平均 {summary.AverageCpuPercent:F1}%";
            MemoryText.Text = $"{summary.CurrentMemoryMB:F0} MB";
            ApiCallsText.Text = summary.TotalApiCalls.ToString();
            PluginCountText.Text = $"{summary.PluginCount} 个活跃插件";
            CpuPercentText.SetResourceReference(
                TextBlock.ForegroundProperty,
                summary.CurrentCpuPercent > 80
                    ? "Long.Brush.State.Danger"
                    : summary.CurrentCpuPercent > 50
                        ? "Long.Brush.State.Warning"
                        : "Long.Brush.Accent.Primary");

            _cpuHistory = summary.CpuHistory;
            DrawCpuChart();

            var plugins = HostProvider.Instance.PluginStore.GetAll();
            var rows = snapshot.Plugins.Take(10).Select(data => new PluginPerformanceRow(
                data.PluginId,
                plugins.FirstOrDefault(plugin => plugin.Manifest.Id == data.PluginId)?.Manifest.Name ?? data.PluginId,
                data.TotalApiCalls,
                $"{data.AverageApiDurationMs:F1} ms")).ToArray();

            PluginPerformanceItems.ItemsSource = rows;
            NoDataText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DrawCpuChart()
        {
            CpuChartCanvas.Children.Clear();
            if (_cpuHistory.Count < 2 || CpuChartCanvas.ActualWidth <= 0 || CpuChartCanvas.ActualHeight <= 0)
                return;

            var width = CpuChartCanvas.ActualWidth;
            var height = CpuChartCanvas.ActualHeight;
            var gridBrush = (Brush)FindResource("Long.Brush.Stroke.Default");
            for (var index = 1; index <= 4; index++)
            {
                var y = height * index / 5.0;
                CpuChartCanvas.Children.Add(new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = gridBrush,
                    StrokeThickness = 1,
                });
            }

            var maxValue = Math.Max(_cpuHistory.Max(), 10);
            var stepX = width / (_cpuHistory.Count - 1);
            var line = new Polyline
            {
                Stroke = (Brush)FindResource("Long.Brush.Accent.Primary"),
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
            };

            for (var index = 0; index < _cpuHistory.Count; index++)
            {
                line.Points.Add(new Point(
                    index * stepX,
                    height - (_cpuHistory[index] / maxValue * height)));
            }

            var fill = new Polygon
            {
                Fill = (Brush)FindResource("Long.Brush.Accent.Soft"),
                Points = new PointCollection(line.Points),
            };
            fill.Points.Add(new Point(width, height));
            fill.Points.Add(new Point(0, height));
            CpuChartCanvas.Children.Add(fill);
            CpuChartCanvas.Children.Add(line);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => _coordinator.Refresh();

        private void PerformancePanel_SizeChanged(object sender, SizeChangedEventArgs e) => DrawCpuChart();

        private sealed record PluginPerformanceRow(
            string PluginId,
            string PluginName,
            int CallCount,
            string AverageDuration);
    }
}
