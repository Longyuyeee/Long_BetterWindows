using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Engine;

internal static class PluginLocalizationLoader
{
    private const long MaximumResourceBytes = 256 * 1024;
    private const int MaximumResourceEntries = 512;
    private const int MaximumKeyLength = 128;
    private const int MaximumValueLength = 4096;

    public static bool TryLoad(
        string pluginDirectory,
        PluginLocalizationPreference localization,
        string requestedLanguage,
        out PluginLanguageContext? context,
        out string? error)
    {
        context = null;
        error = null;

        var selected = FindResource(localization, requestedLanguage)
            ?? FindResource(localization, localization.DefaultLanguage);
        if (selected is null)
        {
            error = "No matching or default localization resource was declared.";
            return false;
        }

        try
        {
            var root = Path.GetFullPath(pluginDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(root, selected.Value.Path));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                error = "Localization resource resolved outside the plugin directory.";
                return false;
            }

            var info = new FileInfo(path);
            if (!info.Exists)
            {
                error = $"Localization resource was not found: {selected.Value.Path}";
                return false;
            }
            if (info.Length > MaximumResourceBytes)
            {
                error = "Localization resource exceeds the 256 KiB limit.";
                return false;
            }

            var resources = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(path));
            if (resources is null || resources.Count > MaximumResourceEntries)
            {
                error = "Localization resource contains too many entries.";
                return false;
            }
            if (resources.Any(entry =>
                    string.IsNullOrWhiteSpace(entry.Key)
                    || entry.Key.Length > MaximumKeyLength
                    || entry.Value is null
                    || entry.Value.Length > MaximumValueLength))
            {
                error = "Localization resource contains an invalid key or value.";
                return false;
            }

            context = new PluginLanguageContext(
                requestedLanguage,
                selected.Value.Language,
                resources);
            return true;
        }
        catch (Exception exception) when (exception is
            IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static (string Language, string Path)? FindResource(
        PluginLocalizationPreference localization,
        string language)
    {
        if (localization.Resources is null)
            return null;

        foreach (var resource in localization.Resources)
        {
            if (string.Equals(
                resource.Key,
                language,
                StringComparison.OrdinalIgnoreCase))
                return (resource.Key, resource.Value);
        }

        return null;
    }
}
