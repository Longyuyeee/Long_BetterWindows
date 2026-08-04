using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.PluginCatalogGenerator;

public sealed class PluginCatalogSynchronizer
{
    public const string CatalogRelativePath = "catalog/plugin-catalog.json";
    public const string RegistryRelativePath =
        "src/LongBetterWindows.Host/Marketplace/registry.json";
    public const string DocumentationRelativePath = "docs/plugin-catalog.md";

    private static readonly JsonSerializerOptions SourceOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<PluginCatalogSyncResult> SynchronizeAsync(
        string repositoryRoot,
        bool checkOnly,
        CancellationToken cancellationToken = default)
    {
        var generated = await GenerateAsync(repositoryRoot, cancellationToken);
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RegistryRelativePath] = generated.RegistryJson,
            [DocumentationRelativePath] = generated.DocumentationMarkdown,
        };

        var changed = outputs
            .Where(output => !ContentMatches(repositoryRoot, output.Key, output.Value))
            .Select(output => output.Key)
            .ToArray();
        if (checkOnly && changed.Length > 0)
            throw new InvalidDataException(
                $"Generated plugin catalog outputs are stale: {string.Join(", ", changed)}");

        if (!checkOnly)
        {
            foreach (var output in outputs)
                await WriteAtomicAsync(repositoryRoot, output.Key, output.Value, cancellationToken);
        }

        return new PluginCatalogSyncResult(outputs.Keys.ToArray(), changed);
    }

    public async Task<GeneratedPluginCatalog> GenerateAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var sourcePath = Path.Combine(
            repositoryRoot,
            CatalogRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var source = JsonSerializer.Deserialize<PluginCatalogSource>(
            await File.ReadAllTextAsync(sourcePath, cancellationToken),
            SourceOptions) ?? throw new InvalidDataException("Plugin catalog is empty.");
        if (source.SchemaVersion != 1)
            throw new InvalidDataException(
                $"Unsupported plugin catalog schema version: {source.SchemaVersion}.");

        var listedEntries = source.Entries
            .Where(entry => !string.Equals(
                entry.MarketplaceListing,
                "none",
                StringComparison.Ordinal))
            .ToArray();
        var generatedEntries = new List<GeneratedCatalogEntry>(listedEntries.Length);
        foreach (var entry in listedEntries)
        {
            ValidateSourceEntry(entry);
            var manifestPath = Path.GetFullPath(Path.Combine(
                repositoryRoot,
                entry.Manifest.Replace('/', Path.DirectorySeparatorChar)));
            EnsureWithinRepository(repositoryRoot, manifestPath);
            var manifestResult = await ManifestReader.ReadAsync(
                Path.GetDirectoryName(manifestPath)!);
            if (!manifestResult.IsSuccess)
                throw new InvalidDataException(
                    $"Invalid manifest '{entry.Manifest}': {manifestResult.Error}");

            var manifest = manifestResult.Manifest!;
            generatedEntries.Add(new GeneratedCatalogEntry(
                manifest.Id,
                manifest.Name,
                entry.Summary,
                string.IsNullOrWhiteSpace(manifest.Description)
                    ? entry.Summary
                    : manifest.Description,
                manifest.Author,
                entry.Category,
                entry.Tags,
                [new GeneratedCatalogVersion(
                    manifest.Version,
                    manifest.Capabilities
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray(),
                    EmptyToNull(manifest.MinHostVersion),
                    EmptyToNull(manifest.MinApiVersion),
                    EmptyToNull(manifest.MinUiKitVersion))],
                entry.MarketplaceListing,
                entry.Manifest,
                string.IsNullOrWhiteSpace(manifest.Runtime) ? "native" : manifest.Runtime));
        }

        var ordered = generatedEntries
            .OrderBy(entry => entry.Id, StringComparer.Ordinal)
            .ToArray();
        var registry = new GeneratedRegistry(
            1,
            "local_package",
            ordered.Select(entry => new GeneratedRegistryEntry(
                entry.Id,
                entry.Name,
                entry.Summary,
                entry.Description,
                entry.Publisher,
                entry.Category,
                entry.Tags,
                entry.Versions)).ToArray());
        var registryJson = JsonSerializer.Serialize(registry, OutputOptions) + "\n";
        return new GeneratedPluginCatalog(registryJson, BuildDocumentation(ordered));
    }

    private static void ValidateSourceEntry(PluginCatalogSourceEntry entry)
    {
        if (entry.MarketplaceListing is not ("local" or "remote"))
            throw new InvalidDataException(
                $"Invalid marketplace listing for '{entry.Manifest}'.");
        if (entry.MarketplaceListing == "local"
            && !entry.Classifications.Contains("local_trusted", StringComparer.Ordinal))
            throw new InvalidDataException(
                $"Local listing must be local_trusted: {entry.Manifest}.");
        if (entry.MarketplaceListing == "remote"
            && !entry.Classifications.Contains("marketplace", StringComparer.Ordinal))
            throw new InvalidDataException(
                $"Remote listing must be marketplace classified: {entry.Manifest}.");
    }

    private static string BuildDocumentation(IReadOnlyList<GeneratedCatalogEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Plugin Catalog");
        builder.AppendLine();
        builder.AppendLine(
            "Generated from `catalog/plugin-catalog.json` and plugin manifests. Do not edit this file manually.");
        builder.AppendLine();
        builder.AppendLine("| Plugin | ID | Version | Listing | Runtime | Category | Capabilities | Manifest |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var entry in entries)
        {
            var version = entry.Versions[0];
            var manifestLink = "../" + entry.ManifestPath;
            builder.Append("| ").Append(Escape(entry.Name))
                .Append(" | `").Append(entry.Id).Append("` | `")
                .Append(version.Version).Append("` | `")
                .Append(entry.Listing).Append("` | `")
                .Append(entry.Runtime).Append("` | `")
                .Append(entry.Category).Append("` | ")
                .Append(version.Capabilities.Count == 0
                    ? "-"
                    : string.Join(", ", version.Capabilities.Select(value => $"`{value}`")))
                .Append(" | [manifest.json](").Append(manifestLink).AppendLine(") |");
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void EnsureWithinRepository(string repositoryRoot, string path)
    {
        var rootPrefix = repositoryRoot.TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Catalog path escapes repository: {path}");
    }

    private static bool ContentMatches(string root, string relativePath, string expected)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path)
            && NormalizeNewlines(File.ReadAllText(path)) == NormalizeNewlines(expected);
    }

    private static string NormalizeNewlines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static async Task WriteAtomicAsync(
        string root,
        string relativePath,
        string content,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                NormalizeNewlines(content),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private sealed record PluginCatalogSource(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("entries")] IReadOnlyList<PluginCatalogSourceEntry> Entries);

    private sealed record PluginCatalogSourceEntry(
        [property: JsonPropertyName("manifest")] string Manifest,
        [property: JsonPropertyName("classifications")] IReadOnlyList<string> Classifications,
        [property: JsonPropertyName("marketplace_listing")] string MarketplaceListing,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
        [property: JsonPropertyName("summary")] string Summary);

    private sealed record GeneratedRegistry(
        int SchemaVersion,
        string Source,
        IReadOnlyList<GeneratedRegistryEntry> Entries);

    private sealed record GeneratedRegistryEntry(
        string Id,
        string Name,
        string Summary,
        string Description,
        string Publisher,
        string Category,
        IReadOnlyList<string> Tags,
        IReadOnlyList<GeneratedCatalogVersion> Versions);

    private sealed record GeneratedCatalogEntry(
        string Id,
        string Name,
        string Summary,
        string Description,
        string Publisher,
        string Category,
        IReadOnlyList<string> Tags,
        IReadOnlyList<GeneratedCatalogVersion> Versions,
        string Listing,
        string ManifestPath,
        string Runtime);

    private sealed record GeneratedCatalogVersion(
        string Version,
        IReadOnlyList<string> Capabilities,
        string? MinHostVersion,
        string? MinApiVersion,
        string? MinUiKitVersion);
}

public sealed record GeneratedPluginCatalog(
    string RegistryJson,
    string DocumentationMarkdown);

public sealed record PluginCatalogSyncResult(
    IReadOnlyList<string> OutputPaths,
    IReadOnlyList<string> ChangedPaths);
