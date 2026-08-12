using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views
{
    public partial class PluginManagementControl : UserControl, IDisposable
    {
        private readonly PluginRegistry _pluginStore;
        private CancellationTokenSource? _refreshDebounce;
        private int _disposed;
        private string _workspaceQuery = string.Empty;

        internal event Action<string>? PluginSettingsRequested;

        public PluginManagementControl()
        {
            App.MarkPluginPageStage("plugin_page_constructor_begin");
            InitializeComponent();
            _pluginStore = HostProvider.Instance.PluginStore;
            _pluginStore.PluginsChanged += OnPluginsChanged;
            Loaded += PluginManagementControl_Loaded;
            App.MarkPluginPageStage("plugin_page_constructor_end");
        }

        public void Refresh()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            App.MarkPluginPageStage("plugin_projection_begin");
            var plugins = _pluginStore.GetAll();
            PluginsHeader.Text = string.Format(
                I18n("plugins.installedCount"),
                plugins.Count);

            var filter = _workspaceQuery.Trim();
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
            if (App.IsPluginPagePerformanceTracing)
                App.MarkPluginPageStage(
                    "plugin_projection_end",
                    GetPerformanceMetricsForQuality());
        }

        private void PluginManagementControl_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            Loaded -= PluginManagementControl_Loaded;
            if (!App.IsPluginPagePerformanceTracing)
                return;

            App.MarkPluginPageStage(
                "plugin_page_loaded",
                GetPerformanceMetricsForQuality());
            _ = Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (Volatile.Read(ref _disposed) == 0)
                    {
                        App.MarkPluginPageStage(
                            "plugin_page_first_idle",
                            GetPerformanceMetricsForQuality());
                    }
                }),
                DispatcherPriority.ContextIdle);
        }

        internal PluginPageVisualMetrics GetPerformanceMetricsForQuality()
        {
            var realized = 0;
            for (var index = 0; index < PluginsPanel.Items.Count; index++)
            {
                if (PluginsPanel.ItemContainerGenerator.ContainerFromIndex(index)
                    is not null)
                {
                    realized++;
                }
            }

            var visualMetrics = CountVisualMetrics(this);
            return new PluginPageVisualMetrics(
                PluginsPanel.Items.Count,
                realized,
                visualMetrics.DescendantCount,
                visualMetrics.AnimatedPropertyCount);
        }

        private static (int DescendantCount, int AnimatedPropertyCount)
            CountVisualMetrics(DependencyObject parent)
        {
            var descendants = 0;
            var animatedProperties = 0;
            var childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (var index = 0; index < childCount; index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                descendants++;
                if (child is UIElement
                    {
                        HasAnimatedProperties: true,
                    })
                {
                    animatedProperties++;
                }
                if (child is UIElement element
                    && element.RenderTransform is Animatable
                    {
                        HasAnimatedProperties: true,
                    })
                {
                    animatedProperties++;
                }

                var nested = CountVisualMetrics(child);
                descendants += nested.DescendantCount;
                animatedProperties += nested.AnimatedPropertyCount;
            }
            return (descendants, animatedProperties);
        }

        private void OnPluginsChanged()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            var next = new CancellationTokenSource();
            var previous = Interlocked.Exchange(ref _refreshDebounce, next);
            if (previous is not null)
            {
                previous.Cancel();
                previous.Dispose();
            }
            _ = RefreshAfterPluginChangeAsync(next);
        }

        private async Task RefreshAfterPluginChangeAsync(
            CancellationTokenSource source)
        {
            try
            {
                await Task.Delay(150, source.Token);
                await Dispatcher.InvokeAsync(
                    Refresh,
                    DispatcherPriority.ContextIdle,
                    source.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (InvalidOperationException)
                when (Volatile.Read(ref _disposed) != 0)
            {
            }
            finally
            {
                if (ReferenceEquals(
                        Interlocked.CompareExchange(
                            ref _refreshDebounce,
                            null,
                            source),
                        source))
                {
                    source.Dispose();
                }
            }
        }

        internal void ApplyWorkspaceSearch(string query)
        {
            _workspaceQuery = query ?? string.Empty;
            Refresh();
        }

        private void RefreshPlugins_Click(object sender, RoutedEventArgs e)
            => Refresh();

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
                        PluginSettingsRequested?.Invoke(item.Entry.Id);
                        return Task.CompletedTask;
                    });
            AddMenuAction(
                menu,
                I18n("plugins.capabilities"),
                button,
                () =>
                {
                    PluginSettingsRequested?.Invoke(item.Entry.Id);
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
                await PluginMainUiLauncher.OpenAsync(
                    HostProvider.Instance.PluginStore,
                    item.Entry.Id);
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

        // Retained only for deterministic detached-window quality probes.
        internal static PluginWindowHost CreateSettingsWindow(
            PluginEntry entry,
            IHasSettingsUI settingsUi,
            Window? owner)
        {
            var content = settingsUi.CreateSettingsUI()
                ?? throw new InvalidOperationException(
                    $"Plugin '{entry.Id}' returned an empty settings UI.");
            var scrollViewer = new ScrollViewer
            {
                Content = content,
                Padding = new Thickness(16),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            scrollViewer.SetResourceReference(
                FrameworkElement.StyleProperty,
                "DarkScrollViewerStyle");
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                scrollViewer,
                "Long.Plugin.Settings.Scroll");
            return new PluginWindowHost(
                entry.Id,
                string.Format(
                    I18n("plugins.settingsTitle"),
                    entry.DisplayName),
                scrollViewer,
                entry.Manifest.Window)
            {
                Owner = owner,
                Width = 520,
                Height = 420,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
            };
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
                CanOpenSettings = true;
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
            Loaded -= PluginManagementControl_Loaded;
            var debounce = Interlocked.Exchange(ref _refreshDebounce, null);
            if (debounce is not null)
            {
                debounce.Cancel();
                debounce.Dispose();
            }
            PluginsPanel.ItemsSource = null;
        }

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);
    }
}
