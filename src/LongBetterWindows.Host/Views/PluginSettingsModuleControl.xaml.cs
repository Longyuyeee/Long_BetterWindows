using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;
using Serilog;

namespace LongBetterWindows.Host.Views
{
    public partial class PluginSettingsModuleControl : UserControl, IDisposable
    {
        private readonly PluginRegistry _plugins;
        private readonly SearchPreferenceService _preferences;
        private readonly string _pluginId;
        private FrameworkElement? _settingsContent;
        private long _settingsRevision = -1;
        private bool _subscribed;
        private int _disposed;

        internal PluginSettingsModuleControl(
            string pluginId,
            PluginRegistry? plugins = null,
            SearchPreferenceService? preferences = null)
        {
            _pluginId = pluginId;
            _plugins = plugins ?? HostProvider.Instance.PluginStore;
            _preferences = preferences ?? ServicesInitializer.SearchPreferences;
            InitializeComponent();
            Loaded += Module_Loaded;
            Unloaded += Module_Unloaded;
        }

        internal string PluginId => _pluginId;
        internal event Action<string>? PluginRunRequested;
        internal event Action<string>? PluginToggleRequested;
        internal event Action<string>? PluginUnavailable;
        internal event Action<PluginSettingsModuleState>? StateChanged;

        internal bool ShowCapabilities
        {
            get => ModuleTabs.SelectedItem == CapabilitiesTab;
            set => ModuleTabs.SelectedItem = value
                ? CapabilitiesTab
                : SettingsTab;
        }

        internal void ApplyLanguage()
        {
            CapabilityPanel.ApplyLanguage();
            Refresh();
        }

        internal void Refresh()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            var entry = _plugins.Get(_pluginId);
            if (entry is null)
            {
                DisableUnavailable();
                PluginUnavailable?.Invoke(_pluginId);
                return;
            }

            var state = PluginSettingsModuleProjection.Build(
                entry,
                CapabilityUsageTracker.Instance.GetStatsSnapshot(_pluginId),
                key => ServicesInitializer.I18n.T(key));
            RuntimeText.Text = state.RuntimeLabel;
            StatusText.Text = state.StatusText;
            StateDot.SetResourceReference(
                Shape.FillProperty,
                state.IsRunning
                    ? "Long.Brush.State.Success"
                    : "Long.Brush.Text.Muted");
            HotkeyText.Text = state.HasHotkey ? state.Hotkey : string.Empty;
            HotkeyText.Visibility = state.HasHotkey
                ? Visibility.Visible
                : Visibility.Collapsed;
            ToggleButton.Content = state.ToggleText;
            ToggleButton.IsEnabled = true;
            RunButton.IsEnabled = state.CanOpen;
            RunButtonText.Text = ServicesInitializer.I18n.T("plugins.open");
            System.Windows.Automation.AutomationProperties.SetName(
                ToggleButton,
                state.ToggleText);
            System.Windows.Automation.AutomationProperties.SetName(
                RunButton,
                RunButtonText.Text);
            CapabilityPanel.LoadCapabilities(
                entry.Id,
                entry.DisplayName,
                entry.Manifest.Capabilities);
            RefreshCommands(entry);
            RefreshSettingsContent(entry, state);
            StateChanged?.Invoke(state);
        }

        private void RefreshCommands(PluginEntry entry)
        {
            var commands = PluginSettingsModuleProjection.BuildCommands(
                entry,
                _plugins.Commands.GetAll(),
                _preferences.GetPinnedResultIds(),
                key => ServicesInitializer.I18n.T(key));
            CommandsList.ItemsSource = commands;
            CommandsList.Visibility = commands.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            CommandsEmptyText.Visibility = commands.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void RefreshSettingsContent(
            PluginEntry entry,
            PluginSettingsModuleState state)
        {
            if (!state.HasSettings)
            {
                SettingsHost.Content = null;
                _settingsContent = null;
                _settingsRevision = entry.RegistrationRevision;
                SettingsUnavailableText.Visibility = Visibility.Visible;
                return;
            }
            if (_settingsContent is not null
                && _settingsRevision == entry.RegistrationRevision)
            {
                SettingsUnavailableText.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                _settingsContent = ((IHasSettingsUI)entry.Instance!).CreateSettingsUI()
                    ?? throw new InvalidOperationException(
                        $"Plugin '{entry.Id}' returned an empty settings UI.");
                _settingsRevision = entry.RegistrationRevision;
                SettingsHost.Content = _settingsContent;
                SettingsUnavailableText.Visibility = Visibility.Collapsed;
            }
            catch (Exception exception)
            {
                Log.Warning(
                    exception,
                    "Plugin {PluginId} settings UI could not be embedded",
                    entry.Id);
                _settingsContent = null;
                SettingsHost.Content = null;
                SettingsUnavailableText.Visibility = Visibility.Visible;
            }
        }

        private void Module_Loaded(object sender, RoutedEventArgs e)
        {
            Subscribe();
            Refresh();
        }

        private void Module_Unloaded(object sender, RoutedEventArgs e)
            => Unsubscribe();

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

        private void Plugins_PluginsChanged()
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(Refresh);
                return;
            }
            Refresh();
        }

        private void RunButton_Click(object sender, RoutedEventArgs e)
            => PluginRunRequested?.Invoke(_pluginId);

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleButton.IsEnabled = false;
            PluginToggleRequested?.Invoke(_pluginId);
        }

        private async void CommandPin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string resultId } button)
                return;
            button.IsEnabled = false;
            try
            {
                await _preferences.TogglePinnedAsync(resultId);
                if (Volatile.Read(ref _disposed) == 0)
                    Refresh();
            }
            catch (Exception exception)
            {
                Log.Warning(
                    exception,
                    "Plugin command pin update failed for {ResultId}",
                    resultId);
                if (Volatile.Read(ref _disposed) == 0)
                    button.IsEnabled = true;
            }
        }

        private void ModuleTabs_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (ReferenceEquals(ModuleTabs.SelectedItem, CapabilitiesTab))
                Refresh();
        }

        private void DisableUnavailable()
        {
            ToggleButton.IsEnabled = false;
            RunButton.IsEnabled = false;
            SettingsHost.Content = null;
            CommandsList.ItemsSource = null;
            _settingsContent = null;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            Unsubscribe();
            Loaded -= Module_Loaded;
            Unloaded -= Module_Unloaded;
            SettingsHost.Content = null;
            _settingsContent = null;
        }
    }
}
