using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;
using Microsoft.Win32;

namespace LongBetterWindows.Host.Views
{
    public partial class MarketplaceControl : UserControl
    {
        private readonly MarketplaceRuntimeService _marketplace;
        private readonly MarketplaceSessionCoordinator _session;
        private bool _pluginEventsSubscribed;
        private bool _languageEventsSubscribed;
        private bool _isCompactLayout;
        private bool _forceListForQuality;
        private bool _catalogDegraded;
        private string _workspaceQuery = string.Empty;
        private readonly MarketplaceModuleRouter _moduleRouter = new();
        private IInputElement? _confirmationFocusOrigin;
        private MarketplaceOperationPresentation? _pendingOperationPresentation;
        private string? _operationStatus;
        private MarketplaceEntry? _selectedEntry;
        private MarketplacePackageVersion? _selectedVersion;
        private IReadOnlyList<MarketplacePackageVersion> _displayedVersions =
            Array.Empty<MarketplacePackageVersion>();

        public MarketplaceControl()
        {
            InitializeComponent();
            var qualityCatalog = (Application.Current as App)?.QualityMarketplaceCatalogPath;
            var catalogPath = string.IsNullOrWhiteSpace(qualityCatalog)
                ? Path.Combine(AppContext.BaseDirectory, "Marketplace", "registry.json")
                : Path.GetFullPath(qualityCatalog);
            var marketDir = Path.Combine(AppContext.BaseDirectory, "Marketplace");
            var qualityTrustStore = (Application.Current as App)?.QualityMarketplaceTrustStorePath;
            var trustStorePath = string.IsNullOrWhiteSpace(qualityTrustStore)
                ? Path.Combine(marketDir, "trusted-publishers.json")
                : Path.GetFullPath(qualityTrustStore);
            var dataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LongBetterWindows", "Marketplace");
            _marketplace = new MarketplaceRuntimeService(
                catalogPath,
                string.IsNullOrWhiteSpace(qualityCatalog)
                    ? MarketplaceSourceKind.LocalPackage
                    : MarketplaceSourceKind.RemoteRegistry,
                Path.Combine(marketDir, "marketplace-settings.json"),
                trustStorePath,
                dataRoot,
                App.ProductVersion);
            _session = new MarketplaceSessionCoordinator(
                _marketplace,
                pluginId => HostProvider.Instance.PluginStore.Get(pluginId)?.Manifest);
            SizeChanged += (_, _) => ApplyResponsiveLayout(ActualWidth);
            Loaded += MarketplaceControl_Loaded;
            Unloaded += MarketplaceControl_Unloaded;
            Dispatcher.ShutdownStarted += (_, _) =>
            {
                _session.Dispose();
                _marketplace.Dispose();
            };
        }

        private async void MarketplaceControl_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyResponsiveLayout(ActualWidth);
            if (!_pluginEventsSubscribed)
            {
                HostProvider.Instance.PluginStore.PluginsChanged += OnPluginsChanged;
                _pluginEventsSubscribed = true;
            }
            if (!_languageEventsSubscribed)
            {
                ServicesInitializer.I18n.LanguageChanged += OnLanguageChanged;
                _languageEventsSubscribed = true;
            }

            await LoadCatalogAsync();
        }

