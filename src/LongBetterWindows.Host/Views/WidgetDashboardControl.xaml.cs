using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views
{
    public partial class WidgetDashboardControl : UserControl, IDisposable
    {
        private const double GridRowHeight = 96;
        private readonly Dictionary<string, WidgetCard> _cards =
            new(StringComparer.Ordinal);
        private readonly Action _pluginsChanged;
        private IReadOnlyList<WidgetCatalogItem> _catalog = [];
        private bool _loaded;
        private bool _disposed;

        internal WidgetDashboardControl()
        {
            InitializeComponent();
            _pluginsChanged = () => Dispatcher.InvokeAsync(ReloadAsync);
            Loaded += OnLoaded;
            SizeChanged += (_, _) => ApplyResponsiveLayout(ActualWidth);
            ApplyResponsiveLayout(ActualWidth);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Loaded -= OnLoaded;
            HostProvider.Instance.PluginStore.PluginsChanged -= _pluginsChanged;
            foreach (var card in _cards.Values)
                card.Host.Dispose();
            _cards.Clear();
            SurfaceGrid.Children.Clear();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_disposed || _loaded)
                return;
            _loaded = true;
            HostProvider.Instance.PluginStore.PluginsChanged += _pluginsChanged;
            await ReloadAsync();
        }

        private async Task ReloadAsync()
        {
            if (_disposed)
                return;
            _catalog = WidgetCatalogProjection.Build(
                HostProvider.Instance.PluginStore.GetAll());
            RenderCatalog();
            var loaded = await ServicesInitializer.Widgets.LoadAsync();
            if (!loaded.IsSuccess)
            {
                SetStatus(I18n("widgets.error.load"));
                return;
            }
            if (_disposed)
                return;
            ReconcileSurface(loaded.Snapshot);
            SetStatus(string.Format(
                I18n("widgets.status.ready"),
                loaded.Snapshot.Placements.Count,
                _catalog.Count));
        }

        private void RenderCatalog()
        {
            CatalogItems.Children.Clear();
            CatalogEmptyText.Visibility = _catalog.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            foreach (var item in _catalog)
                CatalogItems.Children.Add(CreateCatalogItem(item));
        }

        private FrameworkElement CreateCatalogItem(WidgetCatalogItem item)
        {
            var title = new TextBlock
            {
                Text = item.Title,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = FindBrush("Long.Brush.Text.Primary"),
            };
            var source = new TextBlock
            {
                Text = $"{item.PluginName} · {item.Definition.DefaultSize!.Columns}×{item.Definition.DefaultSize.Rows}",
                Margin = new Thickness(0, 3, 0, 0),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = FindBrush("Long.Brush.Text.Muted"),
            };
            var text = new StackPanel();
            text.Children.Add(title);
            text.Children.Add(source);

            var add = new Button
            {
                Content = "+",
                Width = 32,
                Height = 32,
                Margin = new Thickness(10, 0, 0, 0),
                Style = FindResource("LongButton.Primary") as Style,
                ToolTip = I18n("widgets.add"),
            };
            AutomationProperties.SetName(add, $"{I18n("widgets.add")} {item.Title}");
            AutomationProperties.SetAutomationId(
                add,
                $"Long.Widgets.Add.{item.PluginId}.{item.WidgetId}");
            add.Click += async (_, _) => await AddAsync(item);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(text);
            Grid.SetColumn(add, 1);
            grid.Children.Add(add);

            if (item.IconPath is not null
                && TryLoadIcon(item.IconPath) is { } icon)
            {
                var withIcon = new Grid();
                withIcon.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                withIcon.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var image = new Image
                {
                    Source = icon,
                    Width = 24,
                    Height = 24,
                    VerticalAlignment = VerticalAlignment.Top,
                };
                withIcon.Children.Add(image);
                Grid.SetColumn(grid, 1);
                withIcon.Children.Add(grid);
                grid = withIcon;
            }

            return new Border
            {
                Child = grid,
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = new CornerRadius(10),
                Background = FindBrush("Long.Brush.Surface.Hover"),
                BorderBrush = FindBrush("Long.Brush.Stroke.Default"),
                BorderThickness = new Thickness(1),
            };
        }

        private async Task AddAsync(WidgetCatalogItem item)
        {
            var result = await ServicesInitializer.Widgets.AddAsync(
                item.PluginId,
                item.WidgetId);
            ApplyMutation(result, I18n("widgets.status.added"));
        }

        private async Task MutateAsync(
            WidgetPlacement placement,
            int columnDelta = 0,
            int rowDelta = 0,
            int columnsDelta = 0,
            int rowsDelta = 0)
        {
            var result = await ServicesInitializer.Widgets.MoveResizeAsync(
                placement.InstanceId,
                placement.Column + columnDelta,
                placement.Row + rowDelta,
                placement.Columns + columnsDelta,
                placement.Rows + rowsDelta);
            ApplyMutation(result, I18n("widgets.status.updated"));
        }

        private async Task RemoveAsync(WidgetPlacement placement)
        {
            var result = await ServicesInitializer.Widgets.RemoveAsync(
                placement.InstanceId);
            ApplyMutation(result, I18n("widgets.status.removed"));
        }

        private void ApplyMutation(
            WidgetLayoutMutationResult result,
            string successMessage)
        {
            if (_disposed)
                return;
            if (!result.IsSuccess)
            {
                SetStatus(I18n($"widgets.error.{result.Error}"));
                return;
            }
            ReconcileSurface(result.Snapshot);
            SetStatus(successMessage);
        }

        private void ReconcileSurface(WidgetLayoutSnapshot snapshot)
        {
            var desiredIds = snapshot.Placements
                .Select(placement => placement.InstanceId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var stale in _cards.Keys.Where(id => !desiredIds.Contains(id)).ToArray())
            {
                var card = _cards[stale];
                SurfaceGrid.Children.Remove(card.Container);
                card.Host.Dispose();
                _cards.Remove(stale);
            }

            foreach (var placement in snapshot.Placements)
            {
                var item = FindCatalogItem(placement);
                var plugin = HostProvider.Instance.PluginStore.Get(placement.PluginId);
                if (item is null || plugin is null)
                    continue;
                if (!_cards.TryGetValue(placement.InstanceId, out var card))
                {
                    card = CreateWidgetCard(plugin, item, placement);
                    _cards.Add(placement.InstanceId, card);
                    SurfaceGrid.Children.Add(card.Container);
                }
                card.Placement = placement;
                card.Host.SetGridSize(placement.Columns, placement.Rows);
                Grid.SetColumn(card.Container, placement.Column);
                Grid.SetRow(card.Container, placement.Row);
                Grid.SetColumnSpan(card.Container, placement.Columns);
                Grid.SetRowSpan(card.Container, placement.Rows);
            }

            EnsureGridDimensions(snapshot);
            SurfaceEmptyPanel.Visibility = snapshot.Placements.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private WidgetCard CreateWidgetCard(
            PluginEntry plugin,
            WidgetCatalogItem item,
            WidgetPlacement placement)
        {
            var session = new WebWidgetSurfaceSession(
                plugin.Manifest,
                plugin.Directory,
                item.Definition,
                placement.InstanceId,
                new WidgetSurfaceLayout(
                    placement.Columns,
                    placement.Rows,
                    Math.Max(1, placement.Columns * 80),
                    Math.Max(1, placement.Rows * GridRowHeight),
                    1));
            var host = new WebWidgetSurfaceHost(
                session,
                placement.Columns,
                placement.Rows);
            var title = new TextBlock
            {
                Text = item.Title,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = FindBrush("Long.Brush.Text.Primary"),
            };
            var menuButton = new Button
            {
                Content = "•••",
                Width = 38,
                Height = 30,
                Style = FindResource("LongButton") as Style,
                ToolTip = I18n("widgets.actions"),
            };
            AutomationProperties.SetName(menuButton, $"{item.Title} {I18n("widgets.actions")}");

            var header = new Grid { Margin = new Thickness(10, 8, 8, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(title);
            Grid.SetColumn(menuButton, 1);
            header.Children.Add(menuButton);

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(header);
            Grid.SetRow(host, 1);
            content.Children.Add(host);

            var border = new Border
            {
                Child = content,
                Margin = new Thickness(4),
                MinHeight = 72,
                ClipToBounds = true,
                CornerRadius = new CornerRadius(12),
                Background = FindBrush("Long.Brush.Surface.Card"),
                BorderBrush = FindBrush("Long.Brush.Stroke.Default"),
                BorderThickness = new Thickness(1),
            };
            AutomationProperties.SetAutomationId(
                border,
                $"Long.Widgets.Instance.{placement.InstanceId}");
            AutomationProperties.SetName(border, item.Title);

            var card = new WidgetCard(border, host, placement);
            menuButton.ContextMenu = CreateActionsMenu(card);
            menuButton.Click += (_, _) =>
            {
                menuButton.ContextMenu.PlacementTarget = menuButton;
                menuButton.ContextMenu.IsOpen = true;
            };
            return card;
        }

        private ContextMenu CreateActionsMenu(WidgetCard card)
        {
            var menu = new ContextMenu();
            AddMenuItem(menu, "← " + I18n("widgets.move.left"), () => MutateAsync(card.Placement, columnDelta: -1));
            AddMenuItem(menu, "→ " + I18n("widgets.move.right"), () => MutateAsync(card.Placement, columnDelta: 1));
            AddMenuItem(menu, "↑ " + I18n("widgets.move.up"), () => MutateAsync(card.Placement, rowDelta: -1));
            AddMenuItem(menu, "↓ " + I18n("widgets.move.down"), () => MutateAsync(card.Placement, rowDelta: 1));
            menu.Items.Add(new Separator());
            AddMenuItem(menu, I18n("widgets.size.wider"), () => MutateAsync(card.Placement, columnsDelta: 1));
            AddMenuItem(menu, I18n("widgets.size.narrower"), () => MutateAsync(card.Placement, columnsDelta: -1));
            AddMenuItem(menu, I18n("widgets.size.taller"), () => MutateAsync(card.Placement, rowsDelta: 1));
            AddMenuItem(menu, I18n("widgets.size.shorter"), () => MutateAsync(card.Placement, rowsDelta: -1));
            menu.Items.Add(new Separator());
            AddMenuItem(menu, I18n("widgets.remove"), () => RemoveAsync(card.Placement));
            return menu;
        }

        private static void AddMenuItem(
            ItemsControl menu,
            string header,
            Func<Task> action)
        {
            var item = new MenuItem { Header = header };
            item.Click += async (_, _) => await action();
            menu.Items.Add(item);
        }

        private void EnsureGridDimensions(WidgetLayoutSnapshot snapshot)
        {
            if (SurfaceGrid.ColumnDefinitions.Count == 0)
            {
                for (var column = 0; column < 24; column++)
                {
                    SurfaceGrid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(1, GridUnitType.Star),
                    });
                }
            }
            var rowCount = Math.Max(
                3,
                snapshot.Placements.Count == 0
                    ? 3
                    : snapshot.Placements.Max(item => item.Row + item.Rows));
            while (SurfaceGrid.RowDefinitions.Count < rowCount)
                SurfaceGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(GridRowHeight) });
            while (SurfaceGrid.RowDefinitions.Count > rowCount)
                SurfaceGrid.RowDefinitions.RemoveAt(SurfaceGrid.RowDefinitions.Count - 1);
            SurfaceGrid.MinHeight = rowCount * GridRowHeight;
        }

        private WidgetCatalogItem? FindCatalogItem(WidgetPlacement placement)
            => _catalog.FirstOrDefault(item =>
                string.Equals(item.PluginId, placement.PluginId, StringComparison.Ordinal)
                && string.Equals(item.WidgetId, placement.WidgetId, StringComparison.Ordinal));

        private async void Refresh_Click(object sender, RoutedEventArgs e)
            => await ReloadAsync();

        private void ApplyResponsiveLayout(double width)
        {
            var narrow = width > 0 && width < 900;
            CatalogColumn.Width = narrow
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(292);
            WideGapColumn.Width = narrow ? new GridLength(0) : new GridLength(16);
            SurfaceColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            CatalogRow.Height = GridLength.Auto;
            NarrowGapRow.Height = narrow ? new GridLength(16) : new GridLength(0);
            SurfaceRow.Height = narrow ? GridLength.Auto : new GridLength(0);
            Grid.SetRow(SurfaceCard, narrow ? 2 : 0);
            Grid.SetColumn(SurfaceCard, narrow ? 0 : 2);
        }

        private void SetStatus(string message)
            => StatusText.Text = message;

        private Brush? FindBrush(string key)
            => TryFindResource(key) as Brush;

        private static ImageSource? TryLoadIcon(string path)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception exception)
                when (exception is IOException
                    or NotSupportedException
                    or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);

        private sealed class WidgetCard(
            Border container,
            WebWidgetSurfaceHost host,
            WidgetPlacement placement)
        {
            internal Border Container { get; } = container;
            internal WebWidgetSurfaceHost Host { get; } = host;
            internal WidgetPlacement Placement { get; set; } = placement;
        }
    }
}
