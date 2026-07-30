using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public sealed class ReferenceWidgetTests
{
    private static readonly string ReferenceRoot = Path.Combine(
        FindRepositoryRoot(),
        "samples",
        "LongWidgetReference");

    [Fact]
    public async Task ReferenceDirectory_PassesProductionValidation()
    {
        var result = await new PluginPackageValidator()
            .ValidateDirectoryAsync(ReferenceRoot);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("com.long.reference-widgets", result.Manifest!.Id);
        Assert.Equal("1.1.0", result.Manifest.Version);
        Assert.Equal(2, result.Manifest.Widgets.Count);
        Assert.Contains(
            result.Manifest.Widgets,
            widget => widget.Id == "focus-pulse"
                && !widget.MultipleInstances);
        Assert.Contains(
            result.Manifest.Widgets,
            widget => widget.Id == "tiny-counter"
                && widget.MultipleInstances);
    }

    [Fact]
    public void ReferenceWebAssets_StayLocalAndCspCompatible()
    {
        var htmlFiles = Directory.GetFiles(
            ReferenceRoot,
            "*.html",
            SearchOption.AllDirectories);
        Assert.Equal(3, htmlFiles.Length);
        foreach (var path in htmlFiles)
        {
            var html = File.ReadAllText(path);
            Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("script type=\"module\" src=", html);
        }

        var controller = File.ReadAllText(Path.Combine(
            ReferenceRoot,
            "widgets",
            "shared",
            "widget-controller.js"));
        Assert.Contains("host.getInfo", controller);
        Assert.Contains("widget.getInstanceState", controller);
        Assert.Contains("widget.setInstanceState", controller);
        Assert.Contains("widget.ready", controller);
        Assert.Contains("trusted widget context", controller);
    }

    [Fact]
    public void ReferenceLocalization_UsesSymmetricResourceKeys()
    {
        var chinese = ReadKeys("zh-CN");
        var english = ReadKeys("en-US");

        Assert.Equal(chinese, english);
        Assert.Contains("checklist.package", chinese);
    }

    [Fact]
    public void ReferencePackage_HasCrossCheckoutLineEndingAndHashContract()
    {
        var root = FindRepositoryRoot();
        var attributes = File.ReadAllText(Path.Combine(root, ".gitattributes"));
        var baseline = File.ReadAllText(Path.Combine(
            root,
            "samples",
            "LongWidgetReference.package.sha256"));
        var buildScript = File.ReadAllText(Path.Combine(
            root,
            "build-reference-widget.ps1"));

        Assert.Contains(
            "samples/LongWidgetReference/** text eol=lf",
            attributes);
        Assert.Matches(
            "^[A-F0-9]{64} \\*com-long-reference-widgets-v1\\.1\\.0\\.lpak\\s*$",
            baseline);
        Assert.Contains("确定性哈希不匹配", buildScript);
        Assert.Contains("Get-FileHash", buildScript);
    }

    private static string[] ReadKeys(string language)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            ReferenceRoot,
            "i18n",
            $"{language}.json")));
        return document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "LongBetterWindows.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate repository root.");
    }
}
