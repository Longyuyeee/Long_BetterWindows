using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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
        private bool _pluginEventsSubscribed;
        private MarketplaceCatalog? _catalog;
        private MarketplaceEntry? _selectedEntry;
        private MarketplacePackageVersion? _selectedVersion;
        private string? _pendingPackagePath;
        private string? _pendingUninstallId;
        private MarketplacePackageMetadata? _pendingMetadata;
        private PackageValidationResult? _pendingValidation;

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
            Loaded += MarketplaceControl_Loaded;
            Unloaded += MarketplaceControl_Unloaded;
            Dispatcher.ShutdownStarted += (_, _) => _marketplace.Dispose();
        }

        private async void MarketplaceControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_pluginEventsSubscribed)
            {
                HostProvider.Instance.PluginStore.PluginsChanged += OnPluginsChanged;
                _pluginEventsSubscribed = true;
            }

            await LoadCatalogAsync();
        }

        private void MarketplaceControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_pluginEventsSubscribed) return;
            HostProvider.Instance.PluginStore.PluginsChanged -= OnPluginsChanged;
            _pluginEventsSubscribed = false;
        }
        private async Task LoadCatalogAsync()
        {
            CatalogStatusText.Text = "正在读取可信目录…";
            var result = await _marketplace.LoadCatalogAsync();
            if (!result.IsSuccess)
            {
                _catalog = null;
                MarketList.ItemsSource = Array.Empty<MarketCardModel>();
                ResultCountText.Text = "市场暂时离线";
                CatalogStatusText.Text = result.Error;
                MarketSourceBadge.Text = "离线 · 本地插件不受影响";
                CategoryBox.ItemsSource = new[] { "全部分类" };
                CategoryBox.SelectedIndex = 0;
                ShowEmptyDetail();
                return;
            }

            _catalog = result.Catalog;
            MarketSourceBadge.Text = _catalog!.Source == MarketplaceSourceKind.RemoteRegistry
                ? "远程 Registry · 强制签名"
                : "内置可信目录 · 本地优先";
            CategoryBox.ItemsSource = new[] { "全部分类" }
                .Concat(MarketplacePresentation.GetCategories(_catalog))
                .ToArray();
            CategoryBox.SelectedIndex = 0;
            CatalogStatusText.Text = string.IsNullOrWhiteSpace(result.Status)
                ? $"目录生成于 {_catalog.GeneratedAt:yyyy-MM-dd} · Schema {_catalog.SchemaVersion}"
                : result.Status;
            await ApplyFiltersAsync();
        }

        private Task ApplyFiltersAsync()
        {
            if (_catalog == null) return Task.CompletedTask;
            var category = CategoryBox.SelectedItem?.ToString();
            if (category == "全部分类") category = null;
            var cards = MarketplacePresentation.ProjectEntries(
                _catalog,
                MarketSearchBox.Text,
                category,
                pluginId => HostProvider.Instance.PluginStore
                    .Get(pluginId)?.Manifest.Version);
            MarketList.ItemsSource = cards;
            ResultCountText.Text = $"发现 {cards.Count} 个插件";
            if (_selectedEntry != null)
            {
                var selected = cards.FirstOrDefault(x => string.Equals(
                    x.Entry.Id, _selectedEntry.Id, StringComparison.OrdinalIgnoreCase));
                if (selected != null) MarketList.SelectedItem = selected;
                else ShowEmptyDetail();
            }
            else if (cards.Count > 0)
            {
                MarketList.SelectedIndex = 0;
            }
            return Task.CompletedTask;
        }

        private void ShowEntry(MarketCardModel card)
        {
            _selectedEntry = card.Entry;
            MarketEmptyDetail.Visibility = Visibility.Collapsed;
            MarketDetail.Visibility = Visibility.Visible;
            DetailMonogram.Text = card.Monogram;
            DetailName.Text = card.Name;
            DetailPublisher.Text = $"{card.Entry.Publisher} · {card.Entry.Category}";
            DetailDescription.Text = string.IsNullOrWhiteSpace(card.Entry.Description)
                ? card.Entry.Summary
                : card.Entry.Description;
            DetailState.Text = card.StateLabel;
            VersionBox.ItemsSource = card.Entry.Versions
                .OrderByDescending(x => MarketplacePresentation.ParseVersion(x.Version))
                .ToArray();
            VersionBox.DisplayMemberPath = nameof(MarketplacePackageVersion.Version);
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
                ? new[] { "无需额外能力" }
                : version.Capabilities;
            DetailTrust.Text = !string.IsNullOrWhiteSpace(version.PublisherKeyId)
                ? $"发布者签名 · {version.PublisherKeyId}"
                : "目录已收录 · 安装包待审查";
            ReleaseNotesText.Text = string.IsNullOrWhiteSpace(version.ReleaseNotes)
                ? "此版本未提供更新说明。"
                : version.ReleaseNotes;

            var compatibility = MarketplacePresentation.GetCompatibility(
                version, App.ProductVersion);
            CompatibilityTitle.Text = compatibility.IsCompatible ? "✓ 与当前 Long 兼容" : "此版本暂不兼容";
            CompatibilityTitle.SetResourceReference(
                ForegroundProperty,
                compatibility.IsCompatible ? "Long.Brush.State.Success" : "Long.Brush.State.Danger");
            CompatibilityText.Text = compatibility.Description;
            InstallButton.IsEnabled = compatibility.IsCompatible;
            InstallButton.Content = version.PackageUri?.Scheme == Uri.UriSchemeHttps
                ? "下载并审查"
                : version.PackageUri?.IsFile == true ? "审查并安装" : "选择安装包";
            DetailHint.Text = version.PackageUri == null
                ? "目录仅提供版本信息，请选择对应的本地 .lpak。"
                : version.PackageUri.IsFile
                    ? "安装前将验证包哈希、发布者与权限变化。"
                : !_marketplace.CanDownload
                        ? "远程下载通道未配置，可先导入已下载的 .lpak。"
                        : "将从允许的 HTTPS 主机下载，并在缓存前核对 SHA-256。";
        }

        private async Task PreviewPackageAsync(
            string path,
            MarketplacePackageMetadata metadata)
        {
            var installed = metadata.ExpectedPluginId == null
                ? null
                : HostProvider.Instance.PluginStore.Get(metadata.ExpectedPluginId)?.Manifest;
            var validation = await _marketplace.ValidatePackageAsync(
                path, metadata, installed);
            if (!validation.IsSuccess)
            {
                ShowConfirmationError("插件包被拒绝", validation.Error ?? "未知校验错误");
                return;
            }

            _pendingPackagePath = path;
            _pendingMetadata = metadata;
            _pendingValidation = validation;
            _pendingUninstallId = null;
            var manifest = validation.Manifest!;
            ConfirmTitle.Text = "安装前审查";
            ConfirmSubtitle.Text = $"{manifest.Name} · v{manifest.Version}";
            ConfirmTrustText.Text = validation.TrustLevel == PackageTrustLevel.PublisherSigned
                ? "✓ 发布者签名验证通过"
                : "本地未签名包 · 来源由你确认";
            AutomationProperties.SetItemStatus(
                ConfirmTrustText, validation.TrustLevel.ToString());
            ConfirmHashText.Text = $"SHA-256  {validation.Sha256}";
            ConfirmCompatibilityText.Text = "✓ 包结构、入口文件与最低版本兼容检查通过";
            PermissionDiffItems.ItemsSource = MarketplacePresentation.FormatPermissionDiff(
                validation.PermissionDiff);
            HighTrustWarning.Visibility = validation.RequiresHighTrustWarning
                ? Visibility.Visible
                : Visibility.Collapsed;
            ConfirmErrorText.Text = string.Empty;
            ConfirmActionButton.Content = "确认安装";
            ConfirmActionButton.IsEnabled = true;
            ConfirmActionButton.SetResourceReference(StyleProperty, "LongButton.Primary");
            ConfirmOverlay.Visibility = Visibility.Visible;
            ConfirmActionButton.Focus();
        }

        private void PreviewUninstall()
        {
            if (_selectedEntry == null) return;
            var installed = HostProvider.Instance.PluginStore.Get(_selectedEntry.Id)?.Manifest;
            if (installed == null) return;
            _pendingUninstallId = installed.Id;
            _pendingPackagePath = null;
            _pendingMetadata = null;
            _pendingValidation = null;
            ConfirmTitle.Text = "确认卸载";
            ConfirmSubtitle.Text = $"{installed.Name} · v{installed.Version}";
            ConfirmTrustText.Text = "插件文件将通过可回滚事务移除";
            ConfirmHashText.Text = installed.Id;
            ConfirmCompatibilityText.Text = "若卸载或重新扫描失败，Long 会恢复当前版本。";
            PermissionDiffItems.ItemsSource = installed.Capabilities.Count == 0
                ? new[] { "• 不涉及已授权能力" }
                : installed.Capabilities.OrderBy(x => x).Select(x => $"− 移除权限  {x}").ToArray();
            HighTrustWarning.Visibility = Visibility.Collapsed;
            ConfirmErrorText.Text = string.Empty;
            ConfirmActionButton.Content = "确认卸载";
            ConfirmActionButton.IsEnabled = true;
            ConfirmActionButton.SetResourceReference(StyleProperty, "LongButton.Danger");
            ConfirmOverlay.Visibility = Visibility.Visible;
            ConfirmActionButton.Focus();
        }

        private void ShowConfirmationError(string title, string error)
        {
            _pendingPackagePath = null;
            _pendingValidation = null;
            _pendingUninstallId = null;
            ConfirmTitle.Text = title;
            ConfirmSubtitle.Text = "Long 已阻止此次操作";
            ConfirmTrustText.Text = "校验未通过";
            AutomationProperties.SetItemStatus(
                ConfirmTrustText,
                error.Contains("超时", StringComparison.OrdinalIgnoreCase)
                    ? "NetworkTimeout"
                    : error.Contains("下载失败", StringComparison.OrdinalIgnoreCase)
                        || error.Contains("网络", StringComparison.OrdinalIgnoreCase)
                        ? "NetworkUnavailable"
                : error.Contains("SHA-256", StringComparison.OrdinalIgnoreCase)
                    ? "HashRejected"
                    : error.Contains("签名", StringComparison.OrdinalIgnoreCase)
                        ? "SignatureRejected"
                        : "Rejected");
            ConfirmHashText.Text = string.Empty;
            ConfirmCompatibilityText.Text = error;
            PermissionDiffItems.ItemsSource = Array.Empty<string>();
            HighTrustWarning.Visibility = Visibility.Collapsed;
            ConfirmErrorText.Text = error;
            ConfirmActionButton.IsEnabled = false;
            ConfirmOverlay.Visibility = Visibility.Visible;
        }

        private async void ConfirmAction_Click(object sender, RoutedEventArgs e)
        {
            var installer = App.PackageInstaller;
            if (installer == null)
            {
                ConfirmErrorText.Text = "插件引擎仍在启动，请稍后再试。";
                return;
            }

            SetBusy(true);
            try
            {
                installer.ConfigureTrustStore(_marketplace.TrustStore);
                InstallResult result;
                if (_pendingUninstallId != null)
                    result = await installer.UninstallAsync(_pendingUninstallId);
                else if (_pendingPackagePath != null && _pendingValidation != null)
                    result = await installer.InstallAsync(_pendingPackagePath, _pendingMetadata);
                else
                    return;

                if (!result.IsSuccess)
                {
                    ConfirmErrorText.Text = result.Error;
                    return;
                }

                ConfirmOverlay.Visibility = Visibility.Collapsed;
                CatalogStatusText.Text = result.Action == InstallAction.Uninstall
                    ? $"已卸载 {result.PluginName}"
                    : $"已安装 {result.PluginName} v{result.PluginVersion}";
                await ApplyFiltersAsync();
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            InstallProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ConfirmActionButton.IsEnabled = !busy;
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
            string? path = null;
            if (_selectedVersion.PackageUri?.IsFile == true
                && File.Exists(_selectedVersion.PackageUri.LocalPath))
                path = _selectedVersion.PackageUri.LocalPath;
            else if (_selectedVersion.PackageUri is { Scheme: "https" })
            {
                if (!_marketplace.CanDownload)
                {
                    ShowConfirmationError("下载通道不可用", "远程下载器尚未配置。");
                    return;
                }
                InstallButton.IsEnabled = false;
                DetailHint.Text = "正在安全下载并核对 SHA-256…";
                try
                {
                    var download = await _marketplace.DownloadPackageAsync(
                        _selectedEntry.Id, _selectedVersion);
                    if (!download.IsSuccess)
                    {
                        ShowConfirmationError("插件包下载失败", download.Error!);
                        return;
                    }
                    path = download.PackagePath;
                    DetailHint.Text = download.FromCache
                        ? "已使用通过哈希复核的本地缓存。"
                        : download.Attempts > 1
                            ? $"网络恢复后第 {download.Attempts} 次下载成功，已核对 {download.Bytes / 1024d:F1} KB。"
                            : $"已下载 {download.Bytes / 1024d:F1} KB，等待安装审查。";
                }
                finally { InstallButton.IsEnabled = true; }
            }
            path ??= PickPackage();
            if (path == null) return;

            var metadata = MarketplacePresentation.CreatePackageMetadata(
                _selectedEntry, _selectedVersion);
            await PreviewPackageAsync(path, metadata);
        }

        private static string? PickPackage()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 Long 插件包",
                Filter = "Long 插件包 (*.lpak)|*.lpak",
                CheckFileExists = true,
                Multiselect = false,
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private void UninstallButton_Click(object sender, RoutedEventArgs e) => PreviewUninstall();

        private void CancelConfirmation_Click(object sender, RoutedEventArgs e)
        {
            if (InstallProgress.Visibility == Visibility.Visible) return;
            ConfirmOverlay.Visibility = Visibility.Collapsed;
            ConfirmActionButton.IsEnabled = true;
        }

        private async void RefreshCatalog_Click(object sender, RoutedEventArgs e) => await LoadCatalogAsync();
        private async void MarketSearchBox_TextChanged(object sender, TextChangedEventArgs e) => await ApplyFiltersAsync();
        private async void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => await ApplyFiltersAsync();

        private void MarketList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MarketList.SelectedItem is MarketCardModel card) ShowEntry(card);
        }

        private void VersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => ShowVersion(VersionBox.SelectedItem as MarketplacePackageVersion);

        private void MarketplaceControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && ConfirmOverlay.Visibility == Visibility.Visible
                && InstallProgress.Visibility != Visibility.Visible)
            {
                ConfirmOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        }

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
            _selectedEntry = null;
            MarketDetail.Visibility = Visibility.Collapsed;
            MarketEmptyDetail.Visibility = Visibility.Visible;
        }

    }
}
