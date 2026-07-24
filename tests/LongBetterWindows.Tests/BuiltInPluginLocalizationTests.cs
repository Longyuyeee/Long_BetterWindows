using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class BuiltInPluginLocalizationTests
{
    [Theory]
    [InlineData("Base64Tool")]
    [InlineData("FolderNotePlugin")]
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
