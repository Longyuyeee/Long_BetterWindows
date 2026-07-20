using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public class MarketplaceRuntimeServiceTests
{
    [Fact]
    public async Task LocalOnlyRuntime_ReportsMissingCatalogWithoutEnablingDownloads()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using var runtime = CreateRuntime(root);

            var result = await runtime.LoadCatalogAsync();
            var download = await runtime.DownloadPackageAsync(
                "sample.plugin",
                new MarketplacePackageVersion { Version = "1.0.0" });

            Assert.False(result.IsSuccess);
            Assert.False(runtime.CanDownload);
            Assert.NotNull(runtime.TrustStore);
            Assert.False(download.IsSuccess);
            Assert.Equal("Remote marketplace download is not configured.", download.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DisposedRuntime_RejectsFurtherInitialization()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var runtime = CreateRuntime(root);
            runtime.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => runtime.InitializeAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MarketplaceRuntimeService CreateRuntime(string root)
        => new(
            Path.Combine(root, "registry.json"),
            MarketplaceSourceKind.LocalPackage,
            Path.Combine(root, "marketplace-settings.json"),
            Path.Combine(root, "trusted-publishers.json"),
            Path.Combine(root, "data"),
            "test");

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "LongBetterWindows.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
