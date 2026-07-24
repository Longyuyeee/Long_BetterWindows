using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class BuiltInPluginLocalizationTests
{
    [Theory]
    [InlineData("Base64Tool")]
    [InlineData("ClipboardTool")]
    [InlineData("FolderNotePlugin")]
    [InlineData("PasswordGenerator")]
    [InlineData("QuickNotePlugin")]
    [InlineData("TimestampConverter")]
    public async Task SamplePlugin_HasValidBilingualResources(string plugin)
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, "src", plugin);
        var manifestResult = await ManifestReader.ReadAsync(directory);

        Assert.True(manifestResult.IsSuccess, manifestResult.Error);
        var localization = Assert.IsType<PluginLocalizationPreference>(
            manifestResult.Manifest!.Localization);
        Assert.Equal("zh-CN", localization.DefaultLanguage);

        using var chinese = ReadResource(directory, localization, "zh-CN");
        using var english = ReadResource(directory, localization, "en-US");
        var chineseKeys = chinese.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var englishKeys = english.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(chineseKeys, englishKeys);
        Assert.Contains("plugin.name", chineseKeys);
        foreach (var command in manifestResult.Manifest.Commands)
        {
            Assert.Contains($"commands.{command.Id}.title", chineseKeys);
            Assert.Contains($"commands.{command.Id}.description", chineseKeys);
        }
    }

    [Fact]
    public void Registry_LocalizesDisplayMetadataWithoutChangingCatalogIdentity()
    {
        var manifest = new PluginManifest
        {
            Id = "test.localized-metadata",
            Version = "1.0.0",
            Name = "原始插件",
            EntryPoint = "plugin.dll",
            Commands =
            [
                new PluginCommand
                {
                    Id = "run",
                    Title = "原始命令",
                    Description = "原始说明",
                    AcceptedInputs = [AcceptedInputType.None],
                },
            ],
        };
        var registry = new PluginRegistry();
        var changes = 0;
        registry.PluginsChanged += () => changes++;
        Assert.True(registry.Register(manifest, new object(), null, "."));
        var revision = registry.CatalogRevision;

        Assert.True(registry.ApplyLocalization(
            manifest.Id,
            new PluginLanguageContext(
                "en-US",
                "en-US",
                new Dictionary<string, string>
                {
                    ["plugin.name"] = "Localized plugin",
                    ["commands.run.title"] = "Localized command",
                    ["commands.run.description"] = "Localized description",
                })));

        var entry = Assert.IsType<PluginEntry>(registry.Get(manifest.Id));
        var descriptor = Assert.IsType<CommandDescriptor>(
            registry.Commands.Get(manifest.Id + ":run"));
        Assert.Equal("Localized plugin", entry.DisplayName);
        Assert.Equal("Localized plugin", descriptor.PluginName);
        Assert.Equal("Localized command", descriptor.Title);
        Assert.Equal("Localized description", descriptor.Description);
        Assert.Equal("原始命令", descriptor.Command.Title);
        Assert.Equal(revision, registry.CatalogRevision);
        Assert.Equal(2, changes);
        Assert.Single(registry.Commands.Search("localized command"));
    }

    [Fact]
    public void Base64Sample_RefreshesTextWithoutResettingUserState()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Base64Tool",
            "index.html"));

        Assert.Contains("long.language-changed", source);
        Assert.Contains("function applyLocalization(message)", source);
        Assert.Contains("renderStatus();", source);
        Assert.DoesNotContain("location.reload", source);
        Assert.DoesNotContain("input.value = ''", source);
        Assert.DoesNotContain("output.value = ''", source);
    }

    [Theory]
    [InlineData("ClipboardTool", "content.value = text", "let currentTab = 'history'")]
    [InlineData("QuickNotePlugin", "if (input.value.trim() === text) input.value = ''", "let notes = []")]
    public void StatefulWebPlugin_RefreshesLocalizationWithoutReloading(
        string plugin,
        string stateMutationMarker,
        string stateMarker)
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            plugin,
            "index.html"));

        Assert.Contains("long.language-changed", source);
        Assert.Contains("function applyLocalization(message)", source);
        Assert.Contains("renderStatus();", source);
        Assert.Contains(stateMarker, source);
        Assert.Contains(stateMutationMarker, source);
        Assert.DoesNotContain("location.reload", source);
        Assert.DoesNotContain("window.location", source);
    }

    [Theory]
    [InlineData("Base64Tool")]
    [InlineData("ClipboardTool")]
    [InlineData("PasswordGenerator")]
    [InlineData("QuickNotePlugin")]
    [InlineData("TimestampConverter")]
    public async Task LocalizedWebPlugin_DeclaresEveryReferencedResourceKey(
        string plugin)
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, "src", plugin);
        var source = File.ReadAllText(Path.Combine(directory, "index.html"));
        var manifestResult = await ManifestReader.ReadAsync(directory);
        var localization = Assert.IsType<PluginLocalizationPreference>(
            manifestResult.Manifest!.Localization);
        using var resource = ReadResource(
            directory,
            localization,
            localization.DefaultLanguage);
        var declaredKeys = resource.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        var referencedKeys = Regex.Matches(
                source,
                """(?:data-i18n="|\bt\s*\(\s*')([A-Za-z0-9._-]+)""")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(referencedKeys);
        Assert.Empty(referencedKeys.Except(declaredKeys, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("PasswordGenerator", "function secureIndex", "renderStrength();")]
    [InlineData("TimestampConverter", "function parse", "renderResult();")]
    public void LightweightWebPlugin_LocalizesProjectionWithoutRegeneratingValue(
        string plugin,
        string nextFunction,
        string projectionCall)
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            plugin,
            "index.html"));
        var start = source.IndexOf(
            "function applyLocalization(message)",
            StringComparison.Ordinal);
        var end = source.IndexOf(nextFunction, start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        var localizationBody = source[start..end];
        Assert.Contains(projectionCall, localizationBody);
        Assert.DoesNotContain("generate();", localizationBody);
        Assert.DoesNotContain("convert();", localizationBody);
        Assert.DoesNotContain("useNow();", localizationBody);
        Assert.DoesNotContain("location.reload", source);
    }

    [Fact]
    public void FolderNoteSample_UsesLanguageLifecycleAndPublishesResources()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FolderNotePlugin",
            "FolderNotePluginImpl.cs"));
        var project = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FolderNotePlugin",
            "FolderNotePlugin.csproj"));
        var hostProject = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "LongBetterWindows.Host.csproj"));

        Assert.Contains("IPluginLanguageLifecycle", source);
        Assert.Contains("window.ApplyLocalization", source);
        Assert.Contains("PluginLocalization Include=\"i18n\\*.json\"", project);
        Assert.Contains("FolderNotePlugin\\i18n", hostProject);
    }

    private static JsonDocument ReadResource(
        string directory,
        PluginLocalizationPreference localization,
        string language)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(
            directory,
            localization.Resources[language])));

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
}
