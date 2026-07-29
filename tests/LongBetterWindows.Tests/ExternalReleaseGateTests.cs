using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace LongBetterWindows.Tests;

public sealed class ExternalReleaseGateTests : IDisposable
{
    private const string Commit = "1111111111111111111111111111111111111111";
    private static readonly byte[] PackageContent = "self-contained fixture"u8.ToArray();
    private static readonly byte[] FrameworkPackageContent = "framework-dependent fixture"u8.ToArray();
    private static readonly byte[] InstallerContent = "installer fixture"u8.ToArray();
    private static readonly string PackageHash = Hash(PackageContent);
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "long-external-release-gate-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task VerifyExternalReleaseGate_AcceptsOneConsistentUnsignedCandidate()
    {
        var paths = WriteFixture(PackageHash);
        var output = Path.Combine(_root, "decision.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.True(result.ExitCode == 0, result.Error);
        using var decision = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        var root = decision.RootElement;
        Assert.True(root.GetProperty("passed").GetBoolean());
        Assert.Equal(Commit, root.GetProperty("source_commit").GetString());
        Assert.Equal("unsigned", root.GetProperty("distribution_channel").GetString());
        Assert.False(root.GetProperty("signed").GetBoolean());
        Assert.Equal(
            PackageHash,
            root.GetProperty("package").GetProperty("sha256").GetString());
        Assert.Equal(
            "1.11.0",
            root.GetProperty("candidate").GetProperty("version").GetString());
        Assert.False(
            root.GetProperty("candidate").GetProperty("source_dirty").GetBoolean());
        Assert.Equal(
            2,
            root.GetProperty("candidate").GetProperty("package_count").GetInt32());
        Assert.Equal(
            1,
            root.GetProperty("candidate").GetProperty("installer_count").GetInt32());
        Assert.True(
            root.GetProperty("candidate").GetProperty("artifact_files_verified").GetBoolean());
        Assert.True(
            root.GetProperty("candidate").GetProperty("checksum_file_verified").GetBoolean());
        Assert.Equal(
            "registry.example.test",
            root.GetProperty("marketplace").GetProperty("destination_host").GetString());
        Assert.Equal(
            32,
            root.GetProperty("evidence_contract")
                .GetProperty("physical_dpi_capture_count")
                .GetInt32());
        Assert.Equal(
            1,
            root.GetProperty("evidence_contract")
                .GetProperty("screen_reader_approval_count")
                .GetInt32());
        Assert.Equal(
            3,
            root.GetProperty("evidence_contract")
                .GetProperty("physical_dpi_schema_version")
                .GetInt32());
        Assert.Equal(
            3,
            root.GetProperty("evidence_contract")
                .GetProperty("accessibility_schema_version")
                .GetInt32());
        Assert.Equal(
            2,
            root.GetProperty("evidence_contract")
                .GetProperty("download_schema_version")
                .GetInt32());
        Assert.Equal(
            2,
            root.GetProperty("evidence_contract")
                .GetProperty("clean_environment_schema_version")
                .GetInt32());
        Assert.Equal(
            2,
            root.GetProperty("evidence_contract")
                .GetProperty("marketplace_rehearsal_schema_version")
                .GetInt32());
        Assert.All(
            root.GetProperty("inputs").EnumerateObject(),
            input => Assert.Matches("^[0-9a-f]{64}$", input.Value.GetString()));
        Assert.Empty(Directory.GetFiles(_root, ".*.tmp"));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_PreflightValidatesWithoutWritingDecision()
    {
        var paths = WriteFixture(PackageHash);

        var result = await RunVerifierAsync(paths, output: null, preflightOnly: true);

        Assert.True(result.ExitCode == 0, result.Error);
        using var preflight = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "external_release_gate_preflight",
            preflight.RootElement.GetProperty("classification").GetString());
        Assert.True(preflight.RootElement.GetProperty("passed").GetBoolean());
        Assert.True(preflight.RootElement.GetProperty("preflight_only").GetBoolean());
        Assert.Empty(Directory.GetFiles(_root, "*decision*.json"));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_PreflightRejectsOutputPath()
    {
        var paths = WriteFixture(PackageHash);
        var output = Path.Combine(_root, "preflight-must-not-write.json");

        var result = await RunVerifierAsync(paths, output, preflightOnly: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("does not accept OutputPath", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_PreservesExistingDecision()
    {
        var paths = WriteFixture(PackageHash);
        var output = Path.Combine(_root, "existing-decision.json");
        const string existingContent = "previous immutable decision";
        await File.WriteAllTextAsync(output, existingContent);

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("decision already exists", result.Error);
        Assert.Equal(existingContent, await File.ReadAllTextAsync(output));
        Assert.Empty(Directory.GetFiles(_root, ".*.tmp"));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_AllowsOnlyOneConcurrentDecisionWriter()
    {
        var paths = WriteFixture(PackageHash);
        var output = Path.Combine(_root, "contended-decision.json");

        var results = await Task.WhenAll(
            RunVerifierAsync(paths, output),
            RunVerifierAsync(paths, output));

        Assert.Single(results, result => result.ExitCode == 0);
        var rejected = Assert.Single(results, result => result.ExitCode != 0);
        Assert.Contains("decision already exists", rejected.Error);
        using var decision = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.True(decision.RootElement.GetProperty("passed").GetBoolean());
        Assert.Empty(Directory.GetFiles(_root, ".*.tmp"));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsPackageIdentityMismatch()
    {
        var paths = WriteFixture(
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        var output = Path.Combine(_root, "rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("refer to different packages", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsPreflightOnlyMarketplaceEvidence()
    {
        var paths = WriteFixture(PackageHash, marketplacePreflightOnly: true);
        var output = Path.Combine(_root, "preflight-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("complete passing deploy and rollback cycle", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsLegacyPhysicalDpiSummary()
    {
        var paths = WriteFixture(PackageHash, physicalDpiSchemaVersion: 2);
        var output = Path.Combine(_root, "legacy-dpi-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Physical DPI gate schema version 3 is required", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsIncompletePhysicalDpiSummary()
    {
        var paths = WriteFixture(PackageHash, physicalDpiCaptureCount: 24);
        var output = Path.Combine(_root, "incomplete-dpi-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must contain exactly 32 captures", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsLegacyAccessibilitySummary()
    {
        var paths = WriteFixture(PackageHash, accessibilitySchemaVersion: 2);
        var output = Path.Combine(_root, "legacy-accessibility-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Accessibility gate schema version 3 is required", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsAccessibilityWithoutScreenReader()
    {
        var paths = WriteFixture(PackageHash, screenReaderApprovalCount: 0);
        var output = Path.Combine(_root, "screen-reader-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("requires at least one screen-reader approval", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsTamperedPhysicalDpiPortableSource()
    {
        var paths = WriteFixture(PackageHash);
        await File.AppendAllTextAsync(paths.DpiSource, "tampered");
        var output = Path.Combine(_root, "dpi-source-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Physical DPI evidence portable source hash mismatch", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsMissingAccessibilityPortableSource()
    {
        var paths = WriteFixture(PackageHash);
        File.Delete(paths.AccessibilitySource);
        var output = Path.Combine(_root, "accessibility-source-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Accessibility evidence portable source hash mismatch", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsIncompleteDownloadSummary()
    {
        var paths = WriteFixture(PackageHash, downloadSchemaVersion: 0);
        var output = Path.Combine(_root, "download-contract-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Release-download gate summary contract is incomplete", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsIncompleteCleanEnvironmentSummary()
    {
        var paths = WriteFixture(PackageHash, cleanSchemaVersion: 0);
        var output = Path.Combine(_root, "clean-contract-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Clean-environment gate summary contract is incomplete", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsTamperedDownloadSource()
    {
        var paths = WriteFixture(PackageHash);
        await File.AppendAllTextAsync(paths.DownloadEvidence, "tampered");
        var output = Path.Combine(_root, "download-source-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Release-download evidence source hash mismatch", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsMissingCleanEnvironmentSource()
    {
        var paths = WriteFixture(PackageHash);
        File.Delete(paths.CleanEvidence);
        var output = Path.Combine(_root, "clean-source-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Clean-environment evidence source hash mismatch", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsHashLockedDownloadContentMismatch()
    {
        var paths = WriteFixture(
            PackageHash,
            downloadSourcePackageMismatch: true);
        var output = Path.Combine(_root, "download-content-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("evidence source content does not match its summary", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsHashLockedCleanReviewerMismatch()
    {
        var paths = WriteFixture(
            PackageHash,
            cleanSourceReviewerMismatch: true);
        var output = Path.Combine(_root, "clean-content-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Clean-environment evidence source content", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsLegacyMarketplaceSummary()
    {
        var paths = WriteFixture(PackageHash, marketplaceSchemaVersion: 1);
        var output = Path.Combine(_root, "marketplace-schema-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Marketplace rehearsal schema version 2 is required", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsTamperedMarketplaceEvidence()
    {
        var paths = WriteFixture(PackageHash);
        File.AppendAllText(paths.MarketplaceDeploymentEvidence, "tampered");
        var output = Path.Combine(_root, "marketplace-tamper-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Marketplace rehearsal evidence deployment source hash mismatch", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsHashLockedMarketplaceReleaseMismatch()
    {
        var paths = WriteFixture(
            PackageHash,
            marketplaceDeploymentReleaseMismatch: true);
        var output = Path.Combine(_root, "marketplace-release-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("deployment report content does not match", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsHashLockedMarketplaceRollbackMismatch()
    {
        var paths = WriteFixture(
            PackageHash,
            marketplaceRollbackMismatch: true);
        var output = Path.Combine(_root, "marketplace-rollback-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("did not restore the baseline Registry", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsLegacyReleaseManifest()
    {
        var paths = WriteFixture(PackageHash, releaseSchemaVersion: 0);
        var output = Path.Combine(_root, "release-schema-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("candidate identity contract is incomplete", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsDirtyReleaseManifest()
    {
        var paths = WriteFixture(PackageHash, releaseSourceDirty: true);
        var output = Path.Combine(_root, "dirty-release-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("candidate identity contract is incomplete", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsInvalidPackageInventory()
    {
        var paths = WriteFixture(PackageHash, invalidPackageInventory: true);
        var output = Path.Combine(_root, "package-inventory-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Release Manifest package inventory is invalid", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsTamperedReleasePackage()
    {
        var paths = WriteFixture(PackageHash);
        await File.AppendAllTextAsync(paths.Package, "tampered");
        var output = Path.Combine(_root, "package-tamper-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("artifact file size does not match the Manifest", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsMissingReleasePackage()
    {
        var paths = WriteFixture(PackageHash);
        File.Delete(paths.FrameworkPackage);
        var output = Path.Combine(_root, "package-missing-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Release artifact file was not found", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsTamperedInstaller()
    {
        var paths = WriteFixture(PackageHash);
        await File.AppendAllTextAsync(paths.Installer, "tampered");
        var output = Path.Combine(_root, "installer-tamper-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("artifact file size does not match the Manifest", result.Error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task VerifyExternalReleaseGate_RejectsIncompleteChecksumFile()
    {
        var paths = WriteFixture(PackageHash);
        File.WriteAllLines(paths.Checksums, new[]
        {
            $"{PackageHash}  LongBetterWindows.zip",
        });
        var output = Path.Combine(_root, "checksums-rejected.json");

        var result = await RunVerifierAsync(paths, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("exact Manifest artifact set", result.Error);
        Assert.False(File.Exists(output));
    }

    private FixturePaths WriteFixture(
        string cleanPackageHash,
        bool marketplacePreflightOnly = false,
        int physicalDpiSchemaVersion = 3,
        int physicalDpiCaptureCount = 32,
        int accessibilitySchemaVersion = 3,
        int screenReaderApprovalCount = 1,
        int downloadSchemaVersion = 2,
        int cleanSchemaVersion = 2,
        int marketplaceSchemaVersion = 2,
        int releaseSchemaVersion = 1,
        bool releaseSourceDirty = false,
        bool invalidPackageInventory = false,
        bool downloadSourcePackageMismatch = false,
        bool cleanSourceReviewerMismatch = false,
        bool marketplaceDeploymentReleaseMismatch = false,
        bool marketplaceRollbackMismatch = false)
    {
        Directory.CreateDirectory(_root);
        var packagePath = Path.Combine(_root, "LongBetterWindows.zip");
        var frameworkPackagePath = Path.Combine(
            _root,
            "LongBetterWindows-framework-dependent.zip");
        File.WriteAllBytes(packagePath, PackageContent);
        File.WriteAllBytes(frameworkPackagePath, FrameworkPackageContent);
        var installerPath = Path.Combine(_root, "LongAssistant-Setup.exe");
        File.WriteAllBytes(installerPath, InstallerContent);
        var frameworkPackageHash = Hash(FrameworkPackageContent);
        var installerHash = Hash(InstallerContent);
        var release = WriteJson("release.json", new
        {
            schema_version = releaseSchemaVersion,
            product = "Long Assistant",
            version = "1.11.0",
            runtime = "win-x64",
            created_at = "2026-07-29T00:00:00Z",
            commit = Commit,
            source_dirty = releaseSourceDirty,
            distribution_channel = "unsigned",
            publisher_identity = "unverified",
            security_notice = "Publisher identity is unverified; validate SHA-256.",
            release_eligible = true,
            signed = false,
            packages = new[]
            {
                ReleasePackage(
                    "LongBetterWindows.zip",
                    "self-contained",
                    PackageHash,
                    PackageContent.LongLength,
                    commandCount: invalidPackageInventory ? 41 : 42),
                ReleasePackage(
                    "LongBetterWindows-framework-dependent.zip",
                    "framework-dependent",
                    frameworkPackageHash,
                    FrameworkPackageContent.LongLength,
                    commandCount: 42),
            },
            installers = new[]
            {
                new
                {
                    file = "LongAssistant-Setup.exe",
                    kind = "installer",
                    format = "inno-setup-exe",
                    install_scope = "current-user",
                    requires_elevation = false,
                    sha256 = installerHash,
                    bytes = InstallerContent.LongLength,
                    plugins = 25,
                    commands = 42,
                    signed = false,
                },
            },
        });
        var checksumsPath = Path.Combine(_root, "SHA256SUMS.txt");
        File.WriteAllLines(checksumsPath, new[]
        {
            $"{PackageHash}  LongBetterWindows.zip",
            $"{frameworkPackageHash}  LongBetterWindows-framework-dependent.zip",
            $"{installerHash}  LongAssistant-Setup.exe",
        });
        var downloadEvidencePath = Path.Combine(_root, "download-evidence.json");
        var downloadApprovalPath = Path.Combine(_root, "download-approval.json");
        var cleanEvidencePath = Path.Combine(_root, "clean-environment-evidence.json");
        WritePortableSource("download-evidence.json", new
        {
            classification = "verified_release_download_provenance",
            passed = true,
            release = new
            {
                source_commit = Commit,
                distribution_channel = "unsigned",
            },
            package = new
            {
                file = "LongBetterWindows.zip",
                sha256 = downloadSourcePackageMismatch
                    ? new string('f', 64)
                    : PackageHash,
            },
            windows_origin = new { host = new { host = "github.com" } },
        });
        WritePortableSource("download-approval.json", new
        {
            classification = "release_download_human_approval",
            source_commit = Commit,
            distribution_channel = "unsigned",
            package = new
            {
                file = "LongBetterWindows.zip",
                sha256 = PackageHash,
            },
            evidence = EvidenceEntry("download-evidence.json"),
            @operator = "capture-user",
            reviewer = "review-user",
        });
        WritePortableSource("clean-environment-evidence.json", new
        {
            classification = "clean_windows_release_evidence",
            release = new
            {
                version = "1.11.0",
                source_commit = Commit,
                distribution_channel = "unsigned",
                package_sha256 = cleanPackageHash,
                signed = false,
            },
            environment = new { label = "clean-vm" },
            automated_checks = new { passed = true },
            human_review = new
            {
                status = "approved",
                reviewer = cleanSourceReviewerMismatch
                    ? "different-reviewer"
                    : "clean-reviewer",
            },
        });
        var download = WriteJson("download.json", new
        {
            schema_version = downloadSchemaVersion,
            classification = "approved_release_download_gate",
            passed = true,
            source_commit = Commit,
            distribution_channel = "unsigned",
            package_file = "LongBetterWindows.zip",
            package_sha256 = PackageHash,
            download_host = "github.com",
            @operator = "capture-user",
            reviewer = "review-user",
            evidence = EvidenceEntry("download-evidence.json"),
            approval = EvidenceEntry("download-approval.json"),
        });
        var clean = WriteJson("clean.json", new
        {
            schema_version = cleanSchemaVersion,
            classification = "approved_clean_windows_release_gate",
            passed = true,
            version = "1.11.0",
            source_commit = Commit,
            distribution_channel = "unsigned",
            signed = false,
            package_sha256 = cleanPackageHash,
            environment_label = "clean-vm",
            reviewer = "clean-reviewer",
            evidence_manifest = EvidenceEntry("clean-environment-evidence.json"),
        });
        var dpiEvidence = new[] { 100, 125, 150, 200 }.Select(scale =>
        {
            var relativePath = $"dpi.sources/physical-dpi-{scale}.json";
            WritePortableSource(relativePath, new
            {
                schema_version = 2,
                classification = "physical_device_dpi_evidence",
                source_commit = Commit,
                expected_scale_percent = scale,
                human_review = new { status = "approved" },
            });
            return new
            {
                scale_percent = scale,
                source_commit = Commit,
                capture_count = 8,
                source_manifest = EvidenceEntry(relativePath),
            };
        }).ToArray();
        var dpi = WriteJson("dpi.json", new
        {
            schema_version = physicalDpiSchemaVersion,
            classification = "approved_physical_device_dpi_matrix",
            passed = true,
            source_commit = Commit,
            required_scales = new[] { 100, 125, 150, 200 },
            capture_count = physicalDpiCaptureCount,
            evidence = dpiEvidence,
        });
        var accessibilityProfiles = new[]
        {
            "high_contrast",
            "reduced_motion",
            "combined",
        };
        var accessibilityEvidence = accessibilityProfiles.Select(profile =>
        {
            var relativePath = $"accessibility.sources/accessibility-{profile}.json";
            WritePortableSource(relativePath, new
            {
                schema_version = 2,
                classification = "physical_accessibility_evidence",
                source_commit = Commit,
                expected_profile = profile,
                human_review = new { status = "approved" },
            });
            return new
            {
                profile,
                source_commit = Commit,
                source_manifest = EvidenceEntry(relativePath),
            };
        }).ToArray();
        var accessibility = WriteJson("accessibility.json", new
        {
            schema_version = accessibilitySchemaVersion,
            classification = "approved_physical_accessibility_matrix",
            passed = true,
            source_commit = Commit,
            required_profiles = accessibilityProfiles,
            screen_reader_approval_count = screenReaderApprovalCount,
            evidence = accessibilityEvidence,
        });
        var marketplaceEvidence = WriteMarketplaceEvidence(
            marketplaceDeploymentReleaseMismatch,
            marketplaceRollbackMismatch);
        var marketplace = WriteJson("marketplace.json", new
        {
            schema_version = marketplaceSchemaVersion,
            started_at = "2026-07-29T00:00:00Z",
            classification = "marketplace_https_rehearsal",
            passed = true,
            destination = "https://registry.example.test/releases/",
            preflight_only = marketplacePreflightOnly,
            release_id = "release-20260723",
            preflight_dry_run_verified = true,
            baseline_verified = true,
            deployment_started = true,
            deployment_completed = true,
            deployment_verified = true,
            rollback_completed = true,
            rollback_verified = true,
            failure = (string?)null,
            rollback_failure = (string?)null,
            rollback_verification_failure = (string?)null,
            completed_at = "2026-07-29T00:00:06Z",
            evidence = new
            {
                preflight_dry_run = EvidenceEntry("preflight-dry-run.json"),
                baseline_verification = EvidenceEntry("baseline-verification.json"),
                deployment = EvidenceEntry("deployment.json"),
                deployed_verification = EvidenceEntry("deployed-verification.json"),
                rollback_verification = EvidenceEntry("rollback-verification.json"),
            },
        });
        return new FixturePaths(
            release,
            download,
            clean,
            dpi,
            accessibility,
            marketplace,
            marketplaceEvidence,
            packagePath,
            frameworkPackagePath,
            installerPath,
            checksumsPath,
            downloadEvidencePath,
            cleanEvidencePath,
            Path.Combine(_root, "dpi.sources", "physical-dpi-100.json"),
            Path.Combine(
                _root,
                "accessibility.sources",
                "accessibility-high_contrast.json"));
    }

    private static object ReleasePackage(
        string file,
        string kind,
        string sha256,
        long bytes,
        int commandCount) =>
        new
        {
            file,
            kind,
            sha256,
            bytes,
            plugins = 25,
            manifests = 25,
            unique_plugin_ids = 25,
            commands = commandCount,
            command_smoke_exit_code = 0,
            added_webview_processes = 0,
        };

    private static string Hash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private string WriteMarketplaceEvidence(
        bool deploymentReleaseMismatch,
        bool rollbackMismatch)
    {
        var deploymentFiles = new[]
        {
            new
            {
                RemotePath = "registry.json",
                Sha256 = new string('a', 64),
                Bytes = 512,
                Kind = "RegistryCommit",
            },
        };
        WriteJson("preflight-dry-run.json", new
        {
            SchemaVersion = 1,
            ReleaseId = "release-20260723",
            ExecutedAt = "2026-07-29T00:00:01Z",
            Mode = "dry_run",
            Target = "Https",
            Destination = "https://registry.example.test/releases/",
            Files = deploymentFiles,
        });
        WriteMarketplaceVerification(
            "baseline-verification.json",
            "2026-07-29T00:00:02Z",
            "2026-07-28T00:00:00Z",
            "baseline.plugin");
        WriteJson("deployment.json", new
        {
            SchemaVersion = 1,
            ReleaseId = deploymentReleaseMismatch
                ? "release-different"
                : "release-20260723",
            ExecutedAt = "2026-07-29T00:00:03Z",
            Mode = "deployed",
            Target = "Https",
            Destination = "https://registry.example.test/releases/",
            Files = deploymentFiles,
        });
        WriteMarketplaceVerification(
            "deployed-verification.json",
            "2026-07-29T00:00:04Z",
            "2026-07-29T00:00:00Z",
            "deployed.plugin");
        WriteMarketplaceVerification(
            "rollback-verification.json",
            "2026-07-29T00:00:05Z",
            rollbackMismatch
                ? "2026-07-27T00:00:00Z"
                : "2026-07-28T00:00:00Z",
            "baseline.plugin");
        return Path.Combine(_root, "deployment.json");
    }

    private void WriteMarketplaceVerification(
        string fileName,
        string verifiedAt,
        string registryGeneratedAt,
        string pluginId)
    {
        WriteJson(fileName, new
        {
            SchemaVersion = 1,
            VerifiedAt = verifiedAt,
            RegistryUri = "https://registry.example.test/releases/registry.json",
            RegistryGeneratedAt = registryGeneratedAt,
            EntryCount = 1,
            PackageCount = 1,
            TotalPackageBytes = 128,
            TrustedPublisherKeyCount = 1,
            DurationMilliseconds = 25,
            Packages = new[]
            {
                new
                {
                    PluginId = pluginId,
                    Version = "1.0.0",
                    Sha256 = new string('b', 64),
                    PublisherKeyId = "publisher",
                    Bytes = 128,
                },
            },
        });
    }

    private object EvidenceEntry(string fileName)
    {
        var path = Path.Combine(_root, fileName);
        return new
        {
            file = fileName,
            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                .ToLowerInvariant(),
        };
    }

    private void WritePortableSource(string relativePath, object value)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value));
    }

    private string WriteJson(string fileName, object value)
    {
        var path = Path.Combine(_root, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(value));
        return path;
    }

    private async Task<ProcessResult> RunVerifierAsync(
        FixturePaths paths,
        string? output,
        bool preflightOnly = false)
    {
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "-NoProfile", "-ExecutionPolicy", "Bypass",
            "-File", Path.Combine(FindRepositoryRoot(), "verify-external-release-gate.ps1"),
            "-ReleaseManifestPath", paths.Release,
            "-DownloadGatePath", paths.Download,
            "-CleanEnvironmentGatePath", paths.Clean,
            "-PhysicalDpiGatePath", paths.Dpi,
            "-AccessibilityGatePath", paths.Accessibility,
            "-MarketplaceRehearsalPath", paths.Marketplace,
            "-ExpectedSourceCommit", Commit,
            "-ExpectedDistributionChannel", "unsigned",
        })
        {
            start.ArgumentList.Add(argument);
        }
        if (preflightOnly)
            start.ArgumentList.Add("-PreflightOnly");
        if (output is not null)
        {
            start.ArgumentList.Add("-OutputPath");
            start.ArgumentList.Add(output);
        }

        using var process = Process.Start(start)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed record FixturePaths(
        string Release,
        string Download,
        string Clean,
        string Dpi,
        string Accessibility,
        string Marketplace,
        string MarketplaceDeploymentEvidence,
        string Package,
        string FrameworkPackage,
        string Installer,
        string Checksums,
        string DownloadEvidence,
        string CleanEvidence,
        string DpiSource,
        string AccessibilitySource);

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
