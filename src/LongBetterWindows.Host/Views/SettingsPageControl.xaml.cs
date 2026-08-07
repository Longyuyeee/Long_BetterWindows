using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;
using Serilog;

namespace LongBetterWindows.Host.Views
{
    public partial class SettingsPageControl : UserControl, IDisposable
    {
        private enum UpdateUiState
        {
            Ready,
            Checking,
            Current,
            Available,
            Downloading,
            Downloaded,
            Failed,
        }

        private readonly List<FrameworkElement> _responsiveActions;
        private string _activeCategory = "appearance";
        private bool _categorySelectorReady;
        private UpdateService? _updateService;
        private UpdateCheckResult? _availableUpdate;
        private string? _downloadedUpdatePath;
        private UpdateUiState _updateState;
        private bool _automaticUpdateCheckStarted;
        private bool _languageSelectorReady;
        private bool _isLightMode;
        private bool _disposed;

        public SettingsPageControl()
        {
            InitializeComponent();
            InitializeCategoryNavigation();
            _responsiveActions =
            [
                LanguageSelector,
                ThemeButton,
                MotionToggle,
                UpdateActionButton,
                BrokerActions,
                ClearPreferencesButton,
            ];
            InitializeLanguageSelector();
            _isLightMode =
                Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme()
                == Wpf.Ui.Appearance.ApplicationTheme.Light;
            RefreshThemeButton();
            RefreshMouseGestureControls();
            RefreshBrokerControls();
            RenderUpdateState();
            SizeChanged += OnSizeChanged;
            Loaded += OnLoaded;
            ServicesInitializer.I18n.LanguageChanged += OnLanguageChanged;
        }

        public event EventHandler? LanguageApplied;

        internal bool NavigateToCategory(string category)
        {
            var normalized = category.Trim().ToLowerInvariant();
            if (normalized is not ("appearance" or "interaction"
                or "connections" or "updates"))
            {
                return false;
            }

            _activeCategory = normalized;
            AppearanceCategory.Visibility = normalized == "appearance"
                ? Visibility.Visible
                : Visibility.Collapsed;
            InteractionCategory.Visibility = normalized == "interaction"
                ? Visibility.Visible
                : Visibility.Collapsed;
            ConnectionsCategory.Visibility = normalized == "connections"
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdatesCategory.Visibility = normalized == "updates"
                ? Visibility.Visible
                : Visibility.Collapsed;
            SynchronizeCategorySelectors();
            AutomationProperties.SetHelpText(
                CategoryContent,
                I18n($"settings.category.{normalized}"));
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SizeChanged -= OnSizeChanged;
            Loaded -= OnLoaded;
            ServicesInitializer.I18n.LanguageChanged -= OnLanguageChanged;
            _updateService?.Dispose();
            _updateService = null;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            await StartAutomaticUpdateCheckAsync();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            var compact = e.NewSize.Width < 650;
            foreach (var action in _responsiveActions)
            {
                Grid.SetRow(action, compact ? 1 : 0);
                Grid.SetColumn(action, compact ? 0 : 1);
                action.HorizontalAlignment = compact
                    ? HorizontalAlignment.Left
                    : HorizontalAlignment.Stretch;
                action.Margin = compact
                    ? new Thickness(0, 12, 0, 0)
                    : new Thickness(0);
            }

            var compactNavigation = e.NewSize.Width < 760;
            DesktopCategoryList.Visibility = compactNavigation
                ? Visibility.Collapsed
                : Visibility.Visible;
            CompactCategorySelector.Visibility = compactNavigation
                ? Visibility.Visible
                : Visibility.Collapsed;
            CategoryColumn.Width = compactNavigation
                ? new GridLength(0)
                : new GridLength(200);
            CategoryGapColumn.Width = compactNavigation
                ? new GridLength(0)
                : new GridLength(20);
            Grid.SetRow(CategoryContent, compactNavigation ? 1 : 0);
            Grid.SetRowSpan(CategoryContent, compactNavigation ? 1 : 2);
            Grid.SetColumn(CategoryContent, compactNavigation ? 0 : 2);
            Grid.SetColumnSpan(CategoryContent, compactNavigation ? 3 : 1);
        }

        private void InitializeCategoryNavigation()
        {
            _categorySelectorReady = true;
            NavigateToCategory(_activeCategory);
        }

