using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class WindowsSettingsSearchProviderTests
{
    [Theory]
    [InlineData("显示", "ms-settings:display")]
    [InlineData("wifi", "ms-settings:network-wifi")]
    [InlineData("卸载", "ms-settings:appsfeatures")]
    [InlineData("高对比度", "ms-settings:easeofaccess")]
    public async Task SearchAsync_MatchesChineseAndEnglishKeywords(
        string query,
        string expectedUri)
    {
        var provider = new WindowsSettingsSearchProvider();

        var results = await provider.SearchAsync(new SearchRequest(
            query, ContextSnapshot.Empty, MaxResults: 6));

        var result = Assert.Single(results, item =>
            string.Equals(item.PrimaryAction.Target, expectedUri,
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal(SearchActionKind.OpenUri, result.PrimaryAction.Kind);
        Assert.Equal(SearchActionKind.CopyText, Assert.Single(result.SecondaryActions).Kind);
        Assert.Equal("Windows 设置", result.Source);
    }

    [Fact]
    public async Task SearchAsync_EmptyQueryDoesNotCrowdRecommendations()
    {
        var provider = new WindowsSettingsSearchProvider();

        var results = await provider.SearchAsync(new SearchRequest(
            string.Empty, ContextSnapshot.Empty, MaxResults: 6));

        Assert.Empty(results);
    }
}
