using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public sealed class PluginLocalizationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "LongBetterWindows.PluginLocalization.Tests",
        Guid.NewGuid().ToString("N"));

    public PluginLocalizationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ManifestReader_AcceptsOptionalLocalizationDeclaration()
    {
        WriteManifest(new
        {
            id = "test.localized",
            version = "1.0.0",
            name = "Localized",
            entry_point = "plugin.dll",
            localization = new
            {
                default_language = "zh-CN",
                resources = new Dictionary<string, string>
                {
                    ["zh-CN"] = "i18n/zh-CN.json",
                    ["en-US"] = "i18n/en-US.json",
                },
            },
        });

        var result = await ManifestReader.ReadAsync(_root);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(
            "i18n/en-US.json",
            result.Manifest!.Localization!.Resources["en-US"]);
    }

    [Theory]
    [InlineData("", "i18n/zh-CN.json")]
    [InlineData("zh-CN", "../outside.json")]
    [InlineData("zh-CN", "i18n/zh-CN.txt")]
    [InlineData("zh-CN", "i18n/\0.json")]
    public async Task ManifestReader_RejectsInvalidLocalizationDeclaration(
        string defaultLanguage,
        string resourcePath)
    {
        WriteManifest(new
        {
            id = "test.invalid-localization",
            version = "1.0.0",
            name = "Invalid localization",
            entry_point = "plugin.dll",
            localization = new
            {
                default_language = defaultLanguage,
                resources = new Dictionary<string, string>
                {
                    ["zh-CN"] = resourcePath,
                },
            },
        });

        var result = await ManifestReader.ReadAsync(_root);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == ManifestValidationCode.InvalidLocalization
                && issue.Path == "localization");
    }

    [Fact]
    public void Loader_UsesRequestedLanguageAndCopiesResources()
    {
        WriteResources("en-US", new Dictionary<string, string>
        {
            ["plugin.name"] = "Localized plugin",
        });
        var localization = Localization();

        var loaded = PluginLocalizationLoader.TryLoad(
            _root,
            localization,
            "en-US",
            out var context,
            out var error);

        Assert.True(loaded, error);
        Assert.Equal("en-US", context!.RequestedLanguage);
        Assert.Equal("en-US", context.ResolvedLanguage);
        Assert.Equal("Localized plugin", context.Resources["plugin.name"]);

        var source = new Dictionary<string, string> { ["key"] = "before" };
        var copied = new PluginLanguageContext("en-US", "en-US", source);
        source["key"] = "after";
        Assert.Equal("before", copied.Resources["key"]);
    }

    [Fact]
    public void Loader_FallsBackToDefaultLanguage()
    {
        WriteResources("zh-CN", new Dictionary<string, string>
        {
            ["plugin.name"] = "默认名称",
        });

        var loaded = PluginLocalizationLoader.TryLoad(
            _root,
            Localization(),
            "fr-FR",
            out var context,
            out var error);

        Assert.True(loaded, error);
        Assert.Equal("fr-FR", context!.RequestedLanguage);
        Assert.Equal("zh-CN", context.ResolvedLanguage);
        Assert.Equal("默认名称", context.Resources["plugin.name"]);
    }

    [Fact]
    public async Task Scanner_NotifiesDeclaredPluginsWithoutChangingState()
    {
        WriteResources("en-US", new Dictionary<string, string>
        {
            ["plugin.name"] = "Localized plugin",
        });
        var plugin = new LanguageAwarePlugin();
        var manifest = new PluginManifest
        {
            Id = plugin.Id,
            Name = plugin.Name,
            Version = plugin.Version,
            EntryPoint = "plugin.dll",
            Localization = Localization(),
        };
        using var scanner = new PluginScanner(_root);
        scanner.LoadedPlugins.Add(new PluginEntry(
            manifest,
            plugin,
            _root,
            registrationRevision: 1)
        {
            State = PluginState.Running,
        });

        await scanner.NotifyLanguageChangedAsync("en-US");

        Assert.Equal(PluginState.Running, plugin.State);
        Assert.Equal("en-US", Assert.Single(plugin.Contexts).ResolvedLanguage);
    }

    [Fact]
    public void WebProtocol_UsesStableLanguageMessageShape()
    {
        var json = WebPluginBridgeProtocol.SerializeLanguageChanged(
            new PluginLanguageContext(
                "en-US",
                "zh-CN",
                new Dictionary<string, string> { ["plugin.name"] = "Name" }));
        using var document = JsonDocument.Parse(json);

        Assert.Equal(
            "long.language-changed",
            document.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            "en-US",
            document.RootElement.GetProperty("requested_language").GetString());
        Assert.Equal(
            "zh-CN",
            document.RootElement.GetProperty("resolved_language").GetString());
        Assert.Equal(
            "Name",
            document.RootElement
                .GetProperty("resources")
                .GetProperty("plugin.name")
                .GetString());
    }

    [Fact]
    public void WebLanguageState_ReplaysOnlyTheLatestMessageAfterNavigation()
    {
        var state = new WebPluginLanguageMessageState();

        Assert.Null(state.Update("zh-CN"));
        Assert.Null(state.Update("en-US"));
        Assert.Equal("en-US", state.CompleteNavigation(isSuccess: true));
        Assert.Equal("zh-CN", state.Update("zh-CN"));

        state.BeginNavigation();
        Assert.Null(state.Update("en-US"));
        Assert.Null(state.CompleteNavigation(isSuccess: false));
        Assert.Equal("en-US", state.CompleteNavigation(isSuccess: true));
    }

    private PluginLocalizationPreference Localization()
        => new()
        {
            DefaultLanguage = "zh-CN",
            Resources = new Dictionary<string, string>
            {
                ["zh-CN"] = "i18n/zh-CN.json",
                ["en-US"] = "i18n/en-US.json",
            },
        };

    private void WriteResources(
        string language,
        IReadOnlyDictionary<string, string> resources)
    {
        var directory = Path.Combine(_root, "i18n");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, language + ".json"),
            JsonSerializer.Serialize(resources));
    }

    private void WriteManifest(object manifest)
        => File.WriteAllText(
            Path.Combine(_root, "manifest.json"),
            JsonSerializer.Serialize(manifest));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class LanguageAwarePlugin :
        ILongPlugin,
        IPluginLanguageLifecycle
    {
        public string Id => "test.language-aware";
        public string Name => "Language aware";
        public string Version => "1.0.0";
        public PluginState State { get; private set; } = PluginState.Running;
        public List<PluginLanguageContext> Contexts { get; } = [];

        public Task<bool> InitializeAsync(IHostApi host) =>
            Task.FromResult(true);

        public Task<bool> StartAsync() => Task.FromResult(true);

        public Task<bool> StopAsync()
        {
            State = PluginState.Stopped;
            return Task.FromResult(true);
        }

        public Task OnLanguageChangedAsync(
            PluginLanguageContext context,
            CancellationToken cancellationToken = default)
        {
            Contexts.Add(context);
            return Task.CompletedTask;
        }
    }
}
