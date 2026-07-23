using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LongBetterWindows.Tests;

public sealed class ExternalReleaseGateTests : IDisposable
{
    private const string Commit = "1111111111111111111111111111111111111111";
    private const string PackageHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
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

        Assert.Equal(0, result.ExitCode);
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
            "registry.example.test",
            root.GetProperty("marketplace").GetProperty("destination_host").GetString());
        Assert.All(
            root.GetProperty("inputs").EnumerateObject(),
            input => Assert.Matches("^[0-9a-f]{64}$", input.Value.GetString()));
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

    private FixturePaths WriteFixture(
        string cleanPackageHash,
        bool marketplacePreflightOnly = false)
    {
        Directory.CreateDirectory(_root);
        var release = WriteJson("release.json", new
        {
            commit = Commit,
            distribution_channel = "unsigned",
            release_eligible = true,
            signed = false,
            packages = new[] { new { file = "LongBetterWindows.zip", sha256 = PackageHash } },
        });
        var download = WriteJson("download.json", new
        {
            classification = "approved_release_download_gate",
            passed = true,
            source_commit = Commit,
            distribution_channel = "unsigned",
            package_file = "LongBetterWindows.zip",
            package_sha256 = PackageHash,
            @operator = "capture-user",
            reviewer = "review-user",
        });
        var clean = WriteJson("clean.json", new
        {
            classification = "approved_clean_windows_release_gate",
            passed = true,
            source_commit = Commit,
            distribution_channel = "unsigned",
            package_sha256 = cleanPackageHash,
            reviewer = "clean-reviewer",
        });
        var dpi = WriteJson("dpi.json", new
        {
            classification = "approved_physical_device_dpi_matrix",
            passed = true,
            source_commit = Commit,
        });
        var accessibility = WriteJson("accessibility.json", new
        {
            classification = "approved_physical_accessibility_matrix",
            passed = true,
            source_commit = Commit,
        });
        var marketplace = WriteJson("marketplace.json", new
        {
            classification = "marketplace_https_rehearsal",
            passed = true,
            destination = "https://registry.example.test/releases/",
            preflight_only = marketplacePreflightOnly,
            release_id = "release-20260723",
            preflight_dry_run_verified = true,
            baseline_verified = true,
            deployment_completed = true,
            deployment_verified = true,
            rollback_completed = true,
            rollback_verified = true,
            failure = (string?)null,
            rollback_failure = (string?)null,
            rollback_verification_failure = (string?)null,
        });
        return new FixturePaths(release, download, clean, dpi, accessibility, marketplace);
    }

    private string WriteJson(string fileName, object value)
    {
        var path = Path.Combine(_root, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(value));
        return path;
    }

    private async Task<ProcessResult> RunVerifierAsync(FixturePaths paths, string output)
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
            "-OutputPath", output,
        })
        {
            start.ArgumentList.Add(argument);
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
        string Marketplace);

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
