using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.PluginIpc.Client;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.Tests;

public sealed class PluginWorkerVerifiedLaunchDescriptorTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        $"long-worker-descriptor-{Guid.NewGuid():N}");

    public PluginWorkerVerifiedLaunchDescriptorTests()
        => Directory.CreateDirectory(_tempRoot);

    [Fact]
    public async Task Candidate_UsesSealedFileEvidenceFromValidatedPackage()
    {
        var (validation, installedRoot) = await CreateInstalledPackageAsync();

        var descriptor = PluginWorkerVerifiedLaunchDescriptor.CreateCandidate(
            validation,
            installedRoot);

        Assert.Equal("reference.headless.native", descriptor.PluginId);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
                Path.Combine(installedRoot, "long-plugin-worker-reference.dll")))),
            descriptor.LaunchPolicy.ExpectedSha256);
        Assert.Empty(descriptor.LaunchPolicy.AllowedHostMethods);
    }

    [Fact]
    public async Task Candidate_LaunchesReferenceWorkloadThroughVerifiedPolicy()
    {
        var (validation, installedRoot) = await CreateInstalledPackageAsync();
        var descriptor = PluginWorkerVerifiedLaunchDescriptor.CreateCandidate(
            validation,
            installedRoot);

        await using var session = await ExperimentalPluginWorkerSession.StartAsync(
            WorkerPath(),
            descriptor.PluginId,
            workloadPolicy: descriptor.LaunchPolicy);
        Assert.Equal("initialized", (await session.InvokeLifecycleAsync(
            PluginWorkerLifecycleOperation.Initialize)).State);
        Assert.Equal("running", (await session.InvokeLifecycleAsync(
            PluginWorkerLifecycleOperation.Start)).State);
        Assert.Equal(
            "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824",
            (await session.InvokeCommandAsync(
                new PluginWorkerCommandRequest("reference.sha256", "hello"))).Text);
    }

    [Fact]
    public async Task Candidate_RejectsInstalledFileTampering()
    {
        var (validation, installedRoot) = await CreateInstalledPackageAsync();
        await File.AppendAllTextAsync(
            Path.Combine(installedRoot, "long-plugin-worker-reference.dll"),
            "tampered");

        var error = Assert.Throws<InvalidDataException>(() =>
            PluginWorkerVerifiedLaunchDescriptor.CreateCandidate(
                validation,
                installedRoot));

        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Candidate_RejectsPubliclyConstructedValidationResult()
    {
        var (validation, installedRoot) = await CreateInstalledPackageAsync();
        var forged = PackageValidationResult.Ok(
            validation.Manifest!,
            validation.Sha256,
            validation.TrustLevel,
            validation.PermissionDiff,
            validation.RequiresHighTrustWarning);

        var error = Assert.Throws<InvalidDataException>(() =>
            PluginWorkerVerifiedLaunchDescriptor.CreateCandidate(forged, installedRoot));

        Assert.Contains("sealed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Candidate_RejectsValidatedPackageWithoutFileLedger()
    {
        var (validation, installedRoot) = await CreateInstalledPackageAsync(
            includeFileLedger: false);
        Assert.True(validation.IsSuccess, validation.Error);

        Assert.Throws<InvalidDataException>(() =>
            PluginWorkerVerifiedLaunchDescriptor.CreateCandidate(
                validation,
                installedRoot));
    }

    [Fact]
    public async Task ProductionReleaseGate_RemainsClosedForValidCandidate()
    {
        var (validation, installedRoot) = await CreateInstalledPackageAsync();
        var descriptor = PluginWorkerVerifiedLaunchDescriptor.CreateCandidate(
            validation,
            installedRoot);

        Assert.False(PluginWorkerProductionReleaseGate.ProductionEnabled);
        var error = Assert.Throws<InvalidOperationException>(() =>
            PluginWorkerProductionReleaseGate.Approve(descriptor));
        Assert.Contains("disabled", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(PackageValidationResult Validation, string InstalledRoot)>
        CreateInstalledPackageAsync(bool includeFileLedger = true)
    {
        var packagePath = Path.Combine(_tempRoot, $"{Guid.NewGuid():N}.lpak");
        var installedRoot = Path.Combine(_tempRoot, $"installed-{Guid.NewGuid():N}");
        var assemblyBytes = await File.ReadAllBytesAsync(ReferenceWorkloadPath());
        var manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            id = "reference.headless.native",
            version = "1.0.0",
            name = "Reference Headless Native",
            author = "Long",
            runtime = "native",
            entry_point = "long-plugin-worker-reference.dll",
            capabilities = Array.Empty<string>(),
        }));

        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "manifest.json", manifestBytes);
            WriteEntry(archive, "long-plugin-worker-reference.dll", assemblyBytes);
            if (includeFileLedger)
            {
                var ledgerBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                {
                    schema_version = 1,
                    classification = "long_plugin_file_manifest",
                    plugin_id = "reference.headless.native",
                    version = "1.0.0",
                    files = new object[]
                    {
                        FileLedgerEntry("manifest.json", manifestBytes),
                        FileLedgerEntry("long-plugin-worker-reference.dll", assemblyBytes),
                    },
                }));
                WriteEntry(archive, "package-files.json", ledgerBytes);
            }
        }

        var validation = await new PluginPackageValidator().ValidateAsync(packagePath);
        Assert.True(validation.IsSuccess, validation.Error);
        Directory.CreateDirectory(installedRoot);
        using var source = ZipFile.OpenRead(packagePath);
        PluginPackageValidator.ExtractSafely(source, installedRoot);
        return (validation, installedRoot);
    }

    private static object FileLedgerEntry(string path, byte[] content)
        => new
        {
            path,
            size = content.LongLength,
            sha256 = Convert.ToHexString(SHA256.HashData(content)),
        };

    private static void WriteEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static string ReferenceWorkloadPath()
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory)
            .Parent?.Name ?? "Release";
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tools",
            "LongBetterWindows.PluginWorker.Reference",
            "bin",
            configuration,
            "net8.0-windows",
            "long-plugin-worker-reference.dll");
        Assert.True(File.Exists(path), $"Reference worker workload is missing: {path}");
        return path;
    }

    private static string WorkerPath()
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory)
            .Parent?.Name ?? "Release";
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tools",
            "LongBetterWindows.PluginWorker",
            "bin",
            configuration,
            "net8.0-windows",
            "long-plugin-worker.dll");
        Assert.True(File.Exists(path), $"Plugin worker is missing: {path}");
        return path;
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

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, true); } catch { }
    }
}
