using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class I18nServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "LongBetterWindows.I18n.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly string _resources;
    private readonly string _settings;

    public I18nServiceTests()
    {
        _resources = Path.Combine(_root, "i18n");
        _settings = Path.Combine(_root, "settings", "language.json");
        Directory.CreateDirectory(_resources);
        File.WriteAllText(
            Path.Combine(_resources, "zh-CN.json"),
            """{"_lang":"简体中文","hello":"你好","fallback":"中文回退"}""");
        File.WriteAllText(
            Path.Combine(_resources, "en-US.json"),
            """{"_lang":"English","hello":"Hello"}""");
    }

    [Fact]
    public void Initialize_MergesTheDefaultLanguageForMissingSelectedKeys()
    {
        var service = new I18nService(_resources, _settings);

        service.Initialize("en-US");

        Assert.Equal("en-US", service.CurrentLanguage);
        Assert.Equal("Hello", service.T("hello"));
        Assert.Equal("中文回退", service.T("fallback"));
        Assert.Equal("missing", service.T("missing"));
    }

    [Fact]
    public void SetLanguage_PersistsAtomicallyAndRaisesOneChange()
    {
        var service = new I18nService(_resources, _settings);
        service.Initialize("zh-CN");
        var changes = new List<string>();
        service.LanguageChanged += changes.Add;

        service.SetLanguage("en-US");
        service.SetLanguage("en-US");

        Assert.Equal(new[] { "en-US" }, changes);
        Assert.False(File.Exists(_settings + ".tmp"));
        using var settings = JsonDocument.Parse(File.ReadAllText(_settings));
        Assert.Equal(
            "en-US",
            settings.RootElement.GetProperty("language").GetString());

        var reloaded = new I18nService(_resources, _settings);
        reloaded.Initialize();
        Assert.Equal("en-US", reloaded.CurrentLanguage);
    }

    [Fact]
    public void ApplyTo_UpdatesDynamicResourceKeys()
    {
        var service = new I18nService(_resources, _settings);
        service.Initialize("en-US");
        var resources = new ResourceDictionary();

        service.ApplyTo(resources);

        Assert.Equal("Hello", resources["i18n.hello"]);
        Assert.Equal("中文回退", resources["i18n.fallback"]);
        Assert.Equal("en-US", resources["i18n.currentLanguage"]);
    }

    [Fact]
    public void UnsupportedLanguage_IsRejectedWithoutChangingTheCurrentLanguage()
    {
        var service = new I18nService(_resources, _settings);
        service.Initialize("zh-CN");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.SetLanguage("fr-FR"));
        Assert.Equal("zh-CN", service.CurrentLanguage);
        Assert.False(File.Exists(_settings));
    }

    [Fact]
    public void RepositoryLanguageFiles_HaveMatchingNonMetadataKeys()
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "i18n");
        var chinese = ReadKeys(Path.Combine(directory, "zh-CN.json"));
        var english = ReadKeys(Path.Combine(directory, "en-US.json"));

        Assert.Equal(chinese, english);
        Assert.Contains("nav.system", chinese);
        Assert.Contains("overview.welcome.title", chinese);
        Assert.Contains("overview.status.releasePending", chinese);
        Assert.Contains("plugins.installedCount", chinese);
        Assert.Contains("plugins.status.runningVersion", chinese);
        Assert.Contains("market.confirm.highTrustWarning", chinese);
        Assert.Contains("market.status.downloadRetried", chinese);
        Assert.Contains("settings.language.title", chinese);
        Assert.Contains("system.sparse.title", chinese);
        Assert.Contains("workflow.editor.name", chinese);
        Assert.Contains("workflow.preflight.permissionCount", chinese);
        Assert.Contains("workflow.import.reviewTemplate", chinese);
        Assert.Contains("workflow.import.confirm.replaceDraft", chinese);
        Assert.Contains("workflow.template.itemDetail", chinese);
        Assert.Contains("workflow.invocation.sensitiveHelp", chinese);
        Assert.Contains("workflow.binding.error.schemaRequired", chinese);
    }

    [Fact]
    public void ToolCenterOverview_UsesDynamicLanguageResources()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "ToolCenterControl.xaml"));

        Assert.Contains("i18n.overview.welcome.title", xaml);
        Assert.Contains("i18n.overview.metrics.plugins", xaml);
        Assert.Contains("i18n.overview.action.title", xaml);
        Assert.Contains("i18n.overview.status.releasePending", xaml);
        Assert.DoesNotContain("阶段 5 本机基线通过", xaml);
    }

    [Fact]
    public void PluginManagement_UsesDynamicLanguageResourcesAndResponsiveToolbar()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "PluginManagementControl.xaml"));
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "PluginManagementControl.xaml.cs"));

        Assert.Contains("i18n.plugins.installed", xaml);
        Assert.Contains("i18n.plugins.search", xaml);
        Assert.Contains("i18n.plugins.openCapabilities", xaml);
        Assert.Contains("ApplyResponsiveLayout", source);
        Assert.Contains("plugins.status.runningVersion", source);
        Assert.DoesNotContain("暂无已安装插件", xaml);
    }

    [Fact]
    public void Marketplace_UsesDynamicResourcesAndCompactMasterDetailLayout()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "MarketplaceControl.xaml"));
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "MarketplaceControl.xaml.cs"));
        var presentation = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Interaction",
            "MarketplacePresentation.cs"));

        Assert.Contains("i18n.market.hero.title", xaml);
        Assert.Contains("i18n.market.confirm.highTrustWarning", xaml);
        Assert.Contains("MarketBackButton", xaml);
        Assert.Contains("ApplyResponsiveLayout", source);
        Assert.Contains("FormatPermissionDiff", source);
        Assert.DoesNotContain("StateLabel", presentation);
        Assert.DoesNotContain("使用当前稳定协议", presentation);
    }

    [Fact]
    public void WorkflowEditor_FoundationUsesDynamicResourcesAndRefreshesOnLanguageChange()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "WorkflowEditorControl.xaml"));
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "WorkflowEditorControl.xaml.cs"));

        Assert.Contains("i18n.workflow.editor.name", xaml);
        Assert.Contains("i18n.workflow.field.failureMode", xaml);
        Assert.Contains("i18n.workflow.step.compensation", xaml);
        Assert.Contains("LanguageChanged += OnLanguageChanged", source);
        Assert.Contains("CreateFailureOptions()", source);
        Assert.Contains("workflow.preflight.permissionCount", source);
    }

    [Fact]
    public void WorkflowImportAndTemplateReview_UseHostLanguageResources()
    {
        var root = FindRepositoryRoot();
        var views = Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views");
        var xaml = File.ReadAllText(Path.Combine(
            views,
            "WorkflowEditorControl.xaml"));
        var source = File.ReadAllText(Path.Combine(
            views,
            "WorkflowEditorControl.xaml.cs"));

        Assert.Contains("Long.Workflow.ImportReview", xaml);
        Assert.Contains("workflow.import.dialog.title", source);
        Assert.Contains("workflow.import.reviewTemplate", source);
        Assert.Contains("workflow.import.preflight.passed", source);
        Assert.Contains("workflow.template.itemDetail", source);
        Assert.Contains("AutomationProperties.SetName", source);
        Assert.DoesNotContain("选择外部工作流", source);
        Assert.DoesNotContain("审查工作流模板", source);
        Assert.DoesNotContain("采用前需审查", source);
    }

    [Fact]
    public void WorkflowInvocationAndBindingEditors_UseHostLanguageResources()
    {
        var root = FindRepositoryRoot();
        var views = Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views");
        var xaml = File.ReadAllText(Path.Combine(
            views,
            "WorkflowInvocationEditorControl.xaml"));
        var invocationSource = File.ReadAllText(Path.Combine(
            views,
            "WorkflowInvocationEditorControl.xaml.cs"));
        var bindingSource = File.ReadAllText(Path.Combine(
            views,
            "WorkflowBindingEditorModel.cs"));

        Assert.Contains("i18n.workflow.invocation.inputType", xaml);
        Assert.Contains("i18n.workflow.invocation.sensitiveHelp", xaml);
        Assert.Contains("i18n.workflow.binding.sourceOutput", xaml);
        Assert.Contains("workflow.invocation.dialog.choosePng", invocationSource);
        Assert.Contains("workflow.constraint.range", invocationSource);
        Assert.Contains("workflow.binding.error.schemaRequired", bindingSource);
        Assert.DoesNotMatch("[一-龥]", xaml);
        Assert.DoesNotMatch("[一-龥]", invocationSource);
        Assert.DoesNotMatch("[一-龥]", bindingSource);
    }

    [Fact]
    public void RepositoryLanguageFiles_HaveMatchingFormatArguments()
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "i18n");
        var chinese = ReadValues(Path.Combine(directory, "zh-CN.json"));
        var english = ReadValues(Path.Combine(directory, "en-US.json"));

        foreach (var key in chinese.Keys.Where(key => key != "_lang"))
        {
            Assert.Equal(
                ReadFormatArguments(chinese[key]),
                ReadFormatArguments(english[key]));
        }
    }

    private static string[] ReadKeys(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Where(key => key != "_lang")
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }

    private static Dictionary<string, string> ReadValues(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.GetString() ?? string.Empty,
            StringComparer.Ordinal);
    }

    private static string[] ReadFormatArguments(string value)
        => Regex.Matches(value, @"\{(?<index>\d+)(?:[^}]*)\}")
            .Select(match => match.Groups["index"].Value)
            .OrderBy(index => index, StringComparer.Ordinal)
            .ToArray();

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

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
