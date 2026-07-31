using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Engine
{
    public enum ManifestErrorCode
    {
        None = 0,
        FileNotFound = 1000,
        ReadFailed = 1001,
        InvalidJson = 1002,
        ValidationFailed = 1003,
    }

    public enum ManifestValidationCode
    {
        InvalidManifestValue = 2000,
        InvalidCommand = 2001,
        InvalidWindow = 2002,
        IncompatibleApiVersion = 2003,
        InvalidLocalization = 2004,
        InvalidWidget = 2005,
    }

    public sealed record ManifestValidationIssue(
        ManifestValidationCode Code,
        string Path,
        string TechnicalMessage);

    public sealed class ManifestResult
    {
        public bool IsSuccess { get; init; }
        public PluginManifest? Manifest { get; init; }
        public ManifestErrorCode ErrorCode { get; init; }
        public string? Error { get; init; }
        public IReadOnlyList<ManifestValidationIssue> Issues { get; init; }
            = Array.Empty<ManifestValidationIssue>();

        public static ManifestResult Ok(PluginManifest manifest)
            => new()
            {
                IsSuccess = true,
                Manifest = manifest,
                ErrorCode = ManifestErrorCode.None,
            };

        public static ManifestResult Fail(ManifestErrorCode code, string technicalMessage)
            => new() { ErrorCode = code, Error = technicalMessage };

        public static ManifestResult ValidationFailure(
            IReadOnlyList<ManifestValidationIssue> issues)
            => new()
            {
                ErrorCode = ManifestErrorCode.ValidationFailed,
                Error = string.Join("; ", issues.Select(issue => issue.TechnicalMessage)),
                Issues = issues,
            };
    }
}
