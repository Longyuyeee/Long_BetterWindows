using System.IO;
using System.Net.Http;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Services
{
    internal sealed class MarketplaceRuntimeService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly string _settingsPath;
        private readonly string _trustStorePath;
        private readonly string _dataRoot;
        private readonly SemaphoreSlim _initializationGate = new(1, 1);
        private IMarketplaceRepository _repository;
        private PluginPackageValidator _validator = new();
        private MarketplacePackageDownloader? _downloader;
        private bool _initialized;
        private bool _disposed;

        public MarketplaceRuntimeService(
            string catalogPath,
            MarketplaceSourceKind catalogSource,
            string settingsPath,
            string trustStorePath,
            string dataRoot,
            string productVersion,
            HttpClient? httpClient = null)
        {
            _repository = new LocalMarketplaceRepository(catalogPath, catalogSource);
            _settingsPath = settingsPath;
            _trustStorePath = trustStorePath;
            _dataRoot = dataRoot;
            _httpClient = httpClient ?? new HttpClient();
            _ownsHttpClient = httpClient == null;
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"LongBetterWindows/{productVersion}");
        }

        public PublisherTrustStore TrustStore { get; private set; }
            = PublisherTrustStore.Empty;

        public bool CanDownload => _downloader != null;

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialized) return;

            await _initializationGate.WaitAsync(cancellationToken);
            try
            {
                if (_initialized) return;

                var settings = await MarketplaceConfigurationLoader.LoadSettingsAsync(
                    _settingsPath, cancellationToken);
                var trust = await MarketplaceConfigurationLoader.LoadTrustStoreAsync(
                    _trustStorePath, cancellationToken);
                TrustStore = trust.IsSuccess ? trust.Store : PublisherTrustStore.Empty;
                _validator = new PluginPackageValidator(trustStore: TrustStore);

                if (settings.RegistryUri != null)
                {
                    var remote = new RemoteMarketplaceRepository(
                        _httpClient,
                        settings.RegistryUri,
                        Path.Combine(_dataRoot, "registry-cache.json"),
                        TimeSpan.FromSeconds(settings.CatalogTimeoutSeconds));
                    _repository = new CompositeMarketplaceRepository(_repository, remote);
                    var allowedHosts = settings.AllowedPackageHosts
                        .Append(settings.RegistryUri.IdnHost)
                        .Distinct(StringComparer.OrdinalIgnoreCase);
                    _downloader = new MarketplacePackageDownloader(
                        _httpClient,
                        Path.Combine(_dataRoot, "Packages"),
                        allowedHosts,
                        TimeSpan.FromSeconds(settings.DownloadTimeoutSeconds));
                }

                _initialized = true;
            }
            finally
            {
                _initializationGate.Release();
            }
        }

        public async Task<MarketplaceCatalogResult> LoadCatalogAsync(
            CancellationToken cancellationToken = default)
        {
            await InitializeAsync(cancellationToken);
            return await _repository.LoadAsync(cancellationToken);
        }

        public async Task<PackageValidationResult> ValidatePackageAsync(
            string path,
            MarketplacePackageMetadata metadata,
            PluginManifest? installedManifest)
        {
            await InitializeAsync();
            return await _validator.ValidateAsync(path, metadata, installedManifest);
        }

        public async Task<PackageDownloadResult> DownloadPackageAsync(
            string pluginId,
            MarketplacePackageVersion version,
            CancellationToken cancellationToken = default)
        {
            await InitializeAsync(cancellationToken);
            return _downloader == null
                ? PackageDownloadResult.Fail(
                    MarketplaceErrorCode.DownloadNotConfigured,
                    "Remote marketplace download is not configured.")
                : await _downloader.DownloadAsync(pluginId, version, cancellationToken);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_ownsHttpClient) _httpClient.Dispose();
        }
    }
}
