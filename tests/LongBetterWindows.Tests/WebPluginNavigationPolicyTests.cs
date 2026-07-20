using System.IO;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public sealed class WebPluginNavigationPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"long-web-policy-{Guid.NewGuid():N}");

    public WebPluginNavigationPolicyTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "pages"));
        File.WriteAllText(Path.Combine(_root, "index.html"), "local");
    }

    [Fact]
    public void IsTrustedLocalUri_AllowsOnlyFilesInsidePluginDirectory()
    {
        var policy = new WebPluginNavigationPolicy(_root);

        Assert.True(policy.IsTrustedLocalUri(
            new Uri(Path.Combine(_root, "pages", "tool.html")).AbsoluteUri));
        Assert.False(policy.IsTrustedLocalUri(
            new Uri(_root + "-lookalike\\index.html").AbsoluteUri));
        Assert.False(policy.IsTrustedLocalUri("https://example.com/plugin"));
        Assert.False(policy.IsTrustedLocalUri("data:text/html,untrusted"));
        Assert.False(policy.IsTrustedLocalUri("about:blank"));
    }

    [Fact]
    public void TryResolveEntryPoint_RejectsTraversalAndMissingFiles()
    {
        var policy = new WebPluginNavigationPolicy(_root);

        Assert.True(policy.TryResolveEntryPoint("index.html", out var entry));
        Assert.NotNull(entry);
        Assert.False(policy.TryResolveEntryPoint("..\\outside.html", out _));
        Assert.False(policy.TryResolveEntryPoint("missing.html", out _));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }
}