        private void CategorySelector_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!_categorySelectorReady)
                return;
            var selected = sender switch
            {
                ComboBox combo => combo.SelectedItem as FrameworkElement,
                _ => null,
            };
            if (selected?.Tag is string category
                && !string.Equals(
                    category,
                    _activeCategory,
                    StringComparison.OrdinalIgnoreCase))
            {
                NavigateToCategory(category);
            }
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string category })
                NavigateToCategory(category);
        }

        private void SynchronizeCategorySelectors()
        {
            _categorySelectorReady = false;
            foreach (var button in DesktopCategoryList.Children.OfType<Button>())
            {
                var selected = string.Equals(
                    button.Tag?.ToString(),
                    _activeCategory,
                    StringComparison.OrdinalIgnoreCase);
                button.SetResourceReference(
                    FrameworkElement.StyleProperty,
                    selected
                        ? "SettingsCategoryButton.Selected"
                        : "SettingsCategoryButton");
                AutomationProperties.SetItemStatus(
                    button,
                    selected ? "selected" : "");
            }
            CompactCategorySelector.SelectedItem = CompactCategorySelector.Items
                .OfType<ComboBoxItem>()
                .First(item => string.Equals(
                    item.Tag?.ToString(),
                    _activeCategory,
                    StringComparison.OrdinalIgnoreCase));
            _categorySelectorReady = true;
        }

        private void InitializeLanguageSelector()
        {
            _languageSelectorReady = false;
            var language = ServicesInitializer.I18n.CurrentLanguage;
            LanguageSelector.SelectedItem = LanguageSelector.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(
                    item.Tag?.ToString(),
                    language,
                    StringComparison.OrdinalIgnoreCase));
            _languageSelectorReady = true;
        }

        private void LanguageSelector_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!_languageSelectorReady
                || LanguageSelector.SelectedItem is not ComboBoxItem
                {
                    Tag: string language,
                }
                || string.Equals(
                    language,
                    ServicesInitializer.I18n.CurrentLanguage,
                    StringComparison.OrdinalIgnoreCase))
                return;

            ServicesInitializer.I18n.SetLanguage(language);
            ServicesInitializer.I18n.ApplyTo(Application.Current.Resources);
            LanguageApplied?.Invoke(this, EventArgs.Empty);
        }

        private void OnLanguageChanged(string language)
        {
            if (_disposed) return;
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                InitializeLanguageSelector();
                RefreshThemeButton();
                RefreshMouseGestureControls();
                RefreshBrokerControls();
                RenderUpdateState();
            });
        }

        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            _isLightMode = !_isLightMode;
            App.ApplyTheme(_isLightMode, persist: true);
            RefreshThemeButton();
        }

        private void RefreshThemeButton()
            => ThemeButton.Content = _isLightMode
                ? I18n("action.darkMode")
                : I18n("action.lightMode");

        private void RefreshMouseGestureControls()
        {
            if (_disposed) return;
            var mode = ServicesInitializer.MouseGestures.Mode;
            foreach (var button in MouseGestureButtons.Children.OfType<Button>())
            {
                var active = string.Equals(
                    button.Tag?.ToString(),
                    mode.ToString(),
                    StringComparison.OrdinalIgnoreCase);
                button.SetResourceReference(
                    FrameworkElement.StyleProperty,
                    active ? "LongButton.Primary" : "LongButton");
            }
            MouseGestureStatusText.Text = mode switch
            {
                MouseGestureMode.MiddleButton =>
                    I18n("settings.gesture.status.middle"),
                MouseGestureMode.LongRightPress => string.Format(
                    I18n("settings.gesture.status.longRight"),
                    LongRightPressRecognizer.HoldMilliseconds),
                _ => I18n("settings.gesture.status.disabled"),
            };
        }

        private async void MouseGestureMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string value }
                || !Enum.TryParse<MouseGestureMode>(value, out var mode))
                return;
            MouseGestureButtons.IsEnabled = false;
            try
            {
                await ServicesInitializer.MouseGestures.SetModeAsync(mode);
                RefreshMouseGestureControls();
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Mouse gesture mode update failed");
            }
            finally
            {
                if (!_disposed)
                    MouseGestureButtons.IsEnabled = true;
            }
        }

        private async void ClearSearchPreferences_Click(
            object sender,
            RoutedEventArgs e)
        {
            ClearPreferencesButton.IsEnabled = false;
            try
            {
                await ServicesInitializer.SearchPreferences.ClearAsync();
                await ServicesInitializer.SuperPanelGroups.ClearAsync();
                if (!_disposed)
                {
                    SearchPreferenceStatusText.Text =
                        I18n("settings.searchPreferences.cleared");
                }
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Search preference cleanup failed");
            }
            finally
            {
                if (!_disposed)
                    ClearPreferencesButton.IsEnabled = true;
            }
        }

        private void RefreshBrokerControls(string? status = null)
        {
            var enabled = (Application.Current as App)?.IsPluginBrokerEnabled == true;
            BrokerToggle.IsChecked = enabled;
            AutomationProperties.SetName(
                BrokerToggle,
                I18n(enabled
                    ? "settings.broker.action.disable"
                    : "settings.broker.action.enable"));
            BrokerStatusText.Text = status ?? I18n(enabled
                ? "settings.broker.status.enabled"
                : "settings.broker.status.disabled");
        }

        private async void BrokerToggle_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is not App app) return;
            BrokerActions.IsEnabled = false;
            try
            {
                await app.SetPluginBrokerEnabledAsync(BrokerToggle.IsChecked == true);
                RefreshBrokerControls();
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Plugin broker setting update failed");
                RefreshBrokerControls(I18n("settings.broker.status.failed"));
            }
            finally
            {
                if (!_disposed) BrokerActions.IsEnabled = true;
            }
        }

        private async void ExportBrokerDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is not App app) return;
            BrokerActions.IsEnabled = false;
            try
            {
                var path = await app.ExportPluginBrokerDiagnosticsAsync();
                RefreshBrokerControls(string.Format(
                    I18n("settings.broker.status.exported"), path));
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Plugin broker diagnostics export failed");
                RefreshBrokerControls(I18n("settings.broker.status.failed"));
            }
            finally
            {
                if (!_disposed) BrokerActions.IsEnabled = true;
            }
        }

        private async Task StartAutomaticUpdateCheckAsync()
        {
            if (_automaticUpdateCheckStarted || _disposed) return;
            _automaticUpdateCheckStarted = true;
            await CheckForUpdatesAsync(silent: true);
        }

        private async void UpdateAction_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadedUpdatePath is not null
                && File.Exists(_downloadedUpdatePath))
            {
                OpenUpdatePackage(_downloadedUpdatePath);
                return;
            }
            if (_availableUpdate?.Package is { } package)
            {
                await DownloadUpdateAsync(package);
                return;
            }
            await CheckForUpdatesAsync(silent: false);
        }

        private async Task CheckForUpdatesAsync(bool silent)
        {
            if (_disposed) return;
            if (!silent)
            {
                _updateState = UpdateUiState.Checking;
                RenderUpdateState();
            }
            else
            {
                UpdateActionButton.IsEnabled = false;
            }

            try
            {
                _updateService ??= UpdateService.CreateDefault();
                var includePrereleases = App.ProductVersion.Contains(
                    '-',
                    StringComparison.Ordinal);
                var result = await _updateService.CheckAsync(includePrereleases);
                if (_disposed) return;
                _availableUpdate = result.State == UpdateCheckState.Available
                    ? result
                    : null;
                _downloadedUpdatePath = null;
                _updateState = _availableUpdate is not null
                    ? UpdateUiState.Available
                    : silent
                        ? UpdateUiState.Ready
                        : UpdateUiState.Current;
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Automatic update check failed");
                if (!_disposed && !silent)
                    _updateState = UpdateUiState.Failed;
            }
            finally
            {
                if (!_disposed)
                    RenderUpdateState();
            }
        }

        private async Task DownloadUpdateAsync(UpdatePackage package)
        {
            if (_updateService is null || _disposed) return;
            _updateState = UpdateUiState.Downloading;
            RenderUpdateState();
            try
            {
                var updateDirectory = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "LongBetterWindows",
                    "Updates",
                    _availableUpdate?.AvailableVersion ?? "latest");
                var packagePath = await _updateService.DownloadAsync(
                    package,
                    updateDirectory);
                if (_disposed) return;
                _downloadedUpdatePath = packagePath;
                _availableUpdate = null;
                _updateState = UpdateUiState.Downloaded;
                OpenUpdatePackage(packagePath);
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Verified update download failed");
                if (!_disposed)
                    _updateState = UpdateUiState.Failed;
            }
            finally
            {
                if (!_disposed)
                    RenderUpdateState();
            }
        }

        private void RenderUpdateState()
        {
            if (_disposed) return;
            var actionKey = _updateState switch
            {
                UpdateUiState.Available => "settings.update.action.download",
                UpdateUiState.Downloaded => "settings.update.action.open",
                _ => "settings.update.action.check",
            };
            UpdateActionButton.Content = I18n(actionKey);
            AutomationProperties.SetName(
                UpdateActionButton,
                I18n(actionKey));
            UpdateActionButton.IsEnabled =
                _updateState is not UpdateUiState.Checking
                    and not UpdateUiState.Downloading;
            UpdateStatusText.Text = _updateState switch
            {
                UpdateUiState.Checking =>
                    I18n("settings.update.status.checking"),
                UpdateUiState.Current => string.Format(
                    I18n("settings.update.status.current"),
                    App.ProductVersion),
                UpdateUiState.Available => string.Format(
                    I18n("settings.update.status.available"),
                    _availableUpdate?.AvailableVersion ?? "-"),
                UpdateUiState.Downloading =>
                    I18n("settings.update.status.downloading"),
                UpdateUiState.Downloaded =>
                    I18n("settings.update.status.downloaded"),
                UpdateUiState.Failed =>
                    I18n("settings.update.status.failed"),
                _ => I18n("settings.update.status.ready"),
            };
        }

        private static void OpenUpdatePackage(string packagePath)
        {
            Process.Start(new ProcessStartInfo(
                    "explorer.exe",
                    $"/select,\"{packagePath}\"")
            {
                UseShellExecute = true,
            });
        }

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);
    }
}
