using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class ManagedWorkflowSearchProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "long-managed-workflow-search-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SearchAsync_ProjectsSavedPreflightValidWorkflow()
    {
        var registry = Registry();
        var repository = new CommandWorkflowRepository(_root, "local-managed");
        var saved = await repository.SaveAsync(Workflow());
        Assert.True(saved.IsSuccess, saved.Error);
        var provider = new ManagedWorkflowSearchProvider(registry, repository);

        var results = await provider.SearchAsync(Request("整理"));

        var result = Assert.Single(results);
        Assert.Equal("workflow:workflow.organize", result.Id);
        Assert.Equal("整理下载目录", result.Title);
        Assert.Equal("组合动作 · 1 步", result.Source);
        Assert.Contains("运行前需批准", result.Subtitle);
        Assert.Equal(SearchActionKind.OpenWorkflowReview, result.PrimaryAction.Kind);
        Assert.Equal("workflow.organize", result.PrimaryAction.Target);
        Assert.Equal(
            new CommandWorkflowPlanner(registry).Preflight(Workflow()).Fingerprint,
            result.PrimaryAction.ExpectedStateFingerprint);
        Assert.True(result.CanPin);
    }

    [Fact]
    public async Task SearchAsync_OmitsWorkflowWhenCurrentPluginCatalogFailsPreflight()
    {
        var repository = new CommandWorkflowRepository(_root, "local-managed");
        var saved = await repository.SaveAsync(Workflow());
        Assert.True(saved.IsSuccess, saved.Error);
        var provider = new ManagedWorkflowSearchProvider(new PluginRegistry(), repository);

        var results = await provider.SearchAsync(Request(string.Empty));

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_DoesNotPublishUnreadableManagedDocuments()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(
            Path.Combine(_root, "broken.workflow.json"),
            "{ not-json }");
        var provider = new ManagedWorkflowSearchProvider(Registry(),
            new CommandWorkflowRepository(_root, "local-managed"));

        var results = await provider.SearchAsync(Request("workflow"));

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_EmptyQueryPrioritizesPinnedWorkflow()
    {
        var repository = new CommandWorkflowRepository(_root, "local-managed");
        Assert.True((await repository.SaveAsync(Workflow())).IsSuccess);
        var provider = new ManagedWorkflowSearchProvider(Registry(), repository);
        var request = Request(string.Empty) with
        {
            PinnedResultIds = ["workflow:workflow.organize"],
        };

        var result = Assert.Single(await provider.SearchAsync(request));

        Assert.True(result.Score > 160);
        Assert.Equal("workflow:workflow.organize", result.Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static SearchRequest Request(string query)
        => new(query, ContextSnapshot.Empty, MaxResults: 10);

    private static CommandWorkflowDefinition Workflow()
        => new(
            "workflow.organize",
            "整理下载目录",
            WorkflowFailureMode.Stop,
            [
                new CommandWorkflowStep(
                    "inspect",
                    WorkflowStepEffect.ReadOnly,
                    new WorkflowCommand(
                        "files:inspect",
                        new PluginCommandInvocation
                        {
                            CommandId = "inspect",
                            InputType = AcceptedInputType.None,
                        })),
            ]);

    private static PluginRegistry Registry()
    {
        var registry = new PluginRegistry();
        registry.Register(
            new PluginManifest
            {
                Id = "files",
                Name = "Files",
                Version = "1.0.0",
                EntryPoint = "files.dll",
                Capabilities = ["files.read"],
                Commands =
                [
                    new PluginCommand
                    {
                        Id = "inspect",
                        Title = "Inspect",
                        AcceptedInputs = [AcceptedInputType.None],
                    },
                ],
            },
            new TestPlugin(),
            null,
            directory: "/files");
        return registry;
    }

    private sealed class TestPlugin : ILongPlugin
    {
        public string Id => "files";
        public string Name => "Files";
        public string Version => "1.0.0";
        public PluginState State => PluginState.Loaded;
        public Task<bool> InitializeAsync(IHostApi host) => Task.FromResult(true);
        public Task<bool> StartAsync() => Task.FromResult(true);
        public Task<bool> StopAsync() => Task.FromResult(true);
    }
}
