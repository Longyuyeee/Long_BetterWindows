using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views
{
    public partial class PluginManagementControl : UserControl, IDisposable
    {
        private readonly PluginRegistry _pluginStore;
        private DispatcherOperation? _refreshOperation;
        private int _refreshPending;
        private int _disposed;

        public PluginManagementControl()
        {
            InitializeComponent();
            _pluginStore = HostProvider.Instance.PluginStore;
            _pluginStore.PluginsChanged += OnPluginsChanged;
            SizeChanged += PluginManagementControl_SizeChanged;
        }

        public void Refresh()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            var plugins = _pluginStore.GetAll();
            PluginsHeader.Text = string.Format(
                I18n("plugins.installedCount"),
                plugins.Count);

            var filter = PluginSearchBox.Text.Trim();
            var filtered = string.IsNullOrEmpty(filter)
                ? plugins
                : plugins.Where(plugin =>
                    plugin.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || plugin.Manifest.Id.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            var items = filtered.Select(PluginCardItem.Create).ToArray();

            PluginsPanel.ItemsSource = items;
            PluginsPanel.Visibility = items.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyStateText.Text = plugins.Count == 0
                ? I18n("plugins.empty")
                : string.Format(I18n("plugins.noMatch"), filter);
            EmptyStateText.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnPluginsChanged()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            if (Interlocked.Exchange(ref _refreshPending, 1) != 0)
                return;

            var operation = Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    _refreshOperation = null;
                    Interlocked.Exchange(ref _refreshPending, 0);
                    Refresh();
                }),
                DispatcherPriority.ContextIdle);
            _refreshOperation = operation;
            if (Volatile.Read(ref _disposed) != 0
                && operation.Status == DispatcherOperationStatus.Pending)
            {
                operation.Abort();
            }
        }

        private void PluginManagementControl_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
            => ApplyResponsiveLayout(ActualWidth);

        private void ApplyResponsiveLayout(double width)
        {
            var compact = width < 620;
            if (compact)
            {
                PluginHeaderColumn.Width = new GridLength(1, GridUnitType.Star);
                PluginSearchColumn.Width = new GridLength(1, GridUnitType.Star);
                PluginRefreshColumn.Width = GridLength.Auto;
                Grid.SetRow(PluginsHeader, 0);
                Grid.SetColumn(PluginsHeader, 0);
                Grid.SetColumnSpan(PluginsHeader, 3);
                Grid.SetRow(PluginSearchHost, 1);
                Grid.SetColumn(PluginSearchHost, 0);
                Grid.SetColumnSpan(PluginSearchHost, 2);
                PluginSearchHost.Margin = new Thickness(0, 12, 10, 0);
                Grid.SetRow(RefreshPluginsButton, 1);
                Grid.SetColumn(RefreshPluginsButton, 2);
                RefreshPluginsButton.Margin = new Thickness(0, 12, 0, 0);
            }
            else
            {
                PluginHeaderColumn.Width = new GridLength(1, GridUnitType.Star);
                PluginSearchColumn.Width = new GridLength(280);
                PluginRefreshColumn.Width = GridLength.Auto;
                Grid.SetRow(PluginsHeader, 0);
                Grid.SetColumn(PluginsHeader, 0);
                Grid.SetColumnSpan(PluginsHeader, 1);
                Grid.SetRow(PluginSearchHost, 0);
                Grid.SetColumn(PluginSearchHost, 1);
                Grid.SetColumnSpan(PluginSearchHost, 1);
                PluginSearchHost.Margin = new Thickness(0, 0, 10, 0);
                Grid.SetRow(RefreshPluginsButton, 0);
                Grid.SetColumn(RefreshPluginsButton, 2);
                RefreshPluginsButton.Margin = new Thickness(0);
            }
        }

        private void PluginSearch_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

        private void RefreshPlugins_Click(object sender, RoutedEventArgs e)
        {
            PluginSearchBox.Clear();
            Refresh();
        }

        private void PluginActions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: PluginCardItem item } button)
                return;

            var menu = new ContextMenu
            {
                Placement = PlacementMode.Bottom,
                PlacementTarget = button,
            };
            if (item.CanOpen)
                AddMenuAction(
                    menu,
                    I18n("action.open"),
                    button,
                    () => OpenPluginAsync(item));
            if (item.CanOpenSettings)
                AddMenuAction(
                    menu,
                    I18n("plugins.settings"),
                    button,
                    () =>
                    {
                        OpenPluginSettings(item);
                        return Task.CompletedTask;
                    });
            AddMenuAction(
                menu,
                I18n("plugins.capabilities"),
                button,
                () =>
                {
                    OpenCapabilityDetails(item);
                    return Task.CompletedTask;
                });
            AddMenuAction(
                menu,
                item.ToggleText,
                button,
                () => TogglePluginAsync(item));
            menu.IsOpen = true;
        }

        private static void AddMenuAction(
            ItemsControl menu,
            string header,
            Button actionButton,
            Func<Task> action)
        {
            var menuItem = new MenuItem { Header = header };
            menuItem.Click += async (_, _) =>
            {
                actionButton.IsEnabled = false;
                try
                {
                    await action();
                }
                finally
                {
                    actionButton.IsEnabled = true;
                }
            };
            menu.Items.Add(menuItem);
        }

        private async Task OpenPluginAsync(PluginCardItem item)
        {
            try
            {
                var registry = HostProvider.Instance.PluginStore;
                var entry = registry.Get(item.Entry.Id);
                if (entry is null)
                    return;
                if (entry.State != PluginState.Running
                    && !await registry.StartPluginAsync(
                        entry.Id,
                        persistAutoStart: false))
                {
                    return;
                }

                if (entry.Instance is IHasMainUI mainUi)
                    mainUi.ShowMainUI();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Plugin open error: {exception.Message}");
            }
            finally
            {
                Refresh();
            }
        }

        private async Task TogglePluginAsync(PluginCardItem item)
        {
            try
            {
                var registry = HostProvider.Instance.PluginStore;
                var entry = registry.Get(item.Entry.Id);
                if (entry is null) return;

                if (entry.State == PluginState.Running)
                    await registry.StopPluginAsync(entry.Id);
                else
                    await registry.StartPluginAsync(entry.Id);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Plugin toggle error: {exception.Message}");
            }
            finally
            {
                Refresh();
            }
        }

        private void OpenPluginSettings(PluginCardItem item)
        {
            var entry = item.Entry;
            if (entry.Instance is not IHasSettingsUI settingsUi) return;

            try
            {
                var content = settingsUi.CreateSettingsUI();
                if (content is null) return;

                new PluginWindowHost(
                    entry.Id,
                    string.Format(I18n("plugins.settingsTitle"), entry.DisplayName),
                    content,
                    entry.Manifest.Window)
                {
                    Owner = Window.GetWindow(this),
                    Width = 520,
                    Height = 420,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                }.ShowDialog();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Settings UI error: {exception.Message}");
            }
        }

        private void OpenCapabilityDetails(PluginCardItem item)
        {
            var entry = item.Entry;

            var panel = new CapabilityDetailPanel();
            panel.LoadCapabilities(entry.Id, entry.DisplayName, entry.Manifest.Capabilities);
            new PluginWindowHost(
                entry.Id,
                string.Format(I18n("plugins.capabilitiesTitle"), entry.DisplayName),
                panel,
                new PluginWindowPreference
                {
                    Mode = PluginWindowMode.Standard,
                    PreferredWidth = 720,
                    PreferredHeight = 640,
                    MinWidth = 560,
                    MinHeight = 460,
                })
            {
                Owner = Window.GetWindow(this),
                ShowInTaskbar = false,
            }.ShowDialog();
        }

        private sealed class PluginCardItem
        {
            private PluginCardItem(PluginEntry entry)
            {
                Entry = entry;
                Name = entry.DisplayName;
                RuntimeLabel = entry.Manifest.Runtime switch
                {
                    "webview" => "Web",
                    "csharp-script" => "Script",
                    _ => "DLL",
                };
                IsRunning = entry.State == PluginState.Running;
                StatusText = string.Format(
                    I18n(IsRunning
                        ? "plugins.status.runningVersion"
                        : "plugins.status.stoppedVersion"),
                    entry.Manifest.Version);
                ToggleText = I18n(IsRunning
                    ? "action.disable"
                    : "action.enable");
                Hotkey = PluginRegistry.GetPluginHotkey(entry) ?? string.Empty;
                HasHotkey = !string.IsNullOrEmpty(Hotkey);
                var visibleCapabilities = entry.Manifest.Capabilities.Take(3).ToArray();
                HasCapabilities = visibleCapabilities.Length > 0;
                var additionalCount =
                    entry.Manifest.Capabilities.Count - visibleCapabilities.Length;
                CapabilitySummary = string.Join(" · ", visibleCapabilities)
                    + (additionalCount > 0 ? $" · +{additionalCount}" : string.Empty);
                CanOpen = entry.Instance is IHasMainUI
                    || entry.Manifest.Window is not null;
                CanOpenSettings = entry.Instance is IHasSettingsUI;
            }

            public PluginEntry Entry { get; }
            public string Name { get; }
            public string RuntimeLabel { get; }
            public bool IsRunning { get; }
            public string StatusText { get; }
            public string ToggleText { get; }
            public string Hotkey { get; }
            public bool HasHotkey { get; }
            public bool HasCapabilities { get; }
            public string CapabilitySummary { get; }
            public bool CanOpen { get; }
            public bool CanOpenSettings { get; }

            public static PluginCardItem Create(PluginEntry entry) => new(entry);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _pluginStore.PluginsChanged -= OnPluginsChanged;
            SizeChanged -= PluginManagementControl_SizeChanged;
            if (_refreshOperation?.Status == DispatcherOperationStatus.Pending)
                _refreshOperation.Abort();
            _refreshOperation = null;
            Interlocked.Exchange(ref _refreshPending, 0);
            PluginsPanel.ItemsSource = null;
        }

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);
    }
}
