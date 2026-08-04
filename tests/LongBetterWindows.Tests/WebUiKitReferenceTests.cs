using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public sealed class WebUiKitReferenceTests
{
    private static readonly string ReferenceRoot = Path.Combine(
        FindRepositoryRoot(),
        "samples",
        "LongWebUiKitPreview");

    [Fact]
    public async Task ReferenceDirectory_PassesProductionValidation()
    {
        var result = await new PluginPackageValidator()
            .ValidateDirectoryAsync(ReferenceRoot);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("com.long.reference-web-ui-kit", result.Manifest!.Id);
        Assert.Equal(PluginUiKitVersion.Current, result.Manifest.Version);
        Assert.Equal(
            PluginUiKitVersion.Current,
            result.Manifest.MinUiKitVersion);
        Assert.Empty(result.Manifest.Capabilities);
        Assert.Empty(result.Manifest.Commands);
    }

    [Fact]
    public void Reference_UsesOnlyInjectedUiKitAssets()
    {
        var html = File.ReadAllText(Path.Combine(ReferenceRoot, "index.html"));
        var script = File.ReadAllText(Path.Combine(ReferenceRoot, "main.js"));

        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stylesheet", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("script type=\"module\" src=\"main.js\"", html);
        Assert.Contains("window.LongUI", script);
        Assert.Contains("ui?.version", script);
        Assert.Contains("ui?.renderState", script);
        Assert.Contains("ui?.announce", script);
        Assert.Contains("ui?.showToast", script);
        Assert.Contains("ui?.confirm", script);
        Assert.Contains("ui?.onLanguageChanged", script);
        Assert.Contains("ui?.onViewportChanged", script);
        Assert.Contains("ui?.language?.resolvedLanguage", script);
        Assert.Contains("ui?.viewport", script);
    }

    [Fact]
    public void Reference_CoversPublicComponentsAndAccessibilityStates()
    {
        var html = File.ReadAllText(Path.Combine(ReferenceRoot, "index.html"));
        var requiredClasses = new[]
        {
            "long-button",
            "long-input",
            "long-badge",
            "long-progress",
            "long-list",
        };

        Assert.All(requiredClasses, name => Assert.Contains(name, html));
        Assert.Contains("role=\"progressbar\"", html);
        Assert.Contains("aria-valuenow=\"64\"", html);
        Assert.Contains("id=\"toastButton\"", html);
        Assert.Contains("id=\"dialogButton\"", html);
        Assert.DoesNotContain("<dialog", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-state=\"empty\"", html);
        Assert.Contains("data-state=\"loading\"", html);
        Assert.Contains("data-state=\"error\"", html);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "LongBetterWindows.sln")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException(
            "Repository root was not found.");
    }
}
