using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public sealed class PluginManifestSchemaTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string SchemaPath = Path.Combine(
        RepositoryRoot,
        "schemas",
        "plugin-manifest.schema.json");

    [Fact]
    public void Schema_TracksManifestAndNestedContractProperties()
    {
        using var document = ReadSchema();
        var root = document.RootElement;

        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            root.GetProperty("$schema").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        AssertPropertySet<PluginManifest>(root.GetProperty("properties"));

        var definitions = root.GetProperty("$defs");
        AssertPropertySet<PluginDependency>(
            definitions.GetProperty("dependency").GetProperty("properties"));
        AssertPropertySet<PluginCommand>(
            definitions.GetProperty("command").GetProperty("properties"));
        AssertPropertySet<PluginCommandOutputDeclaration>(
            definitions.GetProperty("output").GetProperty("properties"));
        AssertPropertySet<PluginCommandArgumentPreset>(
            definitions.GetProperty("argumentPreset").GetProperty("properties"));
        AssertPropertySet<PluginCommandArgumentDeclaration>(
            definitions.GetProperty("argument").GetProperty("properties"));
        AssertPropertySet<PluginWindowPreference>(
            definitions.GetProperty("window").GetProperty("properties"));
        AssertPropertySet<PluginLifecyclePreference>(
            definitions.GetProperty("lifecycle").GetProperty("properties"));
        AssertPropertySet<PluginLocalizationPreference>(
            definitions.GetProperty("localization").GetProperty("properties"));
        AssertPropertySet<PluginBackgroundPreference>(
            definitions.GetProperty("background").GetProperty("properties"));

        Assert.Equal(
            new[] { "entry_point", "id", "name", "version" },
            ReadStrings(root.GetProperty("required")).Order().ToArray());
    }

    [Fact]
    public void Schema_TracksCapabilitiesRuntimesAndSerializedEnums()
    {
        using var document = ReadSchema();
        var root = document.RootElement;
        var properties = root.GetProperty("properties");
        var definitions = root.GetProperty("$defs");

        Assert.Equal(
            ManifestReader.KnownCapabilities.Order(StringComparer.Ordinal),
            ReadStrings(
                    properties
                        .GetProperty("capabilities")
                        .GetProperty("items")
                        .GetProperty("enum"))
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            new[] { "csharp-script", "native", "webview" },
            ReadStrings(properties.GetProperty("runtime").GetProperty("enum"))
                .Order(StringComparer.Ordinal));

        AssertSchemaEnum<AcceptedInputType>(
            definitions
                .GetProperty("command")
                .GetProperty("properties")
                .GetProperty("accepted_inputs")
                .GetProperty("items")
                .GetProperty("enum"));
        AssertSchemaEnum<PluginViewMode>(
            definitions
                .GetProperty("command")
                .GetProperty("properties")
                .GetProperty("view_mode")
                .GetProperty("enum"));
        AssertSchemaEnum<PluginCommandOutputType>(
            definitions
                .GetProperty("output")
                .GetProperty("properties")
                .GetProperty("type")
                .GetProperty("enum"));
        AssertSchemaEnum<PluginCommandArgumentType>(
            definitions
                .GetProperty("argument")
                .GetProperty("properties")
                .GetProperty("type")
                .GetProperty("enum"));
        AssertSchemaEnum<PluginWindowMode>(
            definitions
                .GetProperty("window")
                .GetProperty("properties")
                .GetProperty("mode")
                .GetProperty("enum"));
        AssertSchemaEnum<PluginCloseBehavior>(
            definitions
                .GetProperty("lifecycle")
                .GetProperty("properties")
                .GetProperty("close_behavior")
                .GetProperty("enum"));
        AssertSchemaEnum<PluginPresentationMode>(
            definitions
                .GetProperty("lifecycle")
                .GetProperty("properties")
                .GetProperty("default_presentation")
                .GetProperty("enum"));
    }

    [Fact]
    public async Task BuiltInAndTemplateManifests_UseDeclaredSchemaFieldsAndRuntimeContract()
    {
        using var schema = ReadSchema();
        var declaredProperties = schema.RootElement
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var manifests = Directory
            .EnumerateDirectories(Path.Combine(RepositoryRoot, "src"))
            .Select(directory => Path.Combine(directory, "manifest.json"))
            .Where(File.Exists)
            .Concat(Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "Templates"),
                "manifest.json",
                SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(29, manifests.Length);
        foreach (var manifestPath in manifests)
        {
            using var manifestDocument = JsonDocument.Parse(
                File.ReadAllText(manifestPath));
            var unknownProperties = manifestDocument.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .Where(property => !declaredProperties.Contains(property))
                .ToArray();
            Assert.True(
                unknownProperties.Length == 0,
                $"{Path.GetRelativePath(RepositoryRoot, manifestPath)} has unknown fields: "
                + string.Join(", ", unknownProperties));

            var result = await ManifestReader.ReadAsync(
                Path.GetDirectoryName(manifestPath)!);
            Assert.True(
                result.IsSuccess,
                $"{Path.GetRelativePath(RepositoryRoot, manifestPath)}: {result.Error}");
        }
    }

    [Fact]
    public void CanonicalDocumentation_LinksTheMachineReadableSchema()
    {
        var guide = File.ReadAllText(
            Path.Combine(RepositoryRoot, "docs", "插件开发指南.md"));
        var readme = File.ReadAllText(
            Path.Combine(RepositoryRoot, "README.md"));

        Assert.Contains(
            "../schemas/plugin-manifest.schema.json",
            guide);
        Assert.Contains(
            "schemas/plugin-manifest.schema.json",
            readme);
        Assert.Contains("32 项能力", guide);
        Assert.DoesNotContain("完整 14 项", guide);
    }

    private static JsonDocument ReadSchema()
        => JsonDocument.Parse(File.ReadAllText(SchemaPath));

    private static void AssertPropertySet<T>(JsonElement schemaProperties)
    {
        var expected = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actual = schemaProperties
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, actual);
    }

    private static void AssertSchemaEnum<T>(JsonElement schemaEnum)
        where T : struct, Enum
    {
        var expected = Enum.GetNames<T>()
            .Select(JsonNamingPolicy.SnakeCaseLower.ConvertName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actual = ReadStrings(schemaEnum)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, actual);
    }

    private static string[] ReadStrings(JsonElement array)
        => array
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
