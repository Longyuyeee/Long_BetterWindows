using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Engine
{
    public enum InstallErrorCode
    {
        None = 0,
        SourceNotFound = 3000,
        InvalidPackageExtension = 3001,
        PackageValidationFailed = 3002,
        InstallFailedRolledBack = 3003,
        InstallRollbackFailed = 3004,
        PluginNotInstalled = 3005,
        InstalledManifestInvalid = 3006,
        UninstallFailedRolledBack = 3007,
        UninstallRollbackFailed = 3008,
    }

    public enum InstallAction
    {
        Install,
        Replace,
        Uninstall,
    }

    public sealed class InstallResult
    {
        public bool IsSuccess { get; init; }
        public string? PluginName { get; init; }
        public string? PluginId { get; init; }
        public string? PluginVersion { get; init; }
        public InstallErrorCode ErrorCode { get; init; }
        public string? Error { get; init; }
        public InstallAction Action { get; init; }
        public PackageValidationResult? Validation { get; init; }
        public ManifestErrorCode? ManifestFailureCode { get; init; }
        public PermissionDiff PermissionDiff { get; init; } = new();

        public static InstallResult Ok(
            string name,
            string id,
            string version,
            InstallAction action,
            PackageValidationResult? validation,
            PermissionDiff permissionDiff)
            => new()
            {
                IsSuccess = true,
                PluginName = name,
                PluginId = id,
                PluginVersion = version,
                ErrorCode = InstallErrorCode.None,
                Action = action,
                Validation = validation,
                PermissionDiff = permissionDiff,
            };

        public static InstallResult Fail(
            InstallErrorCode code,
            string technicalMessage,
            PackageValidationResult? validation = null,
            ManifestErrorCode? manifestFailureCode = null)
            => new()
            {
                ErrorCode = code,
                Error = technicalMessage,
                Validation = validation,
                ManifestFailureCode = manifestFailureCode,
            };
    }
}
