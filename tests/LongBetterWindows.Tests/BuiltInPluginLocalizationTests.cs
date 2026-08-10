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
    [InlineData("FileRenamerPlugin")]
    [InlineData("JsonFormatterPlugin")]
    [InlineData("MarkdownPreview")]
    [InlineData("PasswordGenerator")]
    [InlineData("QuickNotePlugin")]
    [InlineData("SamplePlugin")]
    [InlineData("TextDiffPlugin")]
    [InlineData("TranslatePlugin")]
    public async Task UtilityPlugin_DefaultsToOnDemandStartup(string plugin)
    {
        var root = FindRepositoryRoot();
        var result = await ManifestReader.ReadAsync(
            Path.Combine(root, "src", plugin));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(
            "false",
            ReadSetting(result.Manifest!.DefaultSettings, "auto_start"));
        Assert.False(result.Manifest.Lifecycle?.StartWithHost ?? false);
    }

    [Theory]
    [InlineData("Base64Tool")]
    [InlineData("ClipboardHistory")]
    [InlineData("ClipboardTool")]
    [InlineData("ColorPickerPlugin")]
    [InlineData("DevToolkit")]
    [InlineData("FileOrganizer")]
    [InlineData("FileRenamerPlugin")]
    [InlineData("FolderNotePlugin")]
    [InlineData("HardwareMonitor")]
    [InlineData("JsonFormatterPlugin")]
    [InlineData("MarkdownPreview")]
    [InlineData("MacroPlugin")]
    [InlineData("PasswordGenerator")]
    [InlineData("PortManager")]
    [InlineData("QuickLaunchPlugin")]
    [InlineData("QuickNotePlugin")]
    [InlineData("RegexTester")]
    [InlineData("ScreenshotPlugin")]
    [InlineData("SamplePlugin")]
    [InlineData("TimestampConverter")]
    [InlineData("TextDiffPlugin")]
    [InlineData("TranslatePlugin")]
    [InlineData("UrlToolkit")]
    [InlineData("UuidGenerator")]
    [InlineData("WindowManagerPlugin")]
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
            foreach (var argument in command.ArgumentSchema)
            {
                Assert.Contains(
                    $"commands.{command.Id}.arguments.{argument.Key}.name",
                    chineseKeys);
                Assert.Contains(
                    $"commands.{command.Id}.arguments.{argument.Key}.description",
                    chineseKeys);
            }
            foreach (var preset in command.ArgumentPresets)
            {
                Assert.Contains(
                    $"commands.{command.Id}.presets.{preset.Id}.name",
                    chineseKeys);
            }
            foreach (var output in command.Outputs)
            {
                Assert.Contains(
                    $"commands.{command.Id}.outputs.{output.Key}.description",
                    chineseKeys);
            }
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
                    ArgumentSchema =
                    [
                        new PluginCommandArgumentDeclaration
                        {
                            Key = "count",
                            Name = "原始数量",
                            Description = "原始参数说明",
                            Type = PluginCommandArgumentType.Integer,
                            DefaultValue = "10",
                            Minimum = 1,
                            Maximum = 100,
                        },
                    ],
                    ArgumentPresets =
                    [
                        new PluginCommandArgumentPreset
                        {
                            Id = "batch",
                            Name = "原始预设",
                            Arguments = new Dictionary<string, string>
                            {
                                ["count"] = "100",
                            },
                        },
                    ],
                    Outputs =
                    [
                        new PluginCommandOutputDeclaration
                        {
                            Key = "result",
                            Type = PluginCommandOutputType.Text,
                            Description = "原始输出说明",
                        },
                    ],
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
                    ["commands.run.arguments.count.name"] = "Localized count",
                    ["commands.run.arguments.count.description"] = "Localized argument",
                    ["commands.run.presets.batch.name"] = "Localized batch",
                    ["commands.run.outputs.result.description"] = "Localized output",
                })));

        var entry = Assert.IsType<PluginEntry>(registry.Get(manifest.Id));
        var descriptor = Assert.IsType<CommandDescriptor>(
            registry.Commands.Get(manifest.Id + ":run"));
        Assert.Equal("Localized plugin", entry.DisplayName);
        Assert.Equal("Localized plugin", descriptor.PluginName);
        Assert.Equal("Localized command", descriptor.Title);
        Assert.Equal("Localized description", descriptor.Description);
        var argument = Assert.Single(descriptor.ArgumentSchema);
        Assert.Equal("Localized count", argument.Name);
        Assert.Equal("Localized argument", argument.Description);
        Assert.Equal("count", argument.Key);
        Assert.Equal("10", argument.DefaultValue);
        var preset = Assert.Single(descriptor.ArgumentPresets);
        Assert.Equal("Localized batch", preset.Name);
        Assert.Equal("100", preset.Arguments["count"]);
        var output = Assert.Single(descriptor.Outputs);
        Assert.Equal("Localized output", output.Description);
        Assert.Equal(PluginCommandOutputType.Text, output.Type);
        Assert.Equal("原始命令", descriptor.Command.Title);
        Assert.Equal("原始数量", Assert.Single(descriptor.Command.ArgumentSchema).Name);
        Assert.Equal("原始预设", Assert.Single(descriptor.Command.ArgumentPresets).Name);
        Assert.Equal("原始输出说明", Assert.Single(descriptor.Command.Outputs).Description);
        Assert.NotSame(descriptor.Command.ArgumentSchema, descriptor.ArgumentSchema);
        Assert.NotSame(descriptor.Command.ArgumentPresets, descriptor.ArgumentPresets);
        Assert.NotSame(descriptor.Command.Outputs, descriptor.Outputs);
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
        var localizationStart = source.IndexOf(
            "function applyLocalization(message)",
            StringComparison.Ordinal);
        var localizationEnd = source.IndexOf(
            "function clearResult()",
            localizationStart,
            StringComparison.Ordinal);
        Assert.True(localizationEnd > localizationStart);
        var localizationBody = source[localizationStart..localizationEnd];
        Assert.DoesNotContain("input.value = ''", localizationBody);
        Assert.DoesNotContain("output.value = ''", localizationBody);
    }

    [Fact]
    public void Base64Tool_UsesUtf8BoundariesAndInvalidatesStaleResults()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Base64Tool",
            "index.html"));

        Assert.Contains("function clearResult()", source);
        Assert.Contains("input.addEventListener('input', invalidateResult)", source);
        Assert.Contains("new TextEncoder().encode(value)", source);
        Assert.Contains("new TextDecoder('utf-8', { fatal: true, ignoreBOM: true })", source);
        Assert.Contains("function isWellFormedUnicode(value)", source);
        Assert.Contains("if (!(next >= 0xdc00 && next <= 0xdfff))", source);
        Assert.Contains("offset += 32768", source);
        Assert.Contains("value.length > maxCommandOutputLength", source);
        Assert.Contains("copyButton.disabled = true", source);
        Assert.Contains("copyButton.disabled = false", source);
        Assert.Contains("error.emptyInput", source);
        Assert.Contains("error.clipboardEmpty", source);
        Assert.Contains("error.unknownCommand", source);
        Assert.DoesNotContain("unescape(", source);
        Assert.DoesNotContain("escape(atob", source);
    }

    [Fact]
    public void JsonFormatter_InvalidatesStaleResultsAndReportsClipboardFailures()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "JsonFormatterPlugin",
            "index.html"));

        Assert.Contains("function clearResult()", source);
        Assert.Contains("copyButton.disabled = true", source);
        Assert.Contains("copyButton.disabled = false", source);
        Assert.Contains("input.addEventListener('input', invalidateResult)", source);
        Assert.Contains("if (!result || !result.success)", source);
        Assert.Contains("error.clipboardEmpty", source);
        Assert.Contains("error.emptyInput", source);
    }

    [Fact]
    public void TextDiff_NormalizesLineEndingsAndInvalidatesStaleResults()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "TextDiffPlugin",
            "index.html"));

        Assert.Contains("function splitLines(value)", source);
        Assert.Contains("value.replace(/\\r\\n?/g, '\\n')", source);
        Assert.Contains("function invalidateComparison()", source);
        Assert.Contains("textA.addEventListener('input', invalidateComparison)", source);
        Assert.Contains("textB.addEventListener('input', invalidateComparison)", source);
        Assert.Contains("setStatus('status.emptyInput'", source);
        Assert.Contains("line.setAttribute(", source);
        Assert.Contains("'aria-label'", source);
        Assert.Contains("diff.addedLine", source);
        Assert.Contains("diff.deletedLine", source);
        Assert.Contains("diff.unchangedLine", source);
    }

    [Fact]
    public void TimestampConverter_ReportsUnitsTimezoneAndInvalidatesStaleResults()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "TimestampConverter",
            "index.html"));

        Assert.Contains("function clearConversion()", source);
        Assert.Contains("input.addEventListener('input', invalidateConversion)", source);
        Assert.Contains("copyButton.disabled = true", source);
        Assert.Contains("copyButton.disabled = false", source);
        Assert.Contains("function inputKindLabel(kind)", source);
        Assert.Contains("inputKind.seconds", source);
        Assert.Contains("inputKind.milliseconds", source);
        Assert.Contains("inputKind.localDate", source);
        Assert.Contains("inputKind.zonedDate", source);
        Assert.Contains("Intl.DateTimeFormat().resolvedOptions().timeZone", source);
        Assert.Contains("function parseLocalDateText(text)", source);
        Assert.Contains("date.getFullYear() === parts[0]", source);
        Assert.Contains("Math.abs(milliseconds) > maxDateMilliseconds", source);
        Assert.Contains("status.emptyInput", source);
        Assert.Contains("status.outOfRange", source);
    }

    [Fact]
    public void UrlToolkit_InvalidatesStaleResultsAndSeparatesEncodingErrors()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "UrlToolkit",
            "index.html"));

        Assert.Contains("function clearResult()", source);
        Assert.Contains("input.addEventListener('input', invalidateResult)", source);
        Assert.Contains("copyButton.disabled = true", source);
        Assert.Contains("copyButton.disabled = false", source);
        Assert.Contains("Array.from(output.value).length", source);
        Assert.Contains("setStatus('error.emptyInput'", source);
        Assert.Contains("setStatus('error.decode'", source);
        Assert.Contains("setStatus('error.encode'", source);
        Assert.Contains("setStatus('error.clipboardEmpty'", source);
        Assert.Contains("input.value = window.LongUI.commandText(command) || ''", source);
    }

    [Fact]
    public void UuidGenerator_ValidatesAmountUniquenessAndResultLifecycle()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "UuidGenerator",
            "index.html"));

        Assert.Contains("function clearResult()", source);
        Assert.Contains("function readAmount()", source);
        Assert.Contains("Number.isInteger(amount)", source);
        Assert.Contains("function generateUniqueValues(amount)", source);
        Assert.Contains("const values = new Set()", source);
        Assert.Contains("copyButton.disabled = true", source);
        Assert.Contains("copyButton.disabled = false", source);
        Assert.Contains("item.addEventListener('input', invalidateResult)", source);
        Assert.Contains("output.selectionEnd > output.selectionStart", source);
        Assert.Contains("status.selectionCopied", source);
        Assert.Contains("error.invalidAmount", source);
        Assert.Contains("error.randomSource", source);
    }

    [Theory]
    [InlineData("ClipboardTool", "content.value = text", "let currentTab = 'history'")]
    [InlineData("QuickNotePlugin", "if (input.value === text) input.value = ''", "let notes = []")]
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
    [InlineData("ClipboardHistory")]
    [InlineData("ClipboardTool")]
    [InlineData("DevToolkit")]
    [InlineData("FileOrganizer")]
    [InlineData("FileRenamerPlugin")]
    [InlineData("HardwareMonitor")]
    [InlineData("JsonFormatterPlugin")]
    [InlineData("MarkdownPreview")]
    [InlineData("PasswordGenerator")]
    [InlineData("PortManager")]
    [InlineData("QuickNotePlugin")]
    [InlineData("RegexTester")]
    [InlineData("TimestampConverter")]
    [InlineData("TextDiffPlugin")]
    [InlineData("TranslatePlugin")]
    [InlineData("UrlToolkit")]
    [InlineData("UuidGenerator")]
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
                """(?:data-i18n(?:-placeholder)?="|\bt\s*\(\s*')([A-Za-z0-9._-]+)""")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(referencedKeys);
        Assert.Empty(referencedKeys.Except(declaredKeys, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("JsonFormatterPlugin", "function transform", "renderStat();", "transform(")]
    [InlineData("MarkdownPreview", "function render", "renderEmptyState();", "render();")]
    [InlineData("PasswordGenerator", "function secureIndex", "renderStrength();", "generate();")]
    [InlineData("RegexTester", "function test", "renderMatches();", "test();")]
    [InlineData("TimestampConverter", "function parse", "renderResult();", "convert();")]
    [InlineData("TextDiffPlugin", "function compare", "renderDiff();", "compare();")]
    [InlineData("TranslatePlugin", "async function translate", "renderRequestState();", "translate();")]
    [InlineData("UrlToolkit", "function transform", "renderStat();", "transform(")]
    [InlineData("UuidGenerator", "function uuid", "output.setAttribute", "generate();")]
    [InlineData("FileRenamerPlugin", "async function loadSelection", "renderListState(false);", "loadSelection();")]
    [InlineData("HardwareMonitor", "async function initialize", "renderMetricsProjection();", "updateMetrics(")]
    [InlineData("PortManager", "async function initialize", "renderPorts();", "refreshPorts(")]
    [InlineData("ClipboardHistory", "function responseData", "renderListState();", "startMonitoring();")]
    [InlineData("DevToolkit", "function base64Encode", "renderAllResults();", "base64Encode();")]
    [InlineData("FileOrganizer", "async function loadActiveFolder", "renderPlanState(false);", "analyzeFolder();")]
    public void LightweightWebPlugin_LocalizesProjectionWithoutRegeneratingValue(
        string plugin,
        string nextFunction,
        string projectionCall,
        string prohibitedCall)
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
        Assert.DoesNotContain(prohibitedCall, localizationBody);
        Assert.DoesNotContain("generate();", localizationBody);
        Assert.DoesNotContain("convert();", localizationBody);
        Assert.DoesNotContain("useNow();", localizationBody);
        Assert.DoesNotContain("startPolling();", localizationBody);
        Assert.DoesNotContain("setInterval", localizationBody);
        Assert.DoesNotContain("captureClipboard(", localizationBody);
        Assert.DoesNotContain("persistSnapshot(", localizationBody);
        Assert.DoesNotContain("autoCopy(", localizationBody);
        Assert.DoesNotContain("copyFromClipboard(", localizationBody);
        Assert.DoesNotContain("location.reload", source);
    }

    [Fact]
    public void WorkflowEditor_UsesLocalizedCommandContractProjection()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "LongBetterWindows.Host",
            "Views",
            "WorkflowEditorControl.xaml.cs"));

        Assert.Contains(
            "SnapshotArgumentSchema(descriptor?.ArgumentSchema)",
            source);
        Assert.Contains("descriptor?.ArgumentPresets", source);
        Assert.Contains("descriptor?.Outputs.Select", source);
        Assert.DoesNotContain(
            "SnapshotArgumentSchema(descriptor?.Command.ArgumentSchema)",
            source);
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
        Assert.Contains("ApplyLocalization(CreateHudLocalization())", source);
        Assert.Contains("PluginLocalization Include=\"i18n\\*.json\"", project);
        Assert.Contains("FolderNotePlugin\\i18n", hostProject);
    }

    [Fact]
    public void MacroPlugin_LocalizesProjectionWithoutRepeatingSideEffects()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "MacroPlugin",
            "MacroPluginImpl.cs"));
        var project = File.ReadAllText(Path.Combine(
            root,
            "src",
            "MacroPlugin",
            "MacroPlugin.csproj"));
        var hostProject = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "LongBetterWindows.Host.csproj"));
        var start = source.IndexOf(
            "public Task OnLanguageChangedAsync(",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private MacroOverlayLocalization",
            start,
            StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        var languageBody = source[start..end];
        Assert.Contains("IPluginLanguageLifecycle", source);
        Assert.Contains("ApplyLocalization", languageBody);
        Assert.DoesNotContain("RegisterAsync", languageBody);
        Assert.DoesNotContain("ToggleRecording", languageBody);
        Assert.DoesNotContain("PlayOnce", languageBody);
        Assert.Contains("PluginLocalization Include=\"i18n\\*.json\"", project);
        Assert.Contains("MacroPlugin\\i18n", hostProject);
    }

    [Fact]
    public void SamplePlugin_IsLocalizedReferenceImplementation()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SamplePlugin",
            "HelloPlugin.cs"));
        var project = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SamplePlugin",
            "SamplePlugin.csproj"));
        var hostProject = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "LongBetterWindows.Host.csproj"));
        var start = source.IndexOf(
            "public Task OnLanguageChangedAsync(",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private string Text(",
            start,
            StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        var languageBody = source[start..end];
        Assert.Contains("IHasSettingsUI", source);
        Assert.Contains("host.Settings", source);
        Assert.Contains("IPluginLanguageLifecycle", source);
        Assert.Contains("ApplyLocalization", languageBody);
        Assert.DoesNotContain("ShowMainUI", languageBody);
        Assert.DoesNotContain("StartAsync", languageBody);
        Assert.DoesNotContain("_audience =", languageBody);
        Assert.Contains("<UseWPF>true</UseWPF>", project);
        Assert.Contains("PluginLocalization Include=\"i18n\\*.json\"", project);
        Assert.Contains("SamplePlugin\\i18n", hostProject);
    }

    [Theory]
    [InlineData(
        "ColorPickerPlugin",
        "ColorPickerPluginImpl.cs",
        "private ColorPickerWindowLocalization CreateWindowLocalization",
        "OnPickColor();")]
    [InlineData(
        "ScreenshotPlugin",
        "ScreenshotPluginImpl.cs",
        "private RegionSelectorLocalization CreateSelectorLocalization",
        "CaptureFullScreen();")]
    public void NativeCapturePlugin_LocalizesProjectionWithoutRepeatingSideEffects(
        string plugin,
        string implementationFile,
        string nextMarker,
        string prohibitedCall)
    {
        var root = FindRepositoryRoot();
        var folder = Path.Combine(root, "src", plugin);
        var source = File.ReadAllText(Path.Combine(folder, implementationFile));
        var project = File.ReadAllText(Path.Combine(folder, plugin + ".csproj"));
        var hostProject = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "LongBetterWindows.Host.csproj"));
        var start = source.IndexOf(
            "public Task OnLanguageChangedAsync(",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            nextMarker,
            start,
            StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        var languageBody = source[start..end];
        Assert.Contains("IPluginLanguageLifecycle", source);
        Assert.Contains("ApplyLocalization", languageBody);
        Assert.DoesNotContain(prohibitedCall, languageBody);
        Assert.DoesNotContain("RegisterAsync", languageBody);
        Assert.DoesNotContain("Show();", languageBody);
        Assert.Contains("PluginLocalization Include=\"i18n\\*.json\"", project);
        Assert.Contains(plugin + "\\i18n", hostProject);
    }

    [Fact]
    public void SharedHotkeySettings_PreservesLocalizedStatusAndRealCallback()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "LongBetterWindows.PluginSdk.Wpf",
            "HotkeySettingsControl.cs"));

        Assert.Contains("HotkeySettingsLocalization", source);
        Assert.Contains("ApplyLocalization(", source);
        Assert.Contains("_hotkeyCallback", source);
        Assert.Contains(
            "previousHotkey, newHotkey, _pluginId, _hotkeyCallback",
            source);
        Assert.Contains("RenderStatus();", source);
        Assert.Contains("\"LongTextBox\"", source);
        Assert.Contains("\"LongButton.Primary\"", source);
        Assert.Contains("\"Long.Brush.State.Danger\"", source);
        Assert.Contains("\"Long.Brush.State.Success\"", source);
        Assert.DoesNotContain("Color.FromRgb", source);
    }

    [Theory]
    [InlineData(
        "QuickLaunchPlugin",
        "QuickLaunchPluginImpl.cs",
        "private LaunchWindowLocalization CreateWindowLocalization",
        "ShowLauncher(")]
    [InlineData(
        "WindowManagerPlugin",
        "WindowManagerPluginImpl.cs",
        "private void ReplaceRegisteredHotkey",
        "SetWindowPos(")]
    public void NativeStatefulPlugin_LocalizesProjectionWithoutExecutingAction(
        string plugin,
        string implementationFile,
        string nextMarker,
        string prohibitedCall)
    {
        var root = FindRepositoryRoot();
        var folder = Path.Combine(root, "src", plugin);
        var source = File.ReadAllText(Path.Combine(folder, implementationFile));
        var project = File.ReadAllText(Path.Combine(folder, plugin + ".csproj"));
        var hostProject = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "LongBetterWindows.Host.csproj"));
        var start = source.IndexOf(
            "public Task OnLanguageChangedAsync(",
            StringComparison.Ordinal);
        var end = source.IndexOf(nextMarker, start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        var languageBody = source[start..end];
        Assert.Contains("IPluginLanguageLifecycle", source);
        Assert.Contains("ApplyLocalization", languageBody);
        Assert.DoesNotContain(prohibitedCall, languageBody);
        Assert.DoesNotContain("RegisterAsync", languageBody);
        Assert.Contains("PluginLocalization Include=\"i18n\\*.json\"", project);
        Assert.Contains(plugin + "\\i18n", hostProject);
    }

    [Fact]
    public void QuickLaunch_LocalizesStableResultProjectionWithoutRescanning()
    {
        var root = FindRepositoryRoot();
        var implementation = File.ReadAllText(Path.Combine(
            root,
            "src",
            "QuickLaunchPlugin",
            "QuickLaunchPluginImpl.cs"));
        var window = File.ReadAllText(Path.Combine(
            root,
            "src",
            "QuickLaunchPlugin",
            "LaunchWindow.xaml.cs"));
        var start = window.IndexOf(
            "public void ApplyLocalization(",
            StringComparison.Ordinal);
        var end = window.IndexOf(
            "private void ApplyResultsProjection",
            start,
            StringComparison.Ordinal);
        var languageBody = window[start..end];

        Assert.Contains("Category = \"application\"", window);
        Assert.Contains("Category = \"calculation\"", window);
        Assert.Contains("category == \"calculation\"", implementation);
        Assert.Contains("_targetPolicy.Validate(category, target)", implementation);
        Assert.DoesNotContain("Category = \"应用\"", window);
        Assert.DoesNotContain("entry.Category == \"计算\"", implementation);
        Assert.Contains("ApplyResultsProjection();", languageBody);
        Assert.DoesNotContain("SearchFiles(", languageBody);
        Assert.DoesNotContain("SearchContent(", languageBody);
        Assert.DoesNotContain("SearchBox_TextChanged(", languageBody);
    }

    [Fact]
    public void WindowManager_SettingsRetainRealTopmostCallback()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "WindowManagerPlugin",
            "WindowManagerPluginImpl.cs"));

        Assert.Matches(
            @"CreateSettingsLocalization\(\),\s*ToggleTopmost\)",
            source);
        Assert.Contains("ReplaceRegisteredHotkey(", source);
        Assert.Contains("_guide?.ApplyLocalization", source);
    }

    private static JsonDocument ReadResource(
        string directory,
        PluginLocalizationPreference localization,
        string language)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(
            directory,
            localization.Resources[language])));

    private static string? ReadSetting(
        IReadOnlyDictionary<string, object>? settings,
        string key)
    {
        if (settings?.TryGetValue(key, out var value) != true)
            return null;
        return value is JsonElement element
            ? element.GetRawText()
            : value?.ToString()?.ToLowerInvariant();
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
