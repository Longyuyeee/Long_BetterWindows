using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public sealed class PluginCatalogTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string CatalogPath = Path.Combine(
        RepositoryRoot, "catalog", "plugin-catalog.json");
    private static readonly string SchemaPath = Path.Combine(
        RepositoryRoot, "schemas", "plugin-catalog.schema.json");
    private static readonly string[] Classifications =
        ["built_in", "local_trusted", "marketplace", "reference", "sample"];
    private static readonly string[] Listings = ["local", "none", "remote"];
    private static readonly string[] Categories =
        ["automation", "design", "developer", "file", "productivity", "security", "system", "text"];

    [Fact]
    public void Schema_LocksCatalogShapeAndEnums()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        var root = document.RootElement;
        var entry = root.GetProperty("$defs").GetProperty("entry");
        var properties = entry.GetProperty("properties");

        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            root.GetProperty("$schema").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.False(entry.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            Classifications,
            ReadStrings(properties.GetProperty("classifications")
                .GetProperty("items").GetProperty("enum")).Order().ToArray());
        Assert.Equal(
            Listings,
            ReadStrings(properties.GetProperty("marketplace_listing")
                .GetProperty("enum")).Order().ToArray());
        Assert.Equal(
            Categories,
            ReadStrings(properties.GetProperty("category")
                .GetProperty("enum")).Order().ToArray());
    }

    [Fact]
    public async Task Catalog_CoversSourceAndReferenceManifestsWithoutTechnicalDuplication()
    {
        using var document = ReadCatalog();
        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        var paths = entries.Select(ReadManifestPath).ToArray();
        var expectedPaths = DiscoverManifests().Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expectedPaths, paths.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(paths.Length, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var pluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            Assert.DoesNotContain(
                entry.EnumerateObject(),
                property => property.Name is "id" or "version" or "name" or
                    "author" or "runtime" or "capabilities");

            var relativePath = ReadManifestPath(entry);
            Assert.Matches(
                new Regex("^(src|samples)/[A-Za-z0-9._-]+/manifest\\.json$"),
                relativePath);
            var fullPath = Path.GetFullPath(Path.Combine(
                RepositoryRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.StartsWith(
                RepositoryRoot + Path.DirectorySeparatorChar,
                fullPath,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(fullPath), $"Catalog manifest is missing: {relativePath}");

            var result = await ManifestReader.ReadAsync(Path.GetDirectoryName(fullPath)!);
            Assert.True(result.IsSuccess, $"{relativePath}: {result.Error}");
            Assert.True(pluginIds.Add(result.Manifest!.Id), $"Duplicate plugin id: {result.Manifest.Id}");
        }
    }

    [Fact]
    public async Task Catalog_LocksClassificationAndMarketplaceBoundaries()
    {
        using var document = ReadCatalog();
        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToArray();

        Assert.Equal(25, Count(entries, "built_in"));
        Assert.Equal(5, Count(entries, "marketplace"));
        Assert.Equal(7, Count(entries, "local_trusted"));
        Assert.Equal(1, Count(entries, "sample"));
        Assert.Equal(2, Count(entries, "reference"));
        Assert.Equal(8, entries.Count(entry => ReadListing(entry) != "none"));

        foreach (var entry in entries)
        {
            var classifications = ReadClassifications(entry);
            Assert.NotEmpty(classifications);
            Assert.Equal(classifications.Count, classifications.Distinct(StringComparer.Ordinal).Count());
            Assert.All(classifications, value => Assert.Contains(value, Classifications));

            var listing = ReadListing(entry);
            Assert.Contains(listing, Listings);
            if (listing == "remote") Assert.Contains("marketplace", classifications);
            if (listing == "local") Assert.Contains("local_trusted", classifications);
            if (classifications.Contains("marketplace")) Assert.Equal("remote", listing);

            var category = entry.GetProperty("category").GetString();
            Assert.Contains(category, Categories);
            var tags = ReadStrings(entry.GetProperty("tags")).ToArray();
            Assert.InRange(tags.Length, 1, 8);
            Assert.Equal(tags.Length, tags.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(tags, tag => Assert.InRange(tag.Length, 1, 32));
            Assert.InRange(entry.GetProperty("summary").GetString()!.Length, 1, 200);

            var manifestPath = Path.Combine(
                RepositoryRoot,
                ReadManifestPath(entry).Replace('/', Path.DirectorySeparatorChar));
            var result = await ManifestReader.ReadAsync(Path.GetDirectoryName(manifestPath)!);
            Assert.True(result.IsSuccess, result.Error);
            var runtime = string.IsNullOrWhiteSpace(result.Manifest!.Runtime)
                ? "native"
                : result.Manifest.Runtime;
            if (classifications.Contains("marketplace")) Assert.Equal("webview", runtime);
            if (classifications.Contains("local_trusted")) Assert.NotEqual("webview", runtime);
        }
    }

    private static int Count(JsonElement[] entries, string classification)
        => entries.Count(entry => ReadClassifications(entry).Contains(classification));

    private static string ReadManifestPath(JsonElement entry)
        => entry.GetProperty("manifest").GetString()!;

    private static string ReadListing(JsonElement entry)
        => entry.GetProperty("marketplace_listing").GetString()!;

    private static IReadOnlyList<string> ReadClassifications(JsonElement entry)
        => ReadStrings(entry.GetProperty("classifications")).ToArray();

    private static IEnumerable<string> ReadStrings(JsonElement element)
        => element.EnumerateArray().Select(item => item.GetString()!);

    private static JsonDocument ReadCatalog()
    {
        var document = JsonDocument.Parse(File.ReadAllText(CatalogPath));
        Assert.Equal(1, document.RootElement.GetProperty("schema_version").GetInt32());
        return document;
    }

    private static IEnumerable<string> DiscoverManifests()
    {
        foreach (var rootName in new[] { "src", "samples" })
        {
            var root = Path.Combine(RepositoryRoot, rootName);
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var path = Path.Combine(directory, "manifest.json");
                if (File.Exists(path))
                    yield return Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/');
            }
        }
    }

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
