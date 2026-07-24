namespace LongBetterWindows.Host.Engine
{
    public enum MarketplaceErrorCode
    {
        None = 0,
        CatalogNotFound = 4000,
        CatalogUnsupported = 4001,
        CatalogTooLarge = 4002,
        CatalogDuplicatePlugin = 4003,
        CatalogInvalidEntry = 4004,
        CatalogUnreadable = 4005,
        CatalogNetworkUnavailable = 4006,
        CatalogInsecureRedirect = 4007,
        CatalogAllSourcesUnavailable = 4008,
        DownloadNotConfigured = 4100,
        DownloadUriNotAllowed = 4101,
        DownloadHashMissing = 4102,
        DownloadCacheFailure = 4103,
        DownloadRedirectNotAllowed = 4104,
        DownloadTooLarge = 4105,
        DownloadHashMismatch = 4106,
        DownloadCanceled = 4107,
        DownloadTimeout = 4108,
        DownloadFailed = 4109,
        PackageRejected = 4200,
        OperationBusy = 4201,
        OperationCanceled = 4202,
    }
}
