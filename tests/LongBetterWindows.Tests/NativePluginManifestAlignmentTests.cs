using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LongBetterWindows.Tests;

public sealed class NativePluginManifestAlignmentTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void NotificationConsumers_DeclareNotificationCapability()
    {
        foreach (var pluginDirectory in NativePluginDirectories())
        {
            var sources = ReadSources(pluginDirectory);
            if (!Regex.IsMatch(sources, @"\b(?:host|_host)\.Notification\b"))
                continue;

            using var manifest = ReadManifest(pluginDirectory);
            var capabilities = manifest.RootElement
                .GetProperty("capabilities")
                .EnumerateArray()
                .Select(value => value.GetString())
                .ToArray();
            Assert.Contains("system.notification", capabilities);
        }
    }

    [Fact]
    public void ReportedVersions_MatchManifestVersions()
    {
        foreach (var pluginDirectory in NativePluginDirectories())
        {
            using var manifest = ReadManifest(pluginDirectory);
            var expected = manifest.RootElement.GetProperty("version").GetString();
            var matches = Regex.Matches(
                ReadSources(pluginDirectory),
                "public\\s+string\\s+Version\\s*=>\\s*\"(?<version>[^\"]+)\"");

            var match = Assert.Single(matches.Cast<Match>());
            Assert.Equal(expected, match.Groups["version"].Value);
        }
    }

    private static IEnumerable<string> NativePluginDirectories()
        => Directory.EnumerateDirectories(Path.Combine(RepositoryRoot, "src"))
            .Where(directory => File.Exists(Path.Combine(directory, "manifest.json")))
            .Where(directory => File.Exists(Path.Combine(
                directory,
                Path.GetFileName(directory) + ".csproj")))
            .OrderBy(directory => directory, StringComparer.Ordinal);

    private static JsonDocument ReadManifest(string pluginDirectory)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(pluginDirectory, "manifest.json")));

    private static string ReadSources(string pluginDirectory)
        => string.Join(
            "\n",
            Directory.EnumerateFiles(pluginDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));

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
