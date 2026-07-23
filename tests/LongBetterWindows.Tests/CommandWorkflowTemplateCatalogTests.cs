using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class CommandWorkflowTemplateCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "long-workflow-template-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task List_MissingCatalogIsEmptyAndIsNotCreated()
    {
        var catalog = Catalog();

        var result = await catalog.ListAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Templates);
        Assert.Empty(result.Issues);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task List_ReadsValidTemplatesAndIsolatesInvalidDocuments()
    {
        Directory.CreateDirectory(_root);
        await WriteTemplateAsync(
            "valid.workflow.json",
            Workflow("template.valid"),
            new WorkflowDocumentSource(WorkflowDocumentSourceKind.Imported, "official.templates"));
        await File.WriteAllTextAsync(
            Path.Combine(_root, "broken.workflow.json"),
            "not-json");
        var managedRoot = Path.Combine(_root, "managed");
        var catalog = Catalog(managedRoot);

        var result = await catalog.ListAsync();

        Assert.True(result.IsSuccess, result.Error);
        var template = Assert.Single(result.Templates);
        Assert.Equal("valid.workflow.json", template.Key);
        Assert.Equal("template.valid", template.Id);
        Assert.Equal("official.templates", template.Source.SourceId);
        Assert.Equal(WorkflowDocumentTrustLevel.Untrusted, template.TrustLevel);
        Assert.Single(result.Issues);
        Assert.False(Directory.Exists(managedRoot));
    }

    [Fact]
    public async Task List_DuplicateWorkflowIdsAreReportedAndExcluded()
    {
        Directory.CreateDirectory(_root);
        await WriteTemplateAsync("first.workflow.json", Workflow("template.same"));
        await WriteTemplateAsync("second.workflow.json", Workflow("template.same"));

        var result = await Catalog().ListAsync();

        Assert.Single(result.Templates);
        var issue = Assert.Single(result.Issues);
        Assert.Contains("duplicated", issue.Error);
    }

    [Fact]
    public async Task List_RejectsCatalogAboveBoundBeforeParsingDocuments()
    {
        Directory.CreateDirectory(_root);
        for (var index = 0;
            index <= CommandWorkflowTemplateCatalog.MaximumTemplateCount;
            index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_root, $"{index:D3}.workflow.json"),
                string.Empty);
        }

        var result = await Catalog().ListAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains(
            CommandWorkflowTemplateCatalog.MaximumTemplateCount.ToString(),
            result.Error);
        Assert.Empty(result.Templates);
    }

    [Fact]
    public async Task Open_RejectsTraversalAndDefinitionChangedAfterListing()
    {
        Directory.CreateDirectory(_root);
        await WriteTemplateAsync("stable.workflow.json", Workflow("template.stable"));
        var catalog = Catalog();
        var listed = Assert.Single((await catalog.ListAsync()).Templates);

        var traversal = await catalog.OpenAsync(
            "..\\outside.workflow.json",
            listed.DefinitionSha256);
        await WriteTemplateAsync(
            "stable.workflow.json",
            Workflow("template.stable") with { Name = "Changed" });
        var changed = await catalog.OpenAsync(listed.Key, listed.DefinitionSha256);

        Assert.False(traversal.IsSuccess);
        Assert.Contains("key is invalid", traversal.Error);
        Assert.False(changed.IsSuccess);
        Assert.Contains("changed after it was listed", changed.Error);
    }

    [Fact]
    public async Task Session_PreviewTemplateDoesNotReplaceDraftUntilAdopted()
    {
        Directory.CreateDirectory(_root);
        await WriteTemplateAsync("adopt.workflow.json", Workflow("template.adopt"));
        var repository = new CommandWorkflowRepository(
            Path.Combine(_root, "managed"),
            "local-user");
        var catalog = new CommandWorkflowTemplateCatalog(_root, repository);
        var session = new CommandWorkflowEditorSession(Registry(), repository, catalog);
        session.StartNew("workflow.current", "Current");
        var summary = Assert.Single((await session.ListTemplatesAsync()).Templates);

        var review = await session.PreviewTemplateAsync(
            summary.Key,
            summary.DefinitionSha256);

        Assert.True(review.IsSuccess, review.Error);
        Assert.True(review.Preflight!.IsValid);
        Assert.Equal("workflow.current", session.State.Draft!.Id);
        Assert.True(session.AdoptImport(review));
        Assert.Equal("template.adopt", session.State.Draft!.Id);
        Assert.Null(session.State.ExistingDefinitionSha256);
        Assert.True(session.State.IsDirty);
        Assert.False(Directory.Exists(Path.Combine(_root, "managed")));
    }

    private CommandWorkflowTemplateCatalog Catalog(string? managedRoot = null)
        => new(
            _root,
            new CommandWorkflowRepository(
                managedRoot ?? Path.Combine(_root, "managed"),
                "local-user"));

    private async Task WriteTemplateAsync(
        string fileName,
        CommandWorkflowDefinition workflow,
        WorkflowDocumentSource? source = null)
    {
        await File.WriteAllTextAsync(
            Path.Combine(_root, fileName),
            CommandWorkflowDocumentCodec.Serialize(
                workflow,
                source ?? new WorkflowDocumentSource(
                    WorkflowDocumentSourceKind.Imported,
                    "test.templates")));
    }

    private static CommandWorkflowDefinition Workflow(string id)
        => new(
            id,
            "Template",
            WorkflowFailureMode.Stop,
            [
                new CommandWorkflowStep(
                    "step",
                    WorkflowStepEffect.ReadOnly,
                    new WorkflowCommand(
                        "editor:first",
                        new PluginCommandInvocation { CommandId = "first" })),
            ]);

    private static PluginRegistry Registry()
    {
        var registry = new PluginRegistry();
        registry.Register(
            new PluginManifest
            {
                Id = "editor",
                Name = "Editor",
                Version = "1.0.0",
                EntryPoint = "editor.dll",
                Commands =
                [
                    new PluginCommand
                    {
                        Id = "first",
                        Title = "First",
                        AcceptedInputs = [AcceptedInputType.None],
                    },
                ],
            },
            new TestPlugin(),
            null,
            "/editor");
        return registry;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class TestPlugin : ILongPlugin
    {
        public string Id => "editor";
        public string Name => "Editor";
        public string Version => "1.0.0";
        public PluginState State => PluginState.Loaded;
        public Task<bool> InitializeAsync(IHostApi host) => Task.FromResult(true);
        public Task<bool> StartAsync() => Task.FromResult(true);
        public Task<bool> StopAsync() => Task.FromResult(true);
    }
}