        private void MarketplaceControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _session.CancelActiveRequests();
            if (!_pluginEventsSubscribed) return;
            HostProvider.Instance.PluginStore.PluginsChanged -= OnPluginsChanged;
            _pluginEventsSubscribed = false;
            if (_languageEventsSubscribed)
            {
                ServicesInitializer.I18n.LanguageChanged -= OnLanguageChanged;
                _languageEventsSubscribed = false;
            }
        }

        private void OnLanguageChanged(string language)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnLanguageChanged(language));
                return;
            }
            _ = LoadCatalogAsync();
        }

        private void ApplyResponsiveLayout(double width)
        {
            var hostWidth = Window.GetWindow(this)?.ActualWidth ?? double.PositiveInfinity;
            var compact = width < 760 || hostWidth < 900;
            _isCompactLayout = compact;

            if (compact)
            {
                MarketHeroCard.Visibility = Visibility.Collapsed;
                CompactImportLocalPackageButton.Visibility = Visibility.Visible;
                CatalogStatePanel.VerticalAlignment = VerticalAlignment.Top;
                CatalogStatePanel.Margin = new Thickness(20, 56, 20, 20);
                ConfirmCard.Padding = new Thickness(20);
                ConfirmCard.VerticalAlignment = VerticalAlignment.Top;
                MarketHeroTextColumn.Width = new GridLength(1, GridUnitType.Star);
                MarketHeroActionColumn.Width = new GridLength(0);
                Grid.SetRow(ImportLocalPackageButton, 1);
                Grid.SetColumn(ImportLocalPackageButton, 0);
                ImportLocalPackageButton.HorizontalAlignment = HorizontalAlignment.Left;
                ImportLocalPackageButton.Margin = new Thickness(0, 14, 0, 0);

                MarketSearchColumn.Width = new GridLength(1, GridUnitType.Star);
                MarketCategoryColumn.Width = new GridLength(1, GridUnitType.Star);
                MarketRefreshColumn.Width = GridLength.Auto;
                Grid.SetRow(CategoryBox, 0);
                Grid.SetColumn(CategoryBox, 0);
                Grid.SetColumnSpan(CategoryBox, 2);
                CategoryBox.Margin = new Thickness(0, 0, 10, 0);
                Grid.SetRow(RefreshCatalogButton, 0);
                Grid.SetColumn(RefreshCatalogButton, 2);
                RefreshCatalogButton.Margin = new Thickness(0);

                MarketListColumn.Width = new GridLength(1, GridUnitType.Star);
                MarketBodyGapColumn.Width = new GridLength(0);
                MarketDetailColumn.Width = new GridLength(0);
                Grid.SetColumn(MarketDetailCard, 0);
                Grid.SetColumnSpan(MarketDetailCard, 3);
                MarketBackButton.Visibility = Visibility.Visible;
                MarketListCard.Visibility = _forceListForQuality
                    || _moduleRouter.Route.Kind == MarketplaceModuleRouteKind.Catalog
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                MarketDetailCard.Visibility = _forceListForQuality
                    || _moduleRouter.Route.Kind == MarketplaceModuleRouteKind.Catalog
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
            else
            {
                MarketHeroCard.Visibility = Visibility.Visible;
                CompactImportLocalPackageButton.Visibility = Visibility.Collapsed;
                CatalogStatePanel.VerticalAlignment = VerticalAlignment.Center;
                CatalogStatePanel.Margin = new Thickness(20);
                ConfirmCard.Padding = new Thickness(26);
                ConfirmCard.VerticalAlignment = VerticalAlignment.Center;
                MarketHeroTextColumn.Width = new GridLength(1, GridUnitType.Star);
                MarketHeroActionColumn.Width = GridLength.Auto;
                Grid.SetRow(ImportLocalPackageButton, 0);
                Grid.SetColumn(ImportLocalPackageButton, 1);
                ImportLocalPackageButton.HorizontalAlignment = HorizontalAlignment.Stretch;
                ImportLocalPackageButton.Margin = new Thickness(0);

                MarketSearchColumn.Width = new GridLength(1, GridUnitType.Star);
                MarketCategoryColumn.Width = new GridLength(190);
                MarketRefreshColumn.Width = GridLength.Auto;
                Grid.SetRow(CategoryBox, 0);
                Grid.SetColumn(CategoryBox, 1);
                Grid.SetColumnSpan(CategoryBox, 1);
                CategoryBox.Margin = new Thickness(10, 0, 10, 0);
                Grid.SetRow(RefreshCatalogButton, 0);
                Grid.SetColumn(RefreshCatalogButton, 2);
                RefreshCatalogButton.Margin = new Thickness(0);

                MarketListColumn.Width = new GridLength(330);
                MarketBodyGapColumn.Width = new GridLength(14);
                MarketDetailColumn.Width = new GridLength(1, GridUnitType.Star);
                Grid.SetColumn(MarketDetailCard, 2);
                Grid.SetColumnSpan(MarketDetailCard, 1);
                MarketBackButton.Visibility = Visibility.Collapsed;
                MarketListCard.Visibility = Visibility.Visible;
                MarketDetailCard.Visibility = Visibility.Visible;
            }
        }

        private async Task LoadCatalogAsync()
        {
            CatalogStatusText.Text = I18n("market.status.loading");
            ApplyCatalogViewState(MarketplaceCatalogViewStatePresenter.Loading());
            var load = await _session.LoadCatalogAsync();
            if (load.IsSuperseded || load.Result == null) return;
            var result = load.Result;
            if (!result.IsSuccess)
            {
                _catalogDegraded = false;
                MarketList.ItemsSource = Array.Empty<MarketCardModel>();
                ResultCountText.Text = I18n("market.status.offline");
                CatalogStatusText.Text = I18n(
                    MarketplacePresentation.GetErrorResourceKey(result.ErrorCode));
                MarketSourceBadge.Text = I18n("market.source.offline");
                CategoryBox.ItemsSource = new[] { I18n("market.allCategories") };
                CategoryBox.SelectedIndex = 0;
                ApplyCatalogViewState(
                    MarketplaceCatalogViewStatePresenter.FromLoad(result));
                ShowEmptyDetail();
                return;
            }

            var catalog = _session.Catalog!;
            _catalogDegraded = result.IsFallback;
            MarketSourceBadge.Text = catalog.Source == MarketplaceSourceKind.RemoteRegistry
                ? I18n("market.source.remote")
                : I18n("market.source.local");
            CategoryBox.ItemsSource = new[] { I18n("market.allCategories") }
                .Concat(MarketplacePresentation.GetCategories(catalog))
                .ToArray();
            CategoryBox.SelectedIndex = 0;
            CatalogStatusText.Text = result.IsFallback
                ? I18n("market.status.catalogFallback")
                : string.Format(
                    I18n("market.status.generated"),
                    catalog.GeneratedAt.ToString("yyyy-MM-dd"),
                    catalog.SchemaVersion);
            await ApplyFiltersAsync();
            _ = Dispatcher.BeginInvoke(
                new Action(() => BringIntoView(new Rect(0, 0, 1, 1))),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private Task ApplyFiltersAsync()
        {
            var catalog = _session.Catalog;
            if (catalog == null) return Task.CompletedTask;
            var category = CategoryBox.SelectedIndex <= 0
                ? null
                : CategoryBox.SelectedItem?.ToString();
            var cards = MarketplacePresentation.ProjectEntries(
                catalog,
                _workspaceQuery,
                category,
                pluginId => HostProvider.Instance.PluginStore
                    .Get(pluginId)?.Manifest.Version);
            MarketList.ItemsSource = cards;
            ApplyCatalogViewState(MarketplaceCatalogViewStatePresenter.FromFilter(
                cards.Count,
                !string.IsNullOrWhiteSpace(_workspaceQuery) || category != null,
                _catalogDegraded));
            ResultCountText.Text = string.Format(
                I18n("market.results.count"),
                cards.Count);
            var availableIds = cards
                .Select(card => card.Entry.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (_moduleRouter.Reconcile(availableIds))
                ShowCatalogRoute(clearSelection: true);
            if (_selectedEntry != null)
            {
                var selected = cards.FirstOrDefault(x => string.Equals(
                    x.Entry.Id, _selectedEntry.Id, StringComparison.OrdinalIgnoreCase));
                if (selected != null) MarketList.SelectedItem = selected;
                else ShowEmptyDetail();
            }
            else if (cards.Count > 0 && !_isCompactLayout)
            {
                MarketList.SelectedIndex = 0;
            }
            else if (_isCompactLayout)
            {
                ShowEmptyDetail();
            }
            return Task.CompletedTask;
        }

        private void ApplyCatalogViewState(MarketplaceCatalogViewState state)
        {
            MarketList.Visibility = state.ShowsCatalog
                ? Visibility.Visible
                : Visibility.Collapsed;
            CatalogStatePanel.Visibility = state.ShowsBlockingState
                ? Visibility.Visible
                : Visibility.Collapsed;
            CatalogNotice.Visibility = state.ShowsNotice
                ? Visibility.Visible
                : Visibility.Collapsed;
            CatalogStateProgress.Visibility = state.ShowsProgress
                ? Visibility.Visible
                : Visibility.Collapsed;
            CatalogStateRetryButton.Visibility = state.CanRetry
                ? Visibility.Visible
                : Visibility.Collapsed;
            CatalogNoticeRetryButton.Visibility = state.CanRetry
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (!string.IsNullOrWhiteSpace(state.TitleResourceKey))
            {
                var title = I18n(state.TitleResourceKey);
                var description = I18n(state.DescriptionResourceKey);
                CatalogStateTitle.Text = title;
                CatalogStateDescription.Text = description;
                CatalogNoticeTitle.Text = title;
                CatalogNoticeDescription.Text = description;
                AutomationProperties.SetName(CatalogStatePanel, $"{title}. {description}");
            }
        }

        private void ShowEntry(MarketCardModel card)
        {
            if (!string.Equals(
                    _selectedEntry?.Id,
                    card.Entry.Id,
                    StringComparison.OrdinalIgnoreCase))
            {
                _operationStatus = null;
            }
            _selectedEntry = card.Entry;
            _moduleRouter.OpenDetail(card.Entry.Id);
            MarketEmptyDetail.Visibility = Visibility.Collapsed;
            MarketDetail.Visibility = Visibility.Visible;
            if (_isCompactLayout)
            {
                MarketListCard.Visibility = Visibility.Collapsed;
                MarketDetailCard.Visibility = Visibility.Visible;
            }
            DetailMonogram.Text = card.Monogram;
            DetailName.Text = card.Name;
            DetailPublisher.Text = $"{card.Entry.Publisher} · {card.Entry.Category}";
            DetailDescription.Text = string.IsNullOrWhiteSpace(card.Entry.Description)
                ? card.Entry.Summary
                : card.Entry.Description;
            DetailState.Text = StateLabel(card.State);
            _displayedVersions = card.Entry.Versions
                .OrderByDescending(x => MarketplacePresentation.ParseVersion(x.Version))
                .ToArray();
            VersionBox.ItemsSource = _displayedVersions
                .Select(version => version.Version)
                .ToArray();
            VersionBox.SelectedIndex = VersionBox.Items.Count > 0 ? 0 : -1;
            UninstallButton.Visibility = card.InstalledVersion == null
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void ShowVersion(MarketplacePackageVersion? version)
        {
            _selectedVersion = version;
            if (version == null)
            {
                CapabilityItems.ItemsSource = Array.Empty<string>();
                InstallButton.IsEnabled = false;
                return;
            }

            CapabilityItems.ItemsSource = version.Capabilities.Count == 0
                ? new[] { I18n("market.permissions.none") }
                : version.Capabilities;
            DetailTrust.Text = !string.IsNullOrWhiteSpace(version.PublisherKeyId)
                ? string.Format(
                    I18n("market.trust.publisherSigned"),
                    version.PublisherKeyId)
                : I18n("market.trust.catalogOnly");
            ReleaseNotesText.Text = string.IsNullOrWhiteSpace(version.ReleaseNotes)
                ? I18n("market.releaseNotes.empty")
                : version.ReleaseNotes;

            var compatibility = MarketplacePresentation.GetCompatibility(
                version, App.ProductVersion);
            CompatibilityTitle.Text = compatibility.IsCompatible
                ? I18n("market.compat.compatible")
                : I18n("market.compat.incompatible");
            CompatibilityTitle.SetResourceReference(
                ForegroundProperty,
                compatibility.IsCompatible ? "Long.Brush.State.Success" : "Long.Brush.State.Danger");
            CompatibilityText.Text = compatibility.Requirements.Count == 0
                ? I18n("market.compat.default")
                : string.Join(" · ", compatibility.Requirements);
            InstallButton.IsEnabled = compatibility.IsCompatible;
            var operation = MarketplaceOperationPresenter.ForInstall(
                HostProvider.Instance.PluginStore.Get(_selectedEntry?.Id ?? string.Empty)
                    ?.Manifest.Version,
                version.Version);
            InstallButton.Content = version.PackageUri?.Scheme == Uri.UriSchemeHttps
                ? I18n(operation.RemoteActionResourceKey)
                : version.PackageUri?.IsFile == true
                    ? I18n(operation.LocalActionResourceKey)
                    : I18n("market.choosePackage");
            if (_operationStatus == null)
            {
                DetailHint.Text = version.PackageUri == null
                    ? I18n("market.hint.chooseLocal")
                    : version.PackageUri.IsFile
                        ? I18n("market.hint.verifyBeforeInstall")
                        : !_marketplace.CanDownload
                            ? I18n("market.hint.downloadUnavailable")
                            : I18n("market.hint.secureDownload");
            }
        }

        private async Task PreviewPackageAsync(
            string path,
            MarketplacePackageMetadata metadata)
        {
            var preparation = await _session.PrepareLocalPackageAsync(path, metadata);
            ShowPreparation(preparation, I18n("market.error.packageRejected"));
        }

        private void ShowInstallConfirmation(MarketplacePendingAction pending)
        {
            RememberConfirmationFocus();
            var validation = pending.Validation!;
            var manifest = validation.Manifest!;
            _pendingOperationPresentation = MarketplaceOperationPresenter.ForInstall(
                HostProvider.Instance.PluginStore.Get(manifest.Id)?.Manifest.Version,
                manifest.Version);
            ConfirmTitle.Text = I18n(
                _pendingOperationPresentation.ReviewTitleResourceKey);
            ConfirmSubtitle.Text = $"{manifest.Name} · v{manifest.Version}";
            ConfirmTrustText.Text = validation.TrustLevel == PackageTrustLevel.PublisherSigned
                ? I18n("market.confirm.publisherVerified")
                : I18n("market.confirm.localUnsigned");
            AutomationProperties.SetItemStatus(
                ConfirmTrustText, validation.TrustLevel.ToString());
            ConfirmHashText.Text = $"SHA-256  {validation.Sha256}";
            ConfirmCompatibilityText.Text = I18n("market.confirm.compatibilityPassed");
            PermissionDiffItems.ItemsSource = FormatPermissionDiff(
                validation.PermissionDiff);
            HighTrustWarning.Visibility = validation.RequiresHighTrustWarning
                ? Visibility.Visible
                : Visibility.Collapsed;
            ConfirmErrorText.Text = string.Empty;
            ConfirmOperationStatusText.Text = string.Empty;
            ConfirmActionButton.Content = I18n(
                _pendingOperationPresentation.ConfirmActionResourceKey);
            ConfirmActionButton.IsEnabled = true;
            ConfirmActionButton.SetResourceReference(StyleProperty, "LongButton.Primary");
            ConfirmOverlay.Visibility = Visibility.Visible;
            FocusConfirmationAction();
        }

        private void PreviewUninstall()
        {
            if (_selectedEntry == null) return;
            _operationStatus = null;
            var installed = HostProvider.Instance.PluginStore.Get(_selectedEntry.Id)?.Manifest;
            if (installed == null) return;
            var preparation = _session.PrepareUninstall(installed);
            if (!preparation.IsSuccess)
            {
                DetailHint.Text = I18n(
                    MarketplacePresentation.GetErrorResourceKey(preparation.ErrorCode));
                return;
            }
            RememberConfirmationFocus();
            _pendingOperationPresentation = MarketplaceOperationPresenter.ForUninstall();
            ConfirmTitle.Text = I18n(
                _pendingOperationPresentation.ReviewTitleResourceKey);
            ConfirmSubtitle.Text = $"{installed.Name} · v{installed.Version}";
            ConfirmTrustText.Text = I18n("market.confirm.rollbackRemoval");
            ConfirmHashText.Text = installed.Id;
            ConfirmCompatibilityText.Text = I18n("market.confirm.rollbackGuarantee");
            PermissionDiffItems.ItemsSource = installed.Capabilities.Count == 0
                ? new[] { I18n("market.permission.noAuthorized") }
                : installed.Capabilities
                    .OrderBy(x => x)
                    .Select(x => string.Format(
                        I18n("market.permission.removed"),
                        x))
                    .ToArray();
            HighTrustWarning.Visibility = Visibility.Collapsed;
            ConfirmErrorText.Text = string.Empty;
            ConfirmOperationStatusText.Text = string.Empty;
            ConfirmActionButton.Content = I18n(
                _pendingOperationPresentation.ConfirmActionResourceKey);
            ConfirmActionButton.IsEnabled = true;
            ConfirmActionButton.SetResourceReference(StyleProperty, "LongButton.Danger");
            ConfirmOverlay.Visibility = Visibility.Visible;
            FocusConfirmationAction();
        }

        private void ShowConfirmationError(
            string title,
            MarketplaceErrorCode errorCode)
        {
            RememberConfirmationFocus();
            var message = I18n(
                MarketplacePresentation.GetErrorResourceKey(errorCode));
            ConfirmTitle.Text = title;
            ConfirmSubtitle.Text = I18n("market.confirm.blocked");
            ConfirmTrustText.Text = I18n("market.confirm.validationFailed");
            AutomationProperties.SetItemStatus(
                ConfirmTrustText,
                MarketplacePresentation.GetErrorAutomationStatus(errorCode));
            ConfirmHashText.Text = string.Empty;
            ConfirmCompatibilityText.Text = message;
            PermissionDiffItems.ItemsSource = Array.Empty<string>();
            HighTrustWarning.Visibility = Visibility.Collapsed;
            ConfirmErrorText.Text = message;
            ConfirmOperationStatusText.Text = string.Empty;
            _pendingOperationPresentation = null;
            ConfirmActionButton.IsEnabled = false;
            ConfirmOverlay.Visibility = Visibility.Visible;
            _ = Dispatcher.BeginInvoke(
                () => ConfirmCancelButton.Focus(),
                DispatcherPriority.Input);
        }

        private async void ConfirmAction_Click(object sender, RoutedEventArgs e)
        {
            var installer = App.PackageInstaller;
            if (installer == null)
            {
                ConfirmErrorText.Text = I18n("market.error.engineStarting");
                return;
            }

            var operation = _pendingOperationPresentation
                ?? MarketplaceOperationPresenter.ForInstall(null, "0.0.0");
            SetBusy(true, operation);
            try
            {
                var execution = await _session.ExecutePendingAsync(async (pending, _) =>
                {
                    installer.ConfigureTrustStore(_marketplace.TrustStore);
                    return pending.Kind == MarketplacePendingActionKind.Uninstall
                        ? await installer.UninstallAsync(pending.PluginId!)
                        : await installer.InstallAsync(pending.PackagePath!, pending.Metadata);
                });
                if (execution.IsBusy)
                {
                    ConfirmErrorText.Text = I18n("market.error.busy");
                    return;
                }
                if (execution.IsCanceled || execution.IsMissing || execution.Result == null)
                {
                    ConfirmErrorText.Text = execution.IsCanceled
                        ? I18n("market.status.canceled")
                        : I18n("market.error.confirmationExpired");
                    return;
                }
                var result = execution.Result;

                if (!result.IsSuccess)
                {
                    ConfirmOperationStatusText.Text = string.Empty;
                    ConfirmErrorText.Text = I18n(
                        MarketplacePresentation.GetInstallErrorResourceKey(result.ErrorCode));
                    return;
                }

                var preferredFocus = operation.Intent == MarketplaceOperationIntent.Uninstall
                    ? InstallButton
                    : UninstallButton;
                DismissConfirmation(
                    cancelPending: false,
                    preferredFocus: preferredFocus);
                var status = FormatOperationSuccess(operation, result);
                CatalogStatusText.Text = status;
                await ApplyFiltersAsync();
                _operationStatus = status;
                DetailHint.Text = status;
                AutomationProperties.SetItemStatus(
                    DetailHint,
                    operation.Intent.ToString());
            }
            finally
            {
                SetBusy(false, operation);
            }
        }

        private void SetBusy(
            bool busy,
            MarketplaceOperationPresentation operation)
        {
            InstallProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ConfirmActionButton.IsEnabled = !busy;
            ConfirmCancelButton.IsEnabled = !busy;
            ConfirmCloseButton.IsEnabled = !busy;
            ConfirmOperationStatusText.Text = busy
                ? I18n(operation.ProgressResourceKey)
                : string.Empty;
        }

        private async void ImportLocalPackage_Click(object sender, RoutedEventArgs e)
        {
            var path = PickPackage();
            if (path != null)
                await PreviewPackageAsync(path, new MarketplacePackageMetadata());
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEntry == null || _selectedVersion == null) return;
            _operationStatus = null;
            var entry = _selectedEntry;
            var version = _selectedVersion;
            string? path = null;
            if (_selectedVersion.PackageUri?.IsFile == true
                && File.Exists(_selectedVersion.PackageUri.LocalPath))
                path = _selectedVersion.PackageUri.LocalPath;
            else if (_selectedVersion.PackageUri is { Scheme: "https" })
            {
                InstallButton.IsEnabled = false;
                DetailHint.Text = I18n("market.status.downloading");
                try
                {
                    var preparation = await _session.PrepareRemotePackageAsync(entry, version);
                    ShowPreparation(
                        preparation,
                        I18n("market.error.downloadFailed"));
                }
                finally { ShowVersion(_selectedVersion); }
                return;
            }
            path ??= PickPackage();
            if (path == null) return;

            var metadata = MarketplacePresentation.CreatePackageMetadata(
                entry, version);
            await PreviewPackageAsync(path, metadata);
        }

        private void ShowPreparation(
            MarketplacePreparationResult preparation,
            string rejectionTitle)
        {
            if (preparation.IsBusy)
            {
                DetailHint.Text = I18n("market.error.busyWait");
                return;
            }
            if (preparation.IsCanceled)
            {
                DetailHint.Text = I18n("market.status.canceled");
                return;
            }
            if (!preparation.IsSuccess || preparation.PendingAction == null)
            {
                ShowConfirmationError(
                    rejectionTitle,
                    preparation.ErrorCode);
                return;
            }

            if (preparation.Download is { } download)
            {
                DetailHint.Text = download.FromCache
                    ? I18n("market.status.cacheVerified")
                    : download.Attempts > 1
                        ? string.Format(
                            I18n("market.status.downloadRetried"),
                            download.Attempts,
                            download.Bytes / 1024d)
                        : string.Format(
                            I18n("market.status.downloaded"),
                            download.Bytes / 1024d);
            }
            ShowInstallConfirmation(preparation.PendingAction);
        }

        private static string? PickPackage()
        {
            var dialog = new OpenFileDialog
            {
                Title = I18n("market.filePicker.title"),
                Filter = I18n("market.filePicker.filter"),
                CheckFileExists = true,
                Multiselect = false,
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private void UninstallButton_Click(object sender, RoutedEventArgs e) => PreviewUninstall();

        private void CancelConfirmation_Click(object sender, RoutedEventArgs e)
            => DismissConfirmation();

        private async void RefreshCatalog_Click(object sender, RoutedEventArgs e) => await LoadCatalogAsync();
        private async void CatalogRetry_Click(object sender, RoutedEventArgs e) => await LoadCatalogAsync();
        private async void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => await ApplyFiltersAsync();

        private void MarketList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isCompactLayout) return;
            if (MarketList.SelectedItem is MarketCardModel card) ShowEntry(card);
        }

        private void MarketList_PreviewMouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (_isCompactLayout &&
                MarketList.SelectedItem is MarketCardModel card)
                ShowEntry(card);
        }

        private void MarketBackButton_Click(object sender, RoutedEventArgs e)
            => NavigateBackInModule();

        internal bool HasDismissibleTransientLayer
            => ConfirmOverlay.Visibility == Visibility.Visible
                && InstallProgress.Visibility != Visibility.Visible;

        internal bool CanNavigateBackInModule
            => _moduleRouter.Route.Kind == MarketplaceModuleRouteKind.Detail
                && ConfirmOverlay.Visibility != Visibility.Visible;

        internal bool DismissTransientLayer()
            => DismissConfirmation();

        internal bool NavigateBackInModule()
        {
            if (!CanNavigateBackInModule)
                return false;
            if (!_moduleRouter.BackToCatalog())
                return false;
            ShowCatalogRoute(clearSelection: false);
            MarketList.Focus();
            return true;
        }

        internal Task ApplyWorkspaceSearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _workspaceQuery = query ?? string.Empty;
            return ApplyFiltersAsync();
        }

        private void VersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _operationStatus = null;
            ShowVersion(
                VersionBox.SelectedIndex >= 0
                && VersionBox.SelectedIndex < _displayedVersions.Count
                    ? _displayedVersions[VersionBox.SelectedIndex]
                    : null);
        }

        internal void ShowListForQuality()
        {
            _forceListForQuality = true;
            ApplyResponsiveLayout(0);
            MarketDetailCard.Visibility = Visibility.Collapsed;
            MarketListCard.Visibility = Visibility.Visible;
        }

        internal bool ShowFirstDetailForQuality()
        {
            if (MarketList.ItemsSource is not IEnumerable<MarketCardModel> cards
                || cards.FirstOrDefault() is not { } first)
            {
                return false;
            }
            _forceListForQuality = false;
            MarketList.SelectedItem = first;
            ShowEntry(first);
            return _moduleRouter.Route.Kind
                == MarketplaceModuleRouteKind.Detail;
        }

        internal bool ShowUpdateReviewForQuality()
        {
            if (MarketList.ItemsSource is not IEnumerable<MarketCardModel> cards
                || cards.FirstOrDefault() is not { } first
                || first.Entry.Versions
                    .OrderByDescending(version =>
                        MarketplacePresentation.ParseVersion(version.Version))
                    .FirstOrDefault() is not { } version)
            {
                return false;
            }

            _forceListForQuality = false;
            MarketList.SelectedItem = first;
            ShowEntry(first);
            RememberConfirmationFocus();
            var operation = MarketplaceOperationPresenter.ForInstall(
                "0.0.0",
                version.Version);
            _pendingOperationPresentation = operation;
            ConfirmTitle.Text = I18n(operation.ReviewTitleResourceKey);
            ConfirmSubtitle.Text = $"{first.Name} · v{version.Version}";
            ConfirmTrustText.Text = !string.IsNullOrWhiteSpace(version.PublisherKeyId)
                ? I18n("market.confirm.publisherVerified")
                : I18n("market.confirm.localUnsigned");
            ConfirmHashText.Text = string.IsNullOrWhiteSpace(version.Sha256)
                ? "SHA-256"
                : $"SHA-256  {version.Sha256}";
            ConfirmCompatibilityText.Text = I18n(
                "market.confirm.compatibilityPassed");
            PermissionDiffItems.ItemsSource = version.Capabilities.Count == 0
                ? new[] { I18n("market.permission.none") }
                : version.Capabilities.Select(capability => string.Format(
                    I18n("market.permission.added"),
                    capability));
            HighTrustWarning.Visibility = Visibility.Collapsed;
            ConfirmErrorText.Text = string.Empty;
            ConfirmOperationStatusText.Text = string.Empty;
            ConfirmActionButton.Content = I18n(
                operation.ConfirmActionResourceKey);
            ConfirmActionButton.IsEnabled = true;
            ConfirmActionButton.SetResourceReference(
                StyleProperty,
                "LongButton.Primary");
            ConfirmOverlay.Visibility = Visibility.Visible;
            FocusConfirmationAction();
            return true;
        }

        private void MarketplaceControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_isCompactLayout
                && ConfirmOverlay.Visibility != Visibility.Visible
                && e.Key is Key.Enter or Key.Space
                && MarketList.IsKeyboardFocusWithin
                && MarketList.SelectedItem is MarketCardModel selected)
            {
                ShowEntry(selected);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && ConfirmOverlay.Visibility == Visibility.Visible
                && InstallProgress.Visibility != Visibility.Visible)
            {
                e.Handled = DismissConfirmation();
            }
        }

        private void RememberConfirmationFocus()
        {
            if (ConfirmOverlay.Visibility != Visibility.Visible)
                _confirmationFocusOrigin = Keyboard.FocusedElement;
        }

        private void FocusConfirmationAction()
        {
            _ = Dispatcher.BeginInvoke(
                () =>
                {
                    ConfirmActionButton.Focus();
                    Keyboard.Focus(ConfirmActionButton);
                },
                DispatcherPriority.Input);
        }

        private bool DismissConfirmation(
            bool cancelPending = true,
            IInputElement? preferredFocus = null)
        {
            if (ConfirmOverlay.Visibility != Visibility.Visible
                || (cancelPending
                    && InstallProgress.Visibility == Visibility.Visible))
                return false;
            if (cancelPending)
                _session.CancelPending();
            ConfirmOverlay.Visibility = Visibility.Collapsed;
            ConfirmActionButton.IsEnabled = true;
            ConfirmCancelButton.IsEnabled = true;
            ConfirmCloseButton.IsEnabled = true;
            var focusOrigin = _confirmationFocusOrigin;
            _confirmationFocusOrigin = null;
            _pendingOperationPresentation = null;
            _ = Dispatcher.BeginInvoke(
                () =>
                {
                    var focusTarget = new[] {
                            preferredFocus,
                            focusOrigin,
                            InstallButton,
                            UninstallButton,
                            VersionBox,
                            MarketList,
                        }
                        .OfType<UIElement>()
                        .FirstOrDefault(element =>
                            element.IsVisible
                            && element.IsEnabled
                            && element.Focusable);
                    if (focusTarget != null)
                    {
                        focusTarget.Focus();
                        Keyboard.Focus(focusTarget);
                    }
                },
                DispatcherPriority.Input);
            return true;
        }

        private static string FormatOperationSuccess(
            MarketplaceOperationPresentation operation,
            InstallResult result)
            => operation.Intent == MarketplaceOperationIntent.Uninstall
                ? string.Format(
                    I18n(operation.SuccessResourceKey),
                    result.PluginName)
                : string.Format(
                    I18n(operation.SuccessResourceKey),
                    result.PluginName,
                    result.PluginVersion);

        private void OnPluginsChanged()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(OnPluginsChanged);
                return;
            }
            _ = ApplyFiltersAsync();
        }

        private void ShowEmptyDetail()
        {
            _moduleRouter.Reset();
            ShowCatalogRoute(clearSelection: true);
        }

        private void ShowCatalogRoute(bool clearSelection)
        {
            if (clearSelection)
            {
                _selectedEntry = null;
                MarketList.SelectedItem = null;
            }
            MarketDetail.Visibility = Visibility.Collapsed;
            MarketEmptyDetail.Visibility = Visibility.Visible;
            if (_isCompactLayout)
            {
                MarketDetailCard.Visibility = Visibility.Collapsed;
                MarketListCard.Visibility = Visibility.Visible;
            }
        }

        private static IReadOnlyList<string> FormatPermissionDiff(
            PermissionDiff diff)
        {
            var lines = new List<string>();
            lines.AddRange(diff.Added.Select(capability =>
                string.Format(I18n("market.permission.added"), capability)));
            lines.AddRange(diff.Removed.Select(capability =>
                string.Format(I18n("market.permission.removed"), capability)));
            lines.AddRange(diff.Unchanged.Select(capability =>
                string.Format(I18n("market.permission.unchanged"), capability)));
            if (lines.Count == 0)
                lines.Add(I18n("market.permission.none"));
            return lines;
        }

        private static string StateLabel(MarketplaceInstallState state)
            => I18n(state switch
            {
                MarketplaceInstallState.Installed => "market.state.installed",
                MarketplaceInstallState.UpdateAvailable => "market.state.update",
                MarketplaceInstallState.DowngradeAvailable => "market.state.downgrade",
                MarketplaceInstallState.Incompatible => "market.state.incompatible",
                _ => "market.state.get",
            });

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);

    }
}
