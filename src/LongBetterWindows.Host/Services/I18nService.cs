using System.IO;
using System.Text.Json;
using System.Windows;
using Serilog;

namespace LongBetterWindows.Host.Services;

public sealed class I18nService
{
    public const string DefaultLanguage = "zh-CN";
    public static readonly IReadOnlyList<string> SupportedLanguages =
        ["zh-CN", "en-US"];

    private readonly string _resourceDirectory;
    private readonly string _settingsPath;
    private IReadOnlyDictionary<string, string> _strings =
        new Dictionary<string, string>();

    public I18nService(string? resourceDirectory = null, string? settingsPath = null)
    {
        _resourceDirectory = resourceDirectory ?? Path.Combine(
            AppContext.BaseDirectory,
            "i18n");
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LongBetterWindows",
            "language.json");
    }

    public string CurrentLanguage { get; private set; } = DefaultLanguage;
    public event Action<string>? LanguageChanged;

    public void Initialize(string? overrideLanguage = null)
    {
        var requested = string.IsNullOrWhiteSpace(overrideLanguage)
            ? ReadPersistedLanguage()
            : overrideLanguage;
        Load(IsSupported(requested) ? requested! : DefaultLanguage);
    }

    public string T(string key, string? fallback = null)
        => _strings.TryGetValue(key, out var value)
            ? value
            : fallback ?? key;

    public void SetLanguage(string language, bool persist = true)
    {
        if (!IsSupported(language))
            throw new ArgumentOutOfRangeException(
                nameof(language),
                language,
                "Unsupported UI language.");
        if (string.Equals(
            CurrentLanguage,
            language,
            StringComparison.OrdinalIgnoreCase))
            return;

        Load(language);
        if (persist)
            PersistLanguage(language);
        LanguageChanged?.Invoke(language);
        Log.Information("UI language changed: {Language}", language);
    }

    public void ApplyTo(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        foreach (var entry in _strings)
            resources[$"i18n.{entry.Key}"] = entry.Value;
        resources["i18n.currentLanguage"] = CurrentLanguage;
    }

    public static bool IsSupported(string? language)
        => SupportedLanguages.Contains(
            language ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);

    private void Load(string language)
    {
        var fallback = ReadLanguageFile(DefaultLanguage);
        var selected = string.Equals(
            language,
            DefaultLanguage,
            StringComparison.OrdinalIgnoreCase)
            ? fallback
            : ReadLanguageFile(language);
        var merged = new Dictionary<string, string>(
            fallback,
            StringComparer.Ordinal);
        foreach (var entry in selected)
            merged[entry.Key] = entry.Value;

        CurrentLanguage = SupportedLanguages.First(item =>
            string.Equals(item, language, StringComparison.OrdinalIgnoreCase));
        _strings = merged;
    }

    private Dictionary<string, string> ReadLanguageFile(string language)
    {
        var path = Path.Combine(_resourceDirectory, $"{language}.json");
        if (!File.Exists(path))
        {
            if (language == DefaultLanguage)
                throw new FileNotFoundException(
                    "Default localization resource was not found.",
                    path);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(path)) ??
            new Dictionary<string, string>();
        values.Remove("_lang");
        return new Dictionary<string, string>(values, StringComparer.Ordinal);
    }

    private string? ReadPersistedLanguage()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return null;
            using var document = JsonDocument.Parse(
                File.ReadAllText(_settingsPath));
            return document.RootElement.TryGetProperty("language", out var value)
                ? value.GetString()
                : null;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not read UI language settings.");
            return null;
        }
    }

    private void PersistLanguage(string language)
    {
        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException(
                "Language settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = _settingsPath + "." +
            Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(new
                {
                    schema_version = 1,
                    language,
                }));
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
