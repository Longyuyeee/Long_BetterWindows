using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Interaction
{
    internal sealed class MarketplaceSessionCoordinator : IDisposable
    {
        private readonly Func<CancellationToken, Task<MarketplaceCatalogResult>> _loadCatalog;
        private readonly Func<string, MarketplacePackageMetadata, PluginManifest?, Task<PackageValidationResult>> _validatePackage;
        private readonly Func<string, MarketplacePackageVersion, CancellationToken, Task<PackageDownloadResult>> _downloadPackage;
        private readonly Func<bool> _canDownload;
        private readonly Func<string, PluginManifest?> _getInstalledManifest;
        private readonly object _stateLock = new();
        private CancellationTokenSource? _catalogLoad;
        private CancellationTokenSource? _operation;
        private bool _disposed;

        public MarketplaceSessionCoordinator(
            MarketplaceRuntimeService runtime,
            Func<string, PluginManifest?> getInstalledManifest)
            : this(
                runtime.LoadCatalogAsync,
                runtime.ValidatePackageAsync,
                runtime.DownloadPackageAsync,
                () => runtime.CanDownload,
                getInstalledManifest)
        {
        }

        internal MarketplaceSessionCoordinator(
            Func<CancellationToken, Task<MarketplaceCatalogResult>> loadCatalog,
            Func<string, MarketplacePackageMetadata, PluginManifest?, Task<PackageValidationResult>> validatePackage,
            Func<string, MarketplacePackageVersion, CancellationToken, Task<PackageDownloadResult>> downloadPackage,
            Func<bool> canDownload,
            Func<string, PluginManifest?> getInstalledManifest)
        {
            _loadCatalog = loadCatalog;
            _validatePackage = validatePackage;
            _downloadPackage = downloadPackage;
            _canDownload = canDownload;
            _getInstalledManifest = getInstalledManifest;
        }

        public MarketplaceCatalog? Catalog { get; private set; }
        public MarketplacePendingAction? PendingAction { get; private set; }
        public bool IsOperationActive
        {
            get { lock (_stateLock) return _operation != null; }
        }

        public async Task<MarketplaceCatalogSessionResult> LoadCatalogAsync(
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CancellationTokenSource current;
            lock (_stateLock)
            {
                _catalogLoad?.Cancel();
                _catalogLoad?.Dispose();
                current = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _catalogLoad = current;
            }

            try
            {
                var result = await _loadCatalog(current.Token);
                lock (_stateLock)
                {
                    if (!ReferenceEquals(_catalogLoad, current))
                        return MarketplaceCatalogSessionResult.Superseded();
                    Catalog = result.IsSuccess ? result.Catalog : null;
                }
                return MarketplaceCatalogSessionResult.Current(result);
            }
            catch (OperationCanceledException) when (current.IsCancellationRequested)
            {
                return MarketplaceCatalogSessionResult.Superseded();
            }
            finally
            {
                lock (_stateLock)
                {
                    if (ReferenceEquals(_catalogLoad, current))
                        _catalogLoad = null;
                }
                current.Dispose();
            }
        }

        public async Task<MarketplacePreparationResult> PrepareLocalPackageAsync(
            string path,
            MarketplacePackageMetadata metadata)
        {
            if (!TryBeginOperation(out var operation))
                return MarketplacePreparationResult.Busy();
            try
            {
                var installed = metadata.ExpectedPluginId == null
                    ? null
                    : _getInstalledManifest(metadata.ExpectedPluginId);
                var validation = await _validatePackage(path, metadata, installed);
                operation.Token.ThrowIfCancellationRequested();
                if (!validation.IsSuccess)
                {
                    PendingAction = null;
                    return MarketplacePreparationResult.Rejected(validation.Error, validation);
                }

                var pending = MarketplacePendingAction.Install(path, metadata, validation);
                PendingAction = pending;
                return MarketplacePreparationResult.Prepared(pending);
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested)
            {
                PendingAction = null;
                return MarketplacePreparationResult.Canceled();
            }
            finally { EndOperation(operation); }
        }

        public async Task<MarketplacePreparationResult> PrepareRemotePackageAsync(
            MarketplaceEntry entry,
            MarketplacePackageVersion version)
        {
            if (!TryBeginOperation(out var operation))
                return MarketplacePreparationResult.Busy();
            try
            {
                if (!_canDownload())
                {
                    PendingAction = null;
                    return MarketplacePreparationResult.Rejected(
                        "远程下载器尚未配置。");
                }

                var download = await _downloadPackage(entry.Id, version, operation.Token);
                if (!download.IsSuccess || download.PackagePath == null)
                {
                    PendingAction = null;
                    return MarketplacePreparationResult.Rejected(
                        download.Error ?? "插件包下载失败。", download: download);
                }

                var metadata = MarketplacePresentation.CreatePackageMetadata(entry, version);
                var validation = await _validatePackage(
                    download.PackagePath,
                    metadata,
                    _getInstalledManifest(entry.Id));
                operation.Token.ThrowIfCancellationRequested();
                if (!validation.IsSuccess)
                {
                    PendingAction = null;
                    return MarketplacePreparationResult.Rejected(
                        validation.Error, validation, download);
                }

                var pending = MarketplacePendingAction.Install(
                    download.PackagePath, metadata, validation);
                PendingAction = pending;
                return MarketplacePreparationResult.Prepared(pending, download);
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested)
            {
                PendingAction = null;
                return MarketplacePreparationResult.Canceled();
            }
            finally { EndOperation(operation); }
        }

        public MarketplacePreparationResult PrepareUninstall(PluginManifest installedManifest)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_stateLock)
            {
                if (_operation != null) return MarketplacePreparationResult.Busy();
                PendingAction = MarketplacePendingAction.Uninstall(installedManifest);
                return MarketplacePreparationResult.Prepared(PendingAction);
            }
        }

        public async Task<MarketplaceExecutionResult> ExecutePendingAsync(
            Func<MarketplacePendingAction, CancellationToken, Task<InstallResult>> execute)
        {
            MarketplacePendingAction pending;
            CancellationTokenSource operation;
            lock (_stateLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_operation != null) return MarketplaceExecutionResult.Busy();
                if (PendingAction == null) return MarketplaceExecutionResult.Missing();
                pending = PendingAction;
                operation = new CancellationTokenSource();
                _operation = operation;
            }
            try
            {
                var result = await execute(pending, operation.Token);
                if (result.IsSuccess)
                {
                    lock (_stateLock)
                    {
                        if (ReferenceEquals(PendingAction, pending)) PendingAction = null;
                    }
                }
                return MarketplaceExecutionResult.Completed(result);
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested)
            {
                return MarketplaceExecutionResult.Canceled();
            }
            finally { EndOperation(operation); }
        }

        public bool CancelPending()
        {
            lock (_stateLock)
            {
                if (_operation != null) return false;
                PendingAction = null;
                return true;
            }
        }

        public void CancelActiveRequests()
        {
            lock (_stateLock)
            {
                _catalogLoad?.Cancel();
                _operation?.Cancel();
            }
        }

        private bool TryBeginOperation(out CancellationTokenSource operation)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_stateLock)
            {
                if (_operation != null)
                {
                    operation = null!;
                    return false;
                }
                operation = new CancellationTokenSource();
                _operation = operation;
                return true;
            }
        }

        private void EndOperation(CancellationTokenSource operation)
        {
            lock (_stateLock)
            {
                if (ReferenceEquals(_operation, operation)) _operation = null;
            }
            operation.Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CancelActiveRequests();
            lock (_stateLock)
            {
                _catalogLoad?.Dispose();
                _catalogLoad = null;
            }
        }
    }

    internal sealed record MarketplaceCatalogSessionResult(
        MarketplaceCatalogResult? Result,
        bool IsSuperseded)
    {
        public static MarketplaceCatalogSessionResult Current(MarketplaceCatalogResult result)
            => new(result, false);
        public static MarketplaceCatalogSessionResult Superseded()
            => new(null, true);
    }

    internal enum MarketplacePendingActionKind
    {
        Install,
        Uninstall,
    }

    internal sealed record MarketplacePendingAction(
        MarketplacePendingActionKind Kind,
        string? PackagePath,
        string? PluginId,
        MarketplacePackageMetadata? Metadata,
        PackageValidationResult? Validation,
        PluginManifest? InstalledManifest)
    {
        public static MarketplacePendingAction Install(
            string path,
            MarketplacePackageMetadata metadata,
            PackageValidationResult validation)
            => new(
                MarketplacePendingActionKind.Install,
                path,
                validation.Manifest?.Id,
                metadata,
                validation,
                null);

        public static MarketplacePendingAction Uninstall(PluginManifest manifest)
            => new(
                MarketplacePendingActionKind.Uninstall,
                null,
                manifest.Id,
                null,
                null,
                manifest);
    }

    internal sealed record MarketplacePreparationResult(
        bool IsSuccess,
        bool IsBusy,
        bool IsCanceled,
        string? Error,
        MarketplacePendingAction? PendingAction,
        PackageValidationResult? Validation,
        PackageDownloadResult? Download)
    {
        public static MarketplacePreparationResult Prepared(
            MarketplacePendingAction pending,
            PackageDownloadResult? download = null)
            => new(true, false, false, null, pending, pending.Validation, download);
        public static MarketplacePreparationResult Rejected(
            string? error,
            PackageValidationResult? validation = null,
            PackageDownloadResult? download = null)
            => new(false, false, false, error, null, validation, download);
        public static MarketplacePreparationResult Busy()
            => new(false, true, false, "已有市场操作正在进行。", null, null, null);
        public static MarketplacePreparationResult Canceled()
            => new(false, false, true, "操作已取消。", null, null, null);
    }

    internal sealed record MarketplaceExecutionResult(
        InstallResult? Result,
        bool IsBusy,
        bool IsCanceled,
        bool IsMissing)
    {
        public static MarketplaceExecutionResult Completed(InstallResult result)
            => new(result, false, false, false);
        public static MarketplaceExecutionResult Busy()
            => new(null, true, false, false);
        public static MarketplaceExecutionResult Canceled()
            => new(null, false, true, false);
        public static MarketplaceExecutionResult Missing()
            => new(null, false, false, true);
    }
}
