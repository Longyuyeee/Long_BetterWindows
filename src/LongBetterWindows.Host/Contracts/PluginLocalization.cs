using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace LongBetterWindows.Host.Contracts;

public sealed class PluginLocalizationPreference
{
    [JsonPropertyName("default_language")]
    public string DefaultLanguage { get; init; } = string.Empty;

    [JsonPropertyName("resources")]
    public Dictionary<string, string> Resources { get; init; } = new();
}

public sealed class PluginLanguageContext
{
    public PluginLanguageContext(
        string requestedLanguage,
        string resolvedLanguage,
        IReadOnlyDictionary<string, string> resources)
    {
        RequestedLanguage = requestedLanguage;
        ResolvedLanguage = resolvedLanguage;
        Resources = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(
                resources,
                StringComparer.Ordinal));
    }

    public string RequestedLanguage { get; }
    public string ResolvedLanguage { get; }
    public IReadOnlyDictionary<string, string> Resources { get; }
}
