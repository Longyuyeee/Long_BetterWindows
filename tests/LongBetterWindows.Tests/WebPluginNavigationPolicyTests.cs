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
        Assert.True(policy.IsTrustedLocalUri(
            $"https://{policy.VirtualHostName}/pages/tool.html"));
        Assert.False(policy.IsTrustedLocalUri(
            new Uri(_root + "-lookalike\\index.html").AbsoluteUri));
        Assert.False(policy.IsTrustedLocalUri(
            $"https://evil.{policy.VirtualHostName}/pages/tool.html"));
        Assert.False(policy.IsTrustedLocalUri("https://example.com/plugin"));
        Assert.False(policy.IsTrustedLocalUri("data:text/html,untrusted"));
        Assert.False(policy.IsTrustedLocalUri("about:blank"));
    }

    [Fact]
    public void IsTrustedWebViewUri_AllowsOnlyVirtualHttpsOrigin()
    {
        var policy = new WebPluginNavigationPolicy(_root);

        Assert.True(policy.IsTrustedWebViewUri(
            $"https://{policy.VirtualHostName}/pages/tool.html"));
        Assert.False(policy.IsTrustedWebViewUri(
            new Uri(Path.Combine(_root, "pages", "tool.html")).AbsoluteUri));
        Assert.False(policy.IsTrustedWebViewUri(
            $"http://{policy.VirtualHostName}/pages/tool.html"));
        Assert.False(policy.IsTrustedWebViewUri(
            $"https://evil.{policy.VirtualHostName}/pages/tool.html"));
    }

    [Fact]
    public void TryResolveEntryPoint_RejectsTraversalAndMissingFiles()
    {
        var policy = new WebPluginNavigationPolicy(_root);

        Assert.True(policy.TryResolveEntryPoint("index.html", out var entry));
        Assert.NotNull(entry);
        Assert.Equal(Uri.UriSchemeHttps, entry!.Scheme);
        Assert.Equal(policy.VirtualHostName, entry.Host);
        Assert.Equal("/", entry.AbsolutePath[..1]);
        Assert.False(policy.TryResolveEntryPoint("..\\outside.html", out _));
        Assert.False(policy.TryResolveEntryPoint("missing.html", out _));
    }

    [Fact]
    public void ContentSecurityPolicy_UsesSelfOnlySandboxDefaults()
    {
        var policy = new WebPluginNavigationPolicy(_root);
        var script = policy.BuildContentSecurityPolicyInjectionScript();

        Assert.Contains("Content-Security-Policy", script);
        Assert.Contains("document.createElement('meta')", script);
        Assert.Contains("default-src 'self'", WebPluginNavigationPolicy.DefaultContentSecurityPolicy);
        Assert.Contains("connect-src 'none'", WebPluginNavigationPolicy.DefaultContentSecurityPolicy);
        Assert.Contains("frame-src 'none'", WebPluginNavigationPolicy.DefaultContentSecurityPolicy);
        Assert.Contains("form-action 'none'", WebPluginNavigationPolicy.DefaultContentSecurityPolicy);
    }

    [Fact]
    public void TryResolveVirtualUriToLocalPath_AllowsOnlyExistingFilesInsidePluginDirectory()
    {
        var policy = new WebPluginNavigationPolicy(_root);

        Assert.True(policy.TryResolveVirtualUriToLocalPath(
            $"https://{policy.VirtualHostName}/index.html",
            out var localPath));
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "index.html")), localPath);
        Assert.False(policy.TryResolveVirtualUriToLocalPath(
            $"https://{policy.VirtualHostName}/../outside.html",
            out _));
        Assert.False(policy.TryResolveVirtualUriToLocalPath(
            $"https://{policy.VirtualHostName}/missing.html",
            out _));
        Assert.False(policy.TryResolveVirtualUriToLocalPath(
            "https://example.com/index.html",
            out _));
    }

    [Fact]
    public void WebResourceSandbox_BlocksAnythingOutsideVirtualOrigin()
    {
        var policy = new WebPluginNavigationPolicy(_root);

        Assert.False(policy.ShouldBlockWebResourceRequest(
            $"https://{policy.VirtualHostName}/index.html"));
        Assert.True(policy.ShouldBlockWebResourceRequest(
            $"https://evil.{policy.VirtualHostName}/index.html"));
        Assert.True(policy.ShouldBlockWebResourceRequest("https://example.com/script.js"));
        Assert.True(policy.ShouldBlockWebResourceRequest("file:///C:/Windows/win.ini"));
        Assert.True(policy.ShouldBlockWebResourceRequest("about:blank"));
    }

    [Fact]
    public void ContentSecurityPolicy_ResponseHeaderUsesCanonicalPolicy()
    {
        var policy = new WebPluginNavigationPolicy(_root);

        Assert.Equal(
            "Content-Security-Policy: " + WebPluginNavigationPolicy.DefaultContentSecurityPolicy,
            policy.BuildContentSecurityPolicyResponseHeader());
        Assert.True(WebPluginNavigationPolicy.IsHtmlDocumentPath("index.html"));
        Assert.True(WebPluginNavigationPolicy.IsHtmlDocumentPath("INDEX.HTM"));
        Assert.False(WebPluginNavigationPolicy.IsHtmlDocumentPath("style.css"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }
}
