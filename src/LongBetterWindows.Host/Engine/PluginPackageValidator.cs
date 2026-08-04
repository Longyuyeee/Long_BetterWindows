using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Engine
{
    public sealed class PluginPackageValidator
    {
        public const int MaximumEntryCount = 2048;
        public const long MaximumUncompressedBytes = 256L * 1024 * 1024;
        public static Version CurrentUiKitVersion => PluginUiKitVersion.CurrentVersion;

        private readonly Version _hostVersion;
        private readonly Version _uiKitVersion;
        private readonly IPublisherTrustStore _trustStore;

        public PluginPackageValidator(
            Version? hostVersion = null,
            Version? uiKitVersion = null,
            IPublisherTrustStore? trustStore = null)
        {
            _hostVersion = hostVersion
                ?? Assembly.GetExecutingAssembly().GetName().Version
                ?? new Version(0, 0, 0);
            _uiKitVersion = uiKitVersion ?? CurrentUiKitVersion;
            _trustStore = trustStore ?? PublisherTrustStore.Empty;
        }

        public async Task<PackageValidationResult> ValidateAsync(
            string packagePath,
            MarketplacePackageMetadata? metadata = null,
            PluginManifest? installedManifest = null)
        {
            metadata ??= new MarketplacePackageMetadata();
            if (!File.Exists(packagePath))
                return PackageValidationResult.Fail("插件包不存在。");

            string sha256;
            try
            {
                await using var stream = File.OpenRead(packagePath);
                sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            }
            catch (Exception ex)
            {
                return PackageValidationResult.Fail($"无法读取插件包：{ex.Message}");
            }

            if (!string.IsNullOrWhiteSpace(metadata.ExpectedSha256)
                && !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(sha256),
                    ParseHash(metadata.ExpectedSha256)))
                return PackageValidationResult.Fail("插件包 SHA-256 与 Registry 元数据不一致。", sha256);

            var trust = PackageTrustLevel.LocalUnsigned;
            if (metadata.Source == MarketplaceSourceKind.RemoteRegistry
                && (string.IsNullOrWhiteSpace(metadata.Signature)
                    || string.IsNullOrWhiteSpace(metadata.PublisherKeyId)))
                return PackageValidationResult.Fail("远程市场包缺少发布者签名。", sha256);

            string? verificationKey = metadata.PublisherPublicKeyPem;
            if (metadata.Source == MarketplaceSourceKind.RemoteRegistry)
            {
                if (!_trustStore.TryGetPublicKey(metadata.PublisherKeyId!, out verificationKey))
                    return PackageValidationResult.Fail("发布者密钥不在 Long 信任库中。", sha256);
                if (!string.IsNullOrWhiteSpace(metadata.PublisherPublicKeyPem)
                    && !SamePublicKey(metadata.PublisherPublicKeyPem, verificationKey))
                    return PackageValidationResult.Fail("Registry 发布者公钥与信任库不一致。", sha256);
            }

            if (!string.IsNullOrWhiteSpace(metadata.Signature)
                || !string.IsNullOrWhiteSpace(verificationKey))
            {
                if (!VerifySignature(sha256, metadata.Signature, verificationKey, out var signatureError))
                    return PackageValidationResult.Fail(signatureError!, sha256);
                trust = PackageTrustLevel.PublisherSigned;
            }

            string stagingDir = Path.Combine(Path.GetTempPath(), $"long-package-validation-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(stagingDir);
                using var archive = ZipFile.OpenRead(packagePath);
                var structureError = ValidateStructure(archive);
                if (structureError != null)
                    return PackageValidationResult.Fail(structureError, sha256);

                var fileManifestError = ValidateFileManifest(
                    archive,
                    out var fileManifestPluginId,
                    out var fileManifestVersion);
                if (fileManifestError != null)
                    return PackageValidationResult.Fail(fileManifestError, sha256);

                ExtractSafely(archive, stagingDir);
                var manifestResult = await ManifestReader.ReadAsync(stagingDir);
                if (!manifestResult.IsSuccess)
                    return PackageValidationResult.Fail(
                        $"manifest.json 无效：{manifestResult.Error}",
                        sha256,
                        manifestFailureCode: manifestResult.ErrorCode,
                        manifestIssues: manifestResult.Issues);

                var manifest = manifestResult.Manifest!;
                if (!Matches(metadata.ExpectedPluginId, manifest.Id))
                    return PackageValidationResult.Fail("插件 ID 与市场元数据不一致。", sha256);
                if (!Matches(metadata.ExpectedVersion, manifest.Version))
                    return PackageValidationResult.Fail("插件版本与市场元数据不一致。", sha256);
                if (!Matches(fileManifestPluginId, manifest.Id)
                    || !Matches(fileManifestVersion, manifest.Version))
                {
                    return PackageValidationResult.Fail(
                        "插件文件总账身份与 manifest.json 不一致。",
                        sha256,
                        manifest);
                }

                if (metadata.Source == MarketplaceSourceKind.RemoteRegistry
                    && RequiresHighTrust(manifest))
                    return PackageValidationResult.Fail(
                        "远程插件市场当前只允许纯 Web 插件；原生、脚本或带原生后台的 Hybrid 插件必须通过本地导入并接受完全信任提示。",
                        sha256,
                        manifest);

                var contentError = ValidateDirectoryContents(stagingDir, manifest);
                if (contentError != null)
                    return PackageValidationResult.Fail(contentError, sha256, manifest);

                var compatibilityError = ValidateCompatibility(manifest);
                if (compatibilityError != null)
                    return PackageValidationResult.Fail(compatibilityError, sha256, manifest);

                return PackageValidationResult.Ok(
                    manifest,
                    sha256,
                    trust,
                    CreatePermissionDiff(installedManifest?.Capabilities, manifest.Capabilities),
                    RequiresHighTrust(manifest));
            }
            catch (InvalidDataException ex)
            {
                return PackageValidationResult.Fail($"插件包已损坏：{ex.Message}", sha256);
            }
            catch (Exception ex)
            {
                return PackageValidationResult.Fail($"插件包校验失败：{ex.Message}", sha256);
            }
            finally
            {
                try { Directory.Delete(stagingDir, true); } catch { }
            }
        }

        public async Task<PackageValidationResult> ValidateDirectoryAsync(
            string pluginDirectory,
            PluginManifest? installedManifest = null)
        {
            if (!Directory.Exists(pluginDirectory))
                return PackageValidationResult.Fail("插件目录不存在。");

            string root;
            try
            {
                root = Path.GetFullPath(pluginDirectory);
            }
            catch (Exception ex)
            {
                return PackageValidationResult.Fail($"插件目录路径无效：{ex.Message}");
            }

            var manifestResult = await ManifestReader.ReadAsync(root);
            if (!manifestResult.IsSuccess)
            {
                return PackageValidationResult.Fail(
                    $"manifest.json 无效：{manifestResult.Error}",
                    manifestFailureCode: manifestResult.ErrorCode,
                    manifestIssues: manifestResult.Issues);
            }

            var manifest = manifestResult.Manifest!;
            var contentError = ValidateDirectoryContents(root, manifest);
            if (contentError != null)
                return PackageValidationResult.Fail(contentError, manifest: manifest);

            var compatibilityError = ValidateCompatibility(manifest);
            if (compatibilityError != null)
                return PackageValidationResult.Fail(compatibilityError, manifest: manifest);

            return PackageValidationResult.Ok(
                manifest,
                sha256: null,
                trust: PackageTrustLevel.LocalUnsigned,
                permissionDiff: CreatePermissionDiff(
                    installedManifest?.Capabilities,
                    manifest.Capabilities),
                highTrust: RequiresHighTrust(manifest));
        }

        public static PermissionDiff CreatePermissionDiff(
            IEnumerable<string>? installed,
            IEnumerable<string>? requested)
        {
            var oldSet = new HashSet<string>(installed ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var newSet = new HashSet<string>(requested ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return new PermissionDiff
            {
                Added = newSet.Except(oldSet).OrderBy(x => x).ToArray(),
                Removed = oldSet.Except(newSet).OrderBy(x => x).ToArray(),
                Unchanged = newSet.Intersect(oldSet).OrderBy(x => x).ToArray(),
            };
        }

        internal static void ExtractSafely(ZipArchive archive, string destination)
        {
            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("插件包包含路径穿越条目。");
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
        }

        private string? ValidateCompatibility(PluginManifest manifest)
        {
            if (!IsAtLeast(_hostVersion, manifest.MinHostVersion, out var hostRequirement))
                return $"宿主版本不兼容：需要 {hostRequirement}，当前 {_hostVersion.ToString(3)}。";
            if (!IsAtLeast(_uiKitVersion, manifest.MinUiKitVersion, out var uiRequirement))
                return $"UI Kit 版本不兼容：需要 {uiRequirement}，当前 {_uiKitVersion.ToString(3)}。";
            if (!string.IsNullOrWhiteSpace(manifest.MinApiVersion)
                && (!TryVersion(manifest.MinApiVersion, out var api)
                    || !ApiVersion.Current.IsCompatibleWith(
                        new ApiVersion(api.Major, api.Minor, Math.Max(0, api.Build)))))
                return $"API 版本不兼容：需要 {manifest.MinApiVersion}，当前 {ApiVersion.Current}。";
            return null;
        }

        private static string? ValidateDirectoryContents(
            string root,
            PluginManifest manifest)
        {
            if (!IsExistingFileWithin(root, manifest.EntryPoint))
                return "插件入口文件不存在或越出插件根目录。";

            if (manifest.Background is { } background
                && !IsExistingFileWithin(root, background.EntryPoint))
            {
                return "插件原生后台入口不存在或越出插件根目录。";
            }

            if (manifest.Localization is { } localization)
            {
                foreach (var resource in localization.Resources)
                {
                    if (!IsExistingFileWithin(root, resource.Value))
                    {
                        return $"插件语言资源不存在或越出插件根目录：{resource.Key}。";
                    }
                }
            }

            foreach (var widget in manifest.Widgets ?? new List<PluginWidgetDefinition>())
            {
                if (!IsExistingFileWithin(root, widget.EntryPoint))
                    return $"插件 Widget 入口不存在或越出插件根目录：{widget.Id}。";
                if (!string.IsNullOrWhiteSpace(widget.Icon)
                    && !IsExistingFileWithin(root, widget.Icon!))
                {
                    return $"插件 Widget 图标不存在或越出插件根目录：{widget.Id}。";
                }
            }

            return null;
        }

        private static bool IsExistingFileWithin(string root, string relativePath)
        {
            try
            {
                var path = Path.GetFullPath(Path.Combine(root, relativePath));
                return IsWithin(root, path) && File.Exists(path);
            }
            catch (Exception exception) when (exception is
                ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }

        private static string? ValidateStructure(ZipArchive archive)
        {
            if (archive.Entries.Count == 0) return "插件包为空。";
            if (archive.Entries.Count > MaximumEntryCount) return "插件包文件数量超过安全限制。";
            long total = 0;
            var manifests = 0;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                total = checked(total + entry.Length);
                if (total > MaximumUncompressedBytes) return "插件包解压后体积超过安全限制。";
                var normalized = entry.FullName.Replace('\\', '/');
                if (normalized.StartsWith('/') || normalized.Contains("../", StringComparison.Ordinal)
                    || Path.IsPathRooted(entry.FullName))
                    return "插件包包含非法路径。";
                if (!paths.Add(normalized))
                    return $"插件包包含重复路径：{normalized}。";
                if (string.Equals(normalized, "manifest.json", StringComparison.OrdinalIgnoreCase)) manifests++;
            }
            return manifests == 1 ? null : "插件包根目录必须且只能包含一个 manifest.json。";
        }

        private static string? ValidateFileManifest(
            ZipArchive archive,
            out string? pluginId,
            out string? version)
        {
            pluginId = null;
            version = null;
            var ledgerEntries = archive.Entries
                .Where(entry => string.Equals(
                    entry.FullName.Replace('\\', '/'),
                    "package-files.json",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (ledgerEntries.Length == 0)
                return null;
            if (ledgerEntries.Length != 1)
                return "插件包根目录最多只能包含一个 package-files.json。";

            try
            {
                using var stream = ledgerEntries[0].Open();
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                if (root.GetProperty("schema_version").GetInt32() != 1
                    || !string.Equals(
                        root.GetProperty("classification").GetString(),
                        "long_plugin_file_manifest",
                        StringComparison.Ordinal))
                {
                    return "插件文件总账版本或分类无效。";
                }

                pluginId = root.GetProperty("plugin_id").GetString();
                version = root.GetProperty("version").GetString();
                if (string.IsNullOrWhiteSpace(pluginId)
                    || string.IsNullOrWhiteSpace(version))
                {
                    return "插件文件总账缺少插件身份。";
                }

                var declared = new Dictionary<
                    string,
                    (long Size, string Sha256)>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var item in root.GetProperty("files").EnumerateArray())
                {
                    var path = item.GetProperty("path").GetString() ?? string.Empty;
                    var normalized = path.Replace('\\', '/');
                    var size = item.GetProperty("size").GetInt64();
                    var hash = item.GetProperty("sha256").GetString() ?? string.Empty;
                    if (normalized.Length == 0
                        || normalized.StartsWith('/')
                        || normalized.Contains("../", StringComparison.Ordinal)
                        || Path.IsPathRooted(path)
                        || string.Equals(
                            normalized,
                            "package-files.json",
                            StringComparison.OrdinalIgnoreCase)
                        || size < 0
                        || hash.Length != 64
                        || !hash.All(Uri.IsHexDigit)
                        || !declared.TryAdd(normalized, (size, hash)))
                    {
                        return "插件文件总账包含无效或重复条目。";
                    }
                }

                var actualFiles = archive.Entries
                    .Where(entry => !string.IsNullOrEmpty(entry.Name))
                    .Where(entry => !string.Equals(
                        entry.FullName.Replace('\\', '/'),
                        "package-files.json",
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (actualFiles.Length != declared.Count)
                    return "插件文件总账与包内文件数量不一致。";

                foreach (var entry in actualFiles)
                {
                    var path = entry.FullName.Replace('\\', '/');
                    if (!declared.TryGetValue(path, out var expected)
                        || expected.Size != entry.Length)
                    {
                        return $"插件文件总账与包内文件不一致：{path}。";
                    }

                    using var content = entry.Open();
                    var actualHash = Convert.ToHexString(SHA256.HashData(content));
                    if (!CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(actualHash),
                        Convert.FromHexString(expected.Sha256)))
                    {
                        return $"插件文件总账 SHA-256 不一致：{path}。";
                    }
                }

                return null;
            }
            catch (Exception exception) when (exception is
                InvalidDataException or InvalidOperationException
                or FormatException or KeyNotFoundException
                or JsonException or OverflowException)
            {
                return $"插件文件总账无效：{exception.Message}";
            }
        }

        private static bool VerifySignature(
            string sha256,
            string? signature,
            string? publicKeyPem,
            out string? error)
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(publicKeyPem);
                var valid = rsa.VerifyHash(
                    Convert.FromHexString(sha256),
                    Convert.FromBase64String(signature!),
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                error = valid ? null : "发布者签名无效，插件包可能已被篡改。";
                return valid;
            }
            catch (Exception ex)
            {
                error = $"无法验证发布者签名：{ex.Message}";
                return false;
            }
        }

        private static bool SamePublicKey(string left, string right)
        {
            try
            {
                using var leftRsa = RSA.Create();
                using var rightRsa = RSA.Create();
                leftRsa.ImportFromPem(left);
                rightRsa.ImportFromPem(right);
                return CryptographicOperations.FixedTimeEquals(
                    leftRsa.ExportSubjectPublicKeyInfo(),
                    rightRsa.ExportSubjectPublicKeyInfo());
            }
            catch { return false; }
        }

        private static byte[] ParseHash(string value)
        {
            try { return Convert.FromHexString(value.Replace("-", string.Empty).Trim()); }
            catch { return Array.Empty<byte>(); }
        }

        private static bool Matches(string? expected, string actual)
            => string.IsNullOrWhiteSpace(expected)
                || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

        private static bool IsWithin(string root, string path)
            => path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

        private static bool IsAtLeast(Version current, string? required, out string requirement)
        {
            requirement = required ?? string.Empty;
            return string.IsNullOrWhiteSpace(required)
                || (TryVersion(required, out var parsed) && current >= parsed);
        }

        private static bool TryVersion(string? value, out Version version)
            => Version.TryParse(value?.TrimStart('v', 'V'), out version!);

        private static bool RequiresHighTrust(PluginManifest manifest)
            => !string.Equals(
                    manifest.Runtime,
                    "webview",
                    StringComparison.OrdinalIgnoreCase)
                || manifest.Background is not null;
    }

    public interface IPublisherTrustStore
    {
        bool TryGetPublicKey(string keyId, out string publicKeyPem);
    }

    public sealed class PublisherTrustStore : IPublisherTrustStore
    {
        private readonly IReadOnlyDictionary<string, string> _keys;
        public static PublisherTrustStore Empty { get; } = new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        public PublisherTrustStore(IReadOnlyDictionary<string, string> keys) => _keys = keys;

        public bool TryGetPublicKey(string keyId, out string publicKeyPem)
            => _keys.TryGetValue(keyId, out publicKeyPem!);
    }

    public sealed class PackageValidationResult
    {
        public bool IsSuccess { get; init; }
        public string? Error { get; init; }
        public PluginManifest? Manifest { get; init; }
        public string? Sha256 { get; init; }
        public PackageTrustLevel TrustLevel { get; init; }
        public PermissionDiff PermissionDiff { get; init; } = new();
        public bool RequiresHighTrustWarning { get; init; }
        public ManifestErrorCode? ManifestFailureCode { get; init; }
        public IReadOnlyList<ManifestValidationIssue> ManifestIssues { get; init; }
            = Array.Empty<ManifestValidationIssue>();

        public static PackageValidationResult Ok(
            PluginManifest manifest, string? sha256, PackageTrustLevel trust,
            PermissionDiff permissionDiff, bool highTrust)
            => new()
            {
                IsSuccess = true,
                Manifest = manifest,
                Sha256 = sha256,
                TrustLevel = trust,
                PermissionDiff = permissionDiff,
                RequiresHighTrustWarning = highTrust,
            };

        public static PackageValidationResult Fail(
            string error,
            string? sha256 = null,
            PluginManifest? manifest = null,
            ManifestErrorCode? manifestFailureCode = null,
            IReadOnlyList<ManifestValidationIssue>? manifestIssues = null)
            => new()
            {
                Error = error,
                Sha256 = sha256,
                Manifest = manifest,
                ManifestFailureCode = manifestFailureCode,
                ManifestIssues = manifestIssues ?? Array.Empty<ManifestValidationIssue>(),
            };
    }
}
