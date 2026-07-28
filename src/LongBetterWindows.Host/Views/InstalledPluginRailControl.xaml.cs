using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views
{
    public partial class InstalledPluginRailControl : UserControl
    {
        private readonly PluginRegistry _plugins = HostProvider.Instance.PluginStore;
        private readonly DispatcherTimer _refreshDebounce;
        private readonly DispatcherTimer _singleClickDelay;
        private IReadOnlyList<MarketplaceEntry> _catalog = [];
        private string? _activePluginId;
        private string? _pendingSettingsPluginId;
        private bool _subscribed;
        private bool _isCompact;
        private bool _suppressNextClick;

        public InstalledPluginRailControl()
        {
            InitializeComponent();
            PluginList.ItemsSource = Items;
            _refreshDebounce = new DispatcherTimer(
                TimeSpan.FromMilliseconds(120),
                DispatcherPriority.Background,
                RefreshDebounce_Tick,
                Dispatcher);
            _singleClickDelay = new DispatcherTimer(
                TimeSpan.FromMilliseconds(220),
                DispatcherPriority.Input,
                SingleClickDelay_Tick,
                Dispatcher);
            Loaded += Rail_Loaded;
            Unloaded += Rail_Unloaded;
            ApplyLanguage();
        }

        internal ObservableCollection<InstalledPluginRailItem> Items { get; } = [];
        internal event Action<string>? PluginSettingsRequested;
        internal event Action<string>? PluginRunRequested;

        internal void SetActivePlugin(string? pluginId)
        {
            if (string.Equals(
                _activePluginId,
                pluginId,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            _activePluginId = pluginId;
            RefreshProjection();
        }

        internal void SetCompact(bool compact)
        {
            if (_isCompact == compact)
                return;
            _isCompact = compact;
            Width = compact ? 64 : 220;
            RailHeader.Visibility = compact
                ? Visibility.Collapsed
                : Visibility.Visible;
            HeaderRow.Height = compact
                ? new GridLength(8)
                : GridLength.Auto;
            PluginList.ItemTemplate = (DataTemplate)Resources[
                compact
                    ? "PluginRailCompactItemTemplate"
                    : "PluginRailWideItemTemplate"];
        }

        internal void ApplyLanguage()
        {
            RailTitle.Text = ServicesInitializer.I18n.T("plugins.installedCount")
                .Replace("{0}", Items.Count.ToString());
            PluginFilterHint.Text = ServicesInitializer.I18n.T("plugins.searchHint");
            System.Windows.Automation.AutomationProperties.SetName(
                PluginFilterBox,
                PluginFilterHint.Text);
            RefreshProjection();
        }

        private async void Rail_Loaded(object sender, RoutedEventArgs e)
        {
            Subscribe();
            RefreshProjection();
            await LoadCatalogAsync();
        }

        private void Rail_Unloaded(object sender, RoutedEventArgs e)
        {
            Unsubscribe();
            _refreshDebounce.Stop();
            _singleClickDelay.Stop();
        }

        private void Subscribe()
        {
            if (_subscribed)
                return;
            _plugins.PluginsChanged += Plugins_PluginsChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;
            _plugins.PluginsChanged -= Plugins_PluginsChanged;
            _subscribed = false;
        }

        private async Task LoadCatalogAsync()
        {
            try
            {
                var path = Path.Combine(
                    AppContext.BaseDirectory,
                    "Marketplace",
                    "registry.json");
                var result = await new LocalMarketplaceRepository(path).LoadAsync();
                if (result.IsSuccess && result.Catalog is not null)
                {
                    _catalog = result.Catalog.Entries;
                    RefreshProjection();
                }
            }
            catch
            {
                // The installed-plugin rail remains fully usable without market metadata.
            }
        }

        private void Plugins_PluginsChanged()
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(ScheduleRefresh);
                return;
            }
            ScheduleRefresh();
        }

        private void ScheduleRefresh()
        {
            _refreshDebounce.Stop();
            _refreshDebounce.Start();
        }

        private void RefreshDebounce_Tick(object? sender, EventArgs e)
        {
            _refreshDebounce.Stop();
            RefreshProjection();
        }

        private void RefreshProjection()
        {
            if (!IsInitialized)
                return;
            var projected = InstalledPluginRailProjection.Build(
                _plugins.GetAll(),
                _catalog,
                PluginFilterBox.Text,
                _activePluginId,
                key => ServicesInitializer.I18n.T(key));
            InstalledPluginRailProjection.Reconcile(Items, projected);
            RailTitle.Text = ServicesInitializer.I18n.T("plugins.installedCount")
                .Replace("{0}", Items.Count.ToString());
        }

        private void PluginFilterBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
            => RefreshProjection();

        private void PluginList_PreviewMouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
                return;
            if (_suppressNextClick)
            {
                _suppressNextClick = false;
                return;
            }
            if (FindItem(e.OriginalSource as DependencyObject) is not { } item)
                return;
            _pendingSettingsPluginId = item.Id;
            _singleClickDelay.Stop();
            _singleClickDelay.Start();
        }

        private void PluginList_PreviewMouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
                return;
            if (FindItem(e.OriginalSource as DependencyObject) is not { } item)
                return;
            _singleClickDelay.Stop();
            _pendingSettingsPluginId = null;
            _suppressNextClick = true;
            e.Handled = true;
            if (item.CanOpen)
                PluginRunRequested?.Invoke(item.Id);
        }

        private void PluginList_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key != Key.Enter
                || PluginList.SelectedItem is not InstalledPluginRailItem item)
            {
                return;
            }
            e.Handled = true;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                if (item.CanOpen)
                    PluginRunRequested?.Invoke(item.Id);
                return;
            }
            PluginSettingsRequested?.Invoke(item.Id);
        }

        private void SingleClickDelay_Tick(object? sender, EventArgs e)
        {
            _singleClickDelay.Stop();
            var pluginId = _pendingSettingsPluginId;
            _pendingSettingsPluginId = null;
            if (!string.IsNullOrWhiteSpace(pluginId))
                PluginSettingsRequested?.Invoke(pluginId);
        }

        private void RunPlugin_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            _singleClickDelay.Stop();
            _pendingSettingsPluginId = null;
            if (sender is Button { Tag: string pluginId })
                PluginRunRequested?.Invoke(pluginId);
        }

        private void PluginIcon_ImageFailed(
            object sender,
            ExceptionRoutedEventArgs e)
        {
            if (sender is Image image)
                image.Source = null;
            e.Handled = true;
        }

        private InstalledPluginRailItem? FindItem(DependencyObject? source)
            => FindAncestor<ListBoxItem>(source)?.DataContext
                as InstalledPluginRailItem;

        private static T? FindAncestor<T>(DependencyObject? source)
            where T : DependencyObject
        {
            while (source is not null)
            {
                if (source is T match)
                    return match;
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }
    }
}
