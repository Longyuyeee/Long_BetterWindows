using System.Net.Http.Headers;
using System.Net.Http;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LongBetterWindows.Host.Services
{
    public enum UpdateCheckState
    {
        UpToDate,
        Available,
        Unavailable,
    }

    public sealed record UpdatePackage(
        string Kind,
        string File,
        Uri Url,
        string Sha256,
        long Bytes);

    public sealed record UpdateCheckResult(
        UpdateCheckState State,
        string CurrentVersion,
        string? AvailableVersion = null,
        Uri? ReleasePage = null,
        UpdatePackage? Package = null);

    /// <summary>
    /// Discovers GitHub releases, verifies the detached release manifest signature,
    /// and downloads only a package whose length and SHA-256 match that manifest.
    /// </summary>
    public sealed class UpdateService : IDisposable
    {
        public const string ManifestAssetName = "update-manifest.json";
        public const string SignatureAssetName = "update-manifest.sig";
        private const int MaximumManifestBytes = 256 * 1024;
        private const int MaximumSignatureBytes = 16 * 1024;
        private const long MaximumPackageBytes = 1024L * 1024 * 1024;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsClient;
        private readonly string _currentVersion;
        private readonly string _repository;
        private readonly string _publicKeyXml;

        public UpdateService(
            string currentVersion,
            string publicKeyXml,
            HttpClient? httpClient = null,
            string repository = "Longyuyeee/Long_BetterWindows")
        {
            if (!SemanticVersion.TryParse(currentVersion, out _))
                throw new ArgumentException("Current version is not semantic.", nameof(currentVersion));
            if (string.IsNullOrWhiteSpace(publicKeyXml))
                throw new ArgumentException("A public verification key is required.", nameof(publicKeyXml));
            if (!IsRepositoryName(repository))
                throw new ArgumentException("Repository must use owner/name form.", nameof(repository));

            _currentVersion = currentVersion;
            _publicKeyXml = publicKeyXml;
            _repository = repository;
            _ownsClient = httpClient is null;
            _httpClient = httpClient ?? new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
                _httpClient.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("LongBetterWindows", currentVersion));
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        public static UpdateService CreateDefault()
        {
            var publicKeyPath = Path.Combine(
                AppContext.BaseDirectory,
                "Update",
                "update-public-key.xml");
            return new UpdateService(
                App.ProductVersion,
                File.ReadAllText(publicKeyPath, Encoding.UTF8));
        }

        public async Task<UpdateCheckResult> CheckAsync(
            bool includePrereleases,
            CancellationToken cancellationToken = default)
        {
            var releasesUri = new Uri(
                $"https://api.github.com/repos/{_repository}/releases?per_page=20");
            var releaseBytes = await ReadLimitedAsync(
                releasesUri,
                MaximumManifestBytes,
                cancellationToken);
            var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(releaseBytes)
                ?? [];
            var current = SemanticVersion.Parse(_currentVersion);

            foreach (var release in releases.Where(item =>
                         !item.Draft && (includePrereleases || !item.Prerelease)))
            {
                if (!TryParseTag(release.TagName, out var releaseVersion)
                    || releaseVersion <= current)
                    continue;

                var manifestAsset = release.Assets.SingleOrDefault(
                    asset => asset.Name == ManifestAssetName);
                var signatureAsset = release.Assets.SingleOrDefault(
                    asset => asset.Name == SignatureAssetName);
                if (manifestAsset is null || signatureAsset is null)
                    continue;

                var manifestUri = ValidateGitHubAssetUri(manifestAsset.DownloadUrl);
                var signatureUri = ValidateGitHubAssetUri(signatureAsset.DownloadUrl);
                var manifestBytes = await ReadLimitedAsync(
                    manifestUri,
                    MaximumManifestBytes,
                    cancellationToken);
                var signatureBytes = await ReadLimitedAsync(
                    signatureUri,
                    MaximumSignatureBytes,
                    cancellationToken);
                VerifyManifestSignature(manifestBytes, signatureBytes);

                var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestBytes)
                    ?? throw new InvalidDataException("The update manifest is empty.");
                var package = ValidateManifest(manifest, release, releaseVersion);
                return new UpdateCheckResult(
                    UpdateCheckState.Available,
                    _currentVersion,
                    manifest.Version,
                    new Uri(manifest.ReleasePage),
                    package);
            }

            return new UpdateCheckResult(UpdateCheckState.UpToDate, _currentVersion);
        }

        public async Task<string> DownloadAsync(
            UpdatePackage package,
            string destinationDirectory,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(package);
            if (package.Bytes <= 0 || package.Bytes > MaximumPackageBytes)
                throw new InvalidDataException("The update package size is outside the allowed range.");
            ValidateGitHubAssetUri(package.Url.ToString());
            Directory.CreateDirectory(destinationDirectory);

            var destinationPath = Path.Combine(destinationDirectory, package.File);
            var partialPath = destinationPath + ".part";
            try
            {
                using var response = await _httpClient.GetAsync(
                    package.Url,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long length
                    && length != package.Bytes)
                    throw new InvalidDataException("The downloaded package length does not match the manifest.");

                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var destination = new FileStream(
                                 partialPath,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 81920,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await source.CopyToAsync(destination, cancellationToken);
                }

                var file = new FileInfo(partialPath);
                if (file.Length != package.Bytes)
                    throw new InvalidDataException("The downloaded package is incomplete.");
                string hash;
                await using (var verificationStream = File.OpenRead(partialPath))
                {
                    hash = Convert.ToHexString(
                            await SHA256.HashDataAsync(verificationStream, cancellationToken))
                        .ToLowerInvariant();
                }
                if (!CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(hash),
                        Encoding.ASCII.GetBytes(package.Sha256)))
                    throw new CryptographicException("The downloaded package SHA-256 is invalid.");

                File.Move(partialPath, destinationPath, true);
                return destinationPath;
            }
            catch
            {
                if (File.Exists(partialPath))
                    File.Delete(partialPath);
                throw;
            }
        }

        private UpdatePackage ValidateManifest(
            UpdateManifest manifest,
            GitHubRelease release,
            SemanticVersion releaseVersion)
        {
            if (manifest.SchemaVersion != 1
                || !SemanticVersion.TryParse(manifest.Version, out var manifestVersion)
                || manifestVersion != releaseVersion
                || !string.Equals(release.TagName, $"v{manifest.Version}", StringComparison.Ordinal)
                || manifest.SourceCommit?.Length != 40
                || !manifest.SourceCommit.All(Uri.IsHexDigit))
                throw new InvalidDataException("The signed update manifest metadata is invalid.");

            var expectedReleasePage =
                $"https://github.com/{_repository}/releases/tag/{release.TagName}";
            if (!string.Equals(
                    manifest.ReleasePage,
                    expectedReleasePage,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update release page is outside the configured repository.");

            var expectedChannel = release.Prerelease ? "prerelease" : "stable";
            if (!string.Equals(manifest.Channel, expectedChannel, StringComparison.Ordinal))
                throw new InvalidDataException("The update channel does not match the GitHub release.");

            var packageModel = manifest.Packages.FirstOrDefault(
                                   package => package.Kind == "self-contained")
                               ?? manifest.Packages.FirstOrDefault(
                                   package => package.Kind == "framework-dependent")
                               ?? throw new InvalidDataException("No supported update package is listed.");
            if (string.IsNullOrWhiteSpace(packageModel.File)
                || Path.GetFileName(packageModel.File) != packageModel.File
                || packageModel.Bytes <= 0
                || packageModel.Bytes > MaximumPackageBytes
                || packageModel.Sha256?.Length != 64
                || !packageModel.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("The signed update package metadata is invalid.");

            var expectedUrl =
                $"https://github.com/{_repository}/releases/download/{release.TagName}/{packageModel.File}";
            if (!string.Equals(packageModel.Url, expectedUrl, StringComparison.Ordinal))
                throw new InvalidDataException("The signed package URL is outside the configured release.");

            return new UpdatePackage(
                packageModel.Kind,
                packageModel.File,
                ValidateGitHubAssetUri(packageModel.Url),
                packageModel.Sha256.ToLowerInvariant(),
                packageModel.Bytes);
        }

        private void VerifyManifestSignature(byte[] manifestBytes, byte[] signatureBytes)
        {
            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(
                    Encoding.UTF8.GetString(signatureBytes).Trim());
            }
            catch (FormatException exception)
            {
                throw new CryptographicException("The update signature encoding is invalid.", exception);
            }

            using var rsa = RSA.Create();
            rsa.FromXmlString(_publicKeyXml);
            if (!rsa.VerifyData(
                    manifestBytes,
                    signature,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1))
                throw new CryptographicException("The update manifest signature is invalid.");
        }

        private async Task<byte[]> ReadLimitedAsync(
            Uri uri,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length
                && length > maximumBytes)
                throw new InvalidDataException("The update response exceeds the allowed size.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            while (true)
            {
                var read = await stream.ReadAsync(chunk, cancellationToken);
                if (read == 0) break;
                if (buffer.Length + read > maximumBytes)
                    throw new InvalidDataException("The update response exceeds the allowed size.");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }

        private static Uri ValidateGitHubAssetUri(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update asset URL is not an HTTPS GitHub URL.");
            return uri;
        }

        private static bool IsRepositoryName(string value)
        {
            var parts = value.Split('/');
            return parts.Length == 2
                   && parts.All(part => part.Length > 0
                       && part.All(character =>
                           char.IsLetterOrDigit(character)
                           || character is '-' or '_' or '.'));
        }

        private static bool TryParseTag(string value, out SemanticVersion version)
        {
            version = default;
            return value.StartsWith('v')
                   && SemanticVersion.TryParse(value[1..], out version);
        }

        public void Dispose()
        {
            if (_ownsClient)
                _httpClient.Dispose();
        }

        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; init; } = "";

            [JsonPropertyName("draft")]
            public bool Draft { get; init; }

            [JsonPropertyName("prerelease")]
            public bool Prerelease { get; init; }

            [JsonPropertyName("assets")]
            public List<GitHubAsset> Assets { get; init; } = [];
        }

        private sealed class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; init; } = "";

            [JsonPropertyName("browser_download_url")]
            public string DownloadUrl { get; init; } = "";
        }

        private sealed class UpdateManifest
        {
            [JsonPropertyName("schema_version")]
            public int SchemaVersion { get; init; }

            [JsonPropertyName("version")]
            public string Version { get; init; } = "";

            [JsonPropertyName("channel")]
            public string Channel { get; init; } = "";

            [JsonPropertyName("source_commit")]
            public string SourceCommit { get; init; } = "";

            [JsonPropertyName("release_page")]
            public string ReleasePage { get; init; } = "";

            [JsonPropertyName("packages")]
            public List<UpdatePackageModel> Packages { get; init; } = [];
        }

        private sealed class UpdatePackageModel
        {
            [JsonPropertyName("kind")]
            public string Kind { get; init; } = "";

            [JsonPropertyName("file")]
            public string File { get; init; } = "";

            [JsonPropertyName("url")]
            public string Url { get; init; } = "";

            [JsonPropertyName("sha256")]
            public string Sha256 { get; init; } = "";

            [JsonPropertyName("bytes")]
            public long Bytes { get; init; }
        }
    }

    public readonly record struct SemanticVersion(
        int Major,
        int Minor,
        int Patch,
        string? Prerelease) : IComparable<SemanticVersion>
    {
        public static SemanticVersion Parse(string value)
            => TryParse(value, out var version)
                ? version
                : throw new FormatException($"Invalid semantic version: {value}");

        public static bool TryParse(string? value, out SemanticVersion version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var pieces = value.Split('-', 2);
            var numbers = pieces[0].Split('.');
            if (numbers.Length != 3
                || !int.TryParse(numbers[0], out var major)
                || !int.TryParse(numbers[1], out var minor)
                || !int.TryParse(numbers[2], out var patch)
                || major < 0 || minor < 0 || patch < 0)
                return false;
            var prerelease = pieces.Length == 2 ? pieces[1] : null;
            if (pieces.Length == 2
                && (string.IsNullOrWhiteSpace(prerelease)
                    || prerelease.Any(character =>
                        !(char.IsLetterOrDigit(character)
                          || character is '-' or '.'))))
                return false;
            version = new SemanticVersion(major, minor, patch, prerelease);
            return true;
        }

        public int CompareTo(SemanticVersion other)
        {
            var core = Major.CompareTo(other.Major);
            if (core == 0) core = Minor.CompareTo(other.Minor);
            if (core == 0) core = Patch.CompareTo(other.Patch);
            if (core != 0) return core;
            if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
            if (other.Prerelease is null) return -1;

            var left = Prerelease.Split('.');
            var right = other.Prerelease.Split('.');
            for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
            {
                if (index >= left.Length) return -1;
                if (index >= right.Length) return 1;
                var leftNumeric = int.TryParse(left[index], out var leftNumber);
                var rightNumeric = int.TryParse(right[index], out var rightNumber);
                int comparison;
                if (leftNumeric && rightNumeric)
                    comparison = leftNumber.CompareTo(rightNumber);
                else if (leftNumeric)
                    comparison = -1;
                else if (rightNumeric)
                    comparison = 1;
                else
                    comparison = string.Compare(left[index], right[index], StringComparison.Ordinal);
                if (comparison != 0) return comparison;
            }
            return 0;
        }

        public static bool operator >(SemanticVersion left, SemanticVersion right)
            => left.CompareTo(right) > 0;

        public static bool operator <(SemanticVersion left, SemanticVersion right)
            => left.CompareTo(right) < 0;

        public static bool operator >=(SemanticVersion left, SemanticVersion right)
            => left.CompareTo(right) >= 0;

        public static bool operator <=(SemanticVersion left, SemanticVersion right)
            => left.CompareTo(right) <= 0;
    }
}
