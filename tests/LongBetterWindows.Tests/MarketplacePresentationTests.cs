using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class MarketplacePresentationTests
{
    [Fact]
    public void ProjectEntries_FiltersAndProjectsInstalledState()
    {
        var catalog = new MarketplaceCatalog
        {
            Entries =
            [
                Entry("dev.long.clipboard", "Clipboard Studio", "Productivity", "2.0.0"),
                Entry("dev.long.color", "Color Lab", "Design", "1.0.0"),
            ],
        };

        var cards = MarketplacePresentation.ProjectEntries(
            catalog,
            "clipboard",
            "Productivity",
            pluginId => pluginId == "dev.long.clipboard" ? "1.0.0" : null);

        var card = Assert.Single(cards);
        Assert.Equal("dev.long.clipboard", card.Entry.Id);
        Assert.Equal(MarketplaceInstallState.UpdateAvailable, card.State);
    }

    [Fact]
    public void ProjectEntries_KeepsMachineCategoryForFilteringAndLocalizesDisplay()
    {
        var catalog = new MarketplaceCatalog
        {
            Entries = [Entry("dev.long.color", "Color Lab", "design", "1.0.0")],
        };

        var cards = MarketplacePresentation.ProjectEntries(
            catalog,
            query: null,
            category: "design",
            _ => null,
            category => category == "design" ? "设计工具" : category);

        var card = Assert.Single(cards);
        Assert.Equal("design", card.Entry.Category);
        Assert.Equal("设计工具", card.CategoryLabel);
        Assert.Equal("设计工具 · Long", card.Meta);
        Assert.Equal("market.category.design", MarketplacePresentation.GetCategoryResourceKey("design"));
        Assert.Equal("custom", MarketplacePresentation.LocalizeCategory("custom", _ => "unused"));
    }

    [Fact]
    public void Compatibility_RejectsNewerHostAndFormatsRequirements()
    {
        var compatibility = MarketplacePresentation.GetCompatibility(
            new MarketplacePackageVersion
            {
                Version = "2.0.0",
                MinHostVersion = "9.0.0",
            },
            "1.9.0");

        Assert.False(compatibility.IsCompatible);
        Assert.Contains("Host >= 9.0.0", compatibility.Requirements);
    }

    [Fact]
    public void CreatePackageMetadata_NormalizesEmptyHashAndCopiesTrustIdentity()
    {
        var entry = Entry("dev.long.sample", "Sample", "Tools", "1.0.0");
        var version = new MarketplacePackageVersion
        {
            Version = "1.0.0",
            Sha256 = " ",
            PublisherKeyId = "publisher-key",
            Signature = "signature",
        };

        var metadata = MarketplacePresentation.CreatePackageMetadata(entry, version);

        Assert.Equal(entry.Id, metadata.ExpectedPluginId);
        Assert.Equal(entry.Source, metadata.Source);
        Assert.Null(metadata.ExpectedSha256);
        Assert.Equal("publisher-key", metadata.PublisherKeyId);
        Assert.Equal("signature", metadata.Signature);
    }

    private static MarketplaceEntry Entry(
        string id,
        string name,
        string category,
        string version)
        => new()
        {
            Id = id,
            Name = name,
            Summary = name,
            Publisher = "Long",
            Category = category,
            Tags = [name],
            Versions = [new MarketplacePackageVersion { Version = version }],
        };
}
