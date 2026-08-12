using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views
{
    public partial class PerformancePanel : UserControl
    {
        private readonly PerformanceRefreshCoordinator _coordinator = new();
        private IReadOnlyList<double> _cpuHistory = Array.Empty<double>();
        private PerformanceSnapshot? _lastSnapshot;
        private bool _languageSubscribed;

        public PerformancePanel()
        {
            InitializeComponent();
            _coordinator.SnapshotAvailable += Coordinator_SnapshotAvailable;
            Loaded += PerformancePanel_Loaded;
            Unloaded += PerformancePanel_Unloaded;
        }

        private void Coordinator_SnapshotAvailable(object? sender, PerformanceSnapshot snapshot)
        {
            _lastSnapshot = snapshot;
            RenderSnapshot(snapshot);
        }

        private void RenderSnapshot(PerformanceSnapshot snapshot)
        {
            var summary = snapshot.Summary;
            CpuPercentText.Text = $"{summary.CurrentCpuPercent:F1}%";
            CpuAvgText.Text = string.Format(
                I18n("diagnostics.cpu.average"),
                summary.AverageCpuPercent);
            MemoryText.Text = $"{summary.CurrentMemoryMB:F0} MB";
            ApiCallsText.Text = summary.TotalApiCalls.ToString();
            PluginCountText.Text = string.Format(
                I18n("diagnostics.plugins.active"),
                summary.PluginCount);
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
                plugins.FirstOrDefault(plugin => plugin.Manifest.Id == data.PluginId)?.DisplayName ?? data.PluginId,
                data.TotalApiCalls,
                $"{data.AverageApiDurationMs:F1} ms")).ToArray();

            PluginPerformanceItems.ItemsSource = rows;
            NoDataText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PerformancePanel_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_languageSubscribed)
            {
                ServicesInitializer.I18n.LanguageChanged += OnLanguageChanged;
                _languageSubscribed = true;
            }
            ApplyResponsiveLayout(ActualWidth);
            RefreshHealthDiagnostics();
            _coordinator.Start();
        }

        private void PerformancePanel_Unloaded(object sender, RoutedEventArgs e)
        {
            _coordinator.Stop();
            if (!_languageSubscribed) return;
            ServicesInitializer.I18n.LanguageChanged -= OnLanguageChanged;
            _languageSubscribed = false;
        }

        private void OnLanguageChanged(string language)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnLanguageChanged(language));
                return;
            }
            if (_lastSnapshot is not null)
                RenderSnapshot(_lastSnapshot);
            RefreshHealthDiagnostics();
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

        private void RefreshHealthButton_Click(object sender, RoutedEventArgs e)
            => RefreshHealthDiagnostics();

        private void RefreshHealthDiagnostics()
        {
            var diagnostics = PluginRuntimeDiagnostics.Build(
                HostProvider.Instance.PluginStore);
            var rows = PluginRuntimeDiagnosticPresentation.Build(diagnostics, I18n)
                .Select(row => new PluginHealthRow(
                    row.PluginId,
                    row.DisplayName,
                    row.Identity,
                    row.RegistryState,
                    row.HealthState,
                    row.Summary,
                    row.AccessibilityName,
                    row.HealthStateValue))
                .ToArray();
            PluginHealthItems.ItemsSource = rows;
            NoHealthDataText.Visibility = rows.Length == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void PerformancePanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyResponsiveLayout(e.NewSize.Width);
            DrawCpuChart();
        }

        private void ApplyResponsiveLayout(double width)
        {
            var compact = width < 560;
            MetricsGrid.Columns = compact ? 1 : 3;
            CpuMetricCard.Margin = compact
                ? new Thickness(0, 0, 0, 10)
                : new Thickness(0, 0, 12, 0);
            MemoryMetricCard.Margin = compact
                ? new Thickness(0, 0, 0, 10)
                : new Thickness(0, 0, 12, 0);
            CallsMetricCard.Margin = new Thickness(0);
        }

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);

        private sealed record PluginPerformanceRow(
            string PluginId,
            string PluginName,
            int CallCount,
            string AverageDuration);

        private sealed record PluginHealthRow(
            string PluginId,
            string DisplayName,
            string Identity,
            string RegistryState,
            string HealthState,
            string Summary,
            string AccessibilityName,
            PluginRuntimeHealthState HealthStateValue);
    }
}
