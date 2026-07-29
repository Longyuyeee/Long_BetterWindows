using System.IO;
using System.Net.Http;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class MarketplaceErrorContractTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "long-market-error-tests-" + Guid.NewGuid().ToString("N"));

    public MarketplaceErrorContractTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void MarketplaceErrorCodes_HaveStablePublishedValues()
    {
        Assert.Equal(0, (int)MarketplaceErrorCode.None);
        Assert.Equal(4000, (int)MarketplaceErrorCode.CatalogNotFound);
        Assert.Equal(4001, (int)MarketplaceErrorCode.CatalogUnsupported);
        Assert.Equal(4002, (int)MarketplaceErrorCode.CatalogTooLarge);
        Assert.Equal(4003, (int)MarketplaceErrorCode.CatalogDuplicatePlugin);
        Assert.Equal(4004, (int)MarketplaceErrorCode.CatalogInvalidEntry);
        Assert.Equal(4005, (int)MarketplaceErrorCode.CatalogUnreadable);
        Assert.Equal(4006, (int)MarketplaceErrorCode.CatalogNetworkUnavailable);
        Assert.Equal(4007, (int)MarketplaceErrorCode.CatalogInsecureRedirect);
        Assert.Equal(4008, (int)MarketplaceErrorCode.CatalogAllSourcesUnavailable);
        Assert.Equal(4100, (int)MarketplaceErrorCode.DownloadNotConfigured);
        Assert.Equal(4101, (int)MarketplaceErrorCode.DownloadUriNotAllowed);
        Assert.Equal(4102, (int)MarketplaceErrorCode.DownloadHashMissing);
        Assert.Equal(4103, (int)MarketplaceErrorCode.DownloadCacheFailure);
        Assert.Equal(4104, (int)MarketplaceErrorCode.DownloadRedirectNotAllowed);
        Assert.Equal(4105, (int)MarketplaceErrorCode.DownloadTooLarge);
        Assert.Equal(4106, (int)MarketplaceErrorCode.DownloadHashMismatch);
        Assert.Equal(4107, (int)MarketplaceErrorCode.DownloadCanceled);
        Assert.Equal(4108, (int)MarketplaceErrorCode.DownloadTimeout);
        Assert.Equal(4109, (int)MarketplaceErrorCode.DownloadFailed);
        Assert.Equal(4200, (int)MarketplaceErrorCode.PackageRejected);
        Assert.Equal(4201, (int)MarketplaceErrorCode.OperationBusy);
        Assert.Equal(4202, (int)MarketplaceErrorCode.OperationCanceled);

        Assert.Equal(
            MarketplaceErrorCode.None,
            MarketplaceCatalogResult.Ok(new MarketplaceCatalog()).ErrorCode);
        Assert.Equal(
            MarketplaceErrorCode.None,
            PackageDownloadResult.Ok("fixture.lpak", false, 0).ErrorCode);
    }

    [Fact]
    public async Task CatalogFailures_ReturnStableCodes()
    {
        var missing = await new LocalMarketplaceRepository(
            Path.Combine(_root, "missing.json")).LoadAsync();

        var duplicatePath = Path.Combine(_root, "duplicate.json");
        await File.WriteAllTextAsync(
            duplicatePath,
            JsonSerializer.Serialize(new MarketplaceCatalog
            {
                Entries =
                [
                    Entry("same.id", "First"),
                    Entry("same.id", "Second"),
                ],
            }));
        var duplicate = await new LocalMarketplaceRepository(duplicatePath).LoadAsync();

        Assert.Equal(MarketplaceErrorCode.CatalogNotFound, missing.ErrorCode);
        Assert.Equal(MarketplaceErrorCode.CatalogDuplicatePlugin, duplicate.ErrorCode);
    }

    [Fact]
    public async Task DownloadInputFailures_ReturnStableCodes()
    {
        using var client = new HttpClient();
        var downloader = new MarketplacePackageDownloader(
            client,
            Path.Combine(_root, "cache"),
            ["packages.example"]);

        var rejectedSource = await downloader.DownloadAsync(
            "dev.long.source",
            new MarketplacePackageVersion
            {
                Version = "1.0.0",
                PackageUri = new Uri("https://untrusted.example/plugin.lpak"),
                Sha256 = new string('A', 64),
            });
        var missingHash = await downloader.DownloadAsync(
            "dev.long.hash",
            new MarketplacePackageVersion
            {
                Version = "1.0.0",
                PackageUri = new Uri("https://packages.example/plugin.lpak"),
                Sha256 = "invalid",
            });

        Assert.Equal(MarketplaceErrorCode.DownloadUriNotAllowed, rejectedSource.ErrorCode);
        Assert.Equal(MarketplaceErrorCode.DownloadHashMissing, missingHash.ErrorCode);
    }

    [Fact]
    public void MarketplaceFailures_HaveBilingualPresentationKeys()
    {
        var repository = FindRepositoryRoot();
        using var chinese = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repository,
            "src",
            "LongBetterWindows.Host",
            "i18n",
            "zh-CN.json")));
        using var english = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repository,
            "src",
            "LongBetterWindows.Host",
            "i18n",
            "en-US.json")));

        foreach (var code in Enum.GetValues<MarketplaceErrorCode>()
                     .Where(code => code != MarketplaceErrorCode.None))
        {
            var key = MarketplacePresentation.GetErrorResourceKey(code);
            Assert.NotEqual("market.error.unknown", key);
            Assert.True(chinese.RootElement.TryGetProperty(key, out _), key);
            Assert.True(english.RootElement.TryGetProperty(key, out _), key);
            Assert.False(string.IsNullOrWhiteSpace(
                MarketplacePresentation.GetErrorAutomationStatus(code)));
        }

        var view = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "MarketplaceControl.xaml.cs"));
        Assert.DoesNotContain("CatalogStatusText.Text = result.Error", view);
        Assert.Contains(
            "MarketplaceCatalogViewStatePresenter.FromLoad(result)",
            view);
        Assert.DoesNotContain("DetailHint.Text = preparation.Error", view);
        Assert.DoesNotContain("error.Contains(", view);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    private static MarketplaceEntry Entry(string id, string name)
        => new()
        {
            Id = id,
            Name = name,
            Summary = name,
            Publisher = "Long",
            Category = "Quality",
            Versions = [new MarketplacePackageVersion { Version = "1.0.0" }],
        };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
