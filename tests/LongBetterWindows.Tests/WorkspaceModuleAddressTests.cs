using System.IO;
using System.Windows;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class WorkspaceModuleAddressTests : IDisposable
{
    private readonly string _workflowRoot = Path.Combine(
        Path.GetTempPath(),
        "long-workspace-address-tests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("management:root", "management:root")]
    [InlineData("MARKETPLACE:CATALOG", "marketplace:catalog")]
    [InlineData("settings:root", "settings:root")]
    [InlineData("diagnostics:root", "diagnostics:root")]
    [InlineData("developer:root", "developer:root")]
    [InlineData("workflow:workflow.safe", "workflow:workflow.safe")]
    [InlineData("plugin-settings:plugin.safe", "plugin-settings:plugin.safe")]
    public void TryParse_AllowedAddress_ReturnsCanonicalValue(
        string target,
        string expected)
    {
        Assert.True(WorkspaceModuleAddress.TryParse(target, out var address));
        Assert.Equal(expected, address.CanonicalValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown:root")]
    [InlineData("management:other")]
    [InlineData("workflow:")]
    [InlineData("workflow:unsafe/id")]
    [InlineData("workflow:safe:extra")]
    [InlineData("plugin-settings:unsafe id")]
    public void TryParse_UnknownOrMalformedAddress_FailsClosed(string target)
        => Assert.False(WorkspaceModuleAddress.TryParse(target, out _));

    [Fact]
    public async Task ResolveAsync_KnownFixedModule_CreatesProtectedManagementRoot()
    {
        var resolver = Resolver(new PluginRegistry());
        WorkspaceModuleAddress.TryParse("management:root", out var address);

        var result = await resolver.ResolveAsync(address);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Module);
        Assert.False(result.Module.CanClose);
        Assert.Equal("management:root", result.Module.Key.ToString());
    }

    [Fact]
    public async Task ResolvedAddress_OpenedTwice_ActivatesSingleModuleSession()
    {
        var resolver = Resolver(new PluginRegistry());
        WorkspaceModuleAddress.TryParse("management:root", out var rootAddress);
        WorkspaceModuleAddress.TryParse("marketplace:catalog", out var marketAddress);
        var root = await resolver.ResolveAsync(rootAddress);
        var market = await resolver.ResolveAsync(marketAddress);
        var coordinator = new WorkspaceSessionCoordinator(root.Module!);

        var opened = coordinator.Open(market.Module!);
        var reopened = coordinator.Open(market.Module!);

        Assert.Equal(WorkspaceNavigationChangeKind.Opened, opened.Kind);
        Assert.Equal(WorkspaceNavigationChangeKind.None, reopened.Kind);
        Assert.Equal(2, coordinator.State.Modules.Count);
        Assert.Equal(market.Module!.Key, coordinator.State.ActiveModuleKey);
    }

    [Fact]
    public async Task ResolveAsync_ExistingWorkflow_UsesWorkflowIdentityAndTitle()
    {
        var repository = Repository();
        var saved = await repository.SaveAsync(Workflow("workflow.safe"));
        Assert.True(saved.IsSuccess, saved.Error);
        var resolver = new WorkspaceModuleResolver(
            new PluginRegistry(),
            repository);
        WorkspaceModuleAddress.TryParse("workflow:workflow.safe", out var address);

        var result = await resolver.ResolveAsync(address);

        Assert.True(result.IsSuccess);
        Assert.Equal("Workflow title", result.Module!.Title);
        Assert.Equal("workflow:workflow.safe", result.Module.Key.ToString());
    }

    [Fact]
    public async Task ResolveAsync_UnknownWorkflow_DoesNotCreateModule()
    {
        var resolver = Resolver(new PluginRegistry());
        WorkspaceModuleAddress.TryParse("workflow:missing", out var address);

        var result = await resolver.ResolveAsync(address);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Module);
        Assert.Equal(
            WorkspaceModuleResolutionError.ResourceNotFound,
            result.Error);
    }

    [Fact]
    public async Task ResolveAsync_PluginSettings_RequiresKnownSettingsProvider()
    {
        var registry = new PluginRegistry();
        Register(registry, "plain", new object());
        Register(registry, "settings", new SettingsProvider());
        var resolver = Resolver(registry);
        WorkspaceModuleAddress.TryParse("plugin-settings:missing", out var missing);
        WorkspaceModuleAddress.TryParse("plugin-settings:plain", out var plain);
        WorkspaceModuleAddress.TryParse("plugin-settings:settings", out var settings);

        var missingResult = await resolver.ResolveAsync(missing);
        var plainResult = await resolver.ResolveAsync(plain);
        var settingsResult = await resolver.ResolveAsync(settings);

        Assert.Equal(
            WorkspaceModuleResolutionError.ResourceNotFound,
            missingResult.Error);
        Assert.Equal(
            WorkspaceModuleResolutionError.ResourceUnsupported,
            plainResult.Error);
        Assert.True(settingsResult.IsSuccess);
        Assert.Equal("plugin-settings:settings", settingsResult.Module!.Key.ToString());
    }

    [Fact]
    public async Task ResolveAsync_DeferredSettingsPlugin_ActivatesWithoutStarting()
    {
        var registry = new PluginRegistry();
        registry.RegisterDeferred(
            Manifest("deferred"),
            "/deferred",
            _ => Task.FromResult<object?>(new SettingsProvider()));
        var resolver = Resolver(registry);
        WorkspaceModuleAddress.TryParse(
            "plugin-settings:deferred",
            out var address);

        var result = await resolver.ResolveAsync(address);

        Assert.True(result.IsSuccess);
        Assert.True(registry.Get("deferred")!.IsActivated);
        Assert.Equal(PluginState.Loaded, registry.Get("deferred")!.State);
    }

    private WorkspaceModuleResolver Resolver(PluginRegistry registry)
        => new(registry, Repository());

    private CommandWorkflowRepository Repository()
        => new(_workflowRoot, "local-test");

    private static CommandWorkflowDefinition Workflow(string id)
        => new(
            id,
            "Workflow title",
            WorkflowFailureMode.Stop,
            [
                new CommandWorkflowStep(
                    "step",
                    WorkflowStepEffect.ReadOnly,
                    new WorkflowCommand(
                        "plugin:command",
                        new PluginCommandInvocation
                        {
                            CommandId = "command",
                        })),
            ]);

    private static void Register(
        PluginRegistry registry,
        string id,
        object instance)
        => registry.Register(
            Manifest(id),
            instance,
            null,
            $"/{id}");

    private static PluginManifest Manifest(string id)
        => new()
        {
            Id = id,
            Name = id,
            Version = "1.0.0",
            EntryPoint = $"{id}.dll",
        };

    public void Dispose()
    {
        if (Directory.Exists(_workflowRoot))
            Directory.Delete(_workflowRoot, recursive: true);
    }

    private sealed class SettingsProvider : IHasSettingsUI
    {
        public FrameworkElement CreateSettingsUI() => new System.Windows.Controls.Border();
    }
}
