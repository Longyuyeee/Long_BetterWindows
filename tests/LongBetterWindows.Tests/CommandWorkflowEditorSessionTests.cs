using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class CommandWorkflowEditorSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "long-workflow-editor-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void StartNew_AddAndReorderStepsMaintainsDraftState()
    {
        var session = Session();
        session.StartNew("workflow.editor", "Editor workflow");

        Assert.True(session.AddStep("editor:first"));
        Assert.True(session.AddStep("editor:second"));
        Assert.True(session.MoveStep("step-2", -1));

        Assert.True(session.State.IsDirty);
        Assert.Equal("step-2", session.State.Draft!.Steps[0].Id);
        Assert.Equal("editor:second", session.State.Draft.Steps[0].Command!.CommandKey);
    }

    [Fact]
    public void MutatingStep_RequiresCompensationBeforeDraftCanSave()
    {
        var session = Session();
        session.StartNew("workflow.mutating", "Mutating workflow");
        session.SetFailureMode(WorkflowFailureMode.Compensate);
        session.AddStep("editor:first", WorkflowStepEffect.Mutating);

        Assert.False(session.State.CanSave);
        Assert.Contains(session.State.Preflight!.Issues, issue => issue.Contains("requires compensation"));

        Assert.True(session.UpdateStep(
            "step-1",
            WorkflowStepEffect.Mutating,
            "editor:first",
            "editor:undo"));
        Assert.True(session.State.CanSave);
    }

    [Fact]
    public async Task SaveAndLoad_RetainsOptimisticConcurrencyHash()
    {
        var session = Session();
        session.StartNew("workflow.saved", "Saved workflow");
        session.AddStep("editor:first");

        var saved = await session.SaveAsync(allowSensitiveInputs: false);
        var loadedSession = Session();
        var loaded = await loadedSession.LoadAsync("workflow.saved");

        Assert.True(saved.IsSuccess, saved.Error);
        Assert.True(loaded);
        Assert.False(loadedSession.State.IsDirty);
        Assert.Equal(saved.DefinitionSha256, loadedSession.State.ExistingDefinitionSha256);
    }

    [Fact]
    public async Task StartNew_AfterLoadingClearsExistingDefinitionHash()
    {
        var session = Session();
        session.StartNew("workflow.first", "First workflow");
        session.AddStep("editor:first");
        await session.SaveAsync(allowSensitiveInputs: false);
        await session.LoadAsync("workflow.first");

        session.StartNew("workflow.second", "Second workflow");
        session.AddStep("editor:second");
        var saved = await session.SaveAsync(allowSensitiveInputs: false);

        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal(2, (await new CommandWorkflowRepository(_root, "local-user")
            .ListManagedAsync()).Workflows.Count);
    }

    [Fact]
    public async Task UpdateIdentity_SavedWorkflowCannotChangeId()
    {
        var session = Session();
        session.StartNew("workflow.fixed", "Fixed workflow");
        session.AddStep("editor:first");
        await session.SaveAsync(allowSensitiveInputs: false);

        session.UpdateIdentity("workflow.renamed", "Renamed workflow");

        Assert.Equal("workflow.fixed", session.State.Draft!.Id);
        Assert.Equal("Fixed workflow", session.State.Draft.Name);
        Assert.Contains("cannot be changed", session.State.Error);
    }

    [Fact]
    public async Task DeleteCurrent_RemovesSavedWorkflowAndClearsDraft()
    {
        var session = Session();
        session.StartNew("workflow.delete", "Delete workflow");
        session.AddStep("editor:first");
        await session.SaveAsync(allowSensitiveInputs: false);

        var result = await session.DeleteCurrentAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(session.State.Draft);
        Assert.Empty((await new CommandWorkflowRepository(_root, "local-user")
            .ListManagedAsync()).Workflows);
    }

    [Fact]
    public async Task DeleteManaged_RejectsStaleDefinitionHash()
    {
        var session = Session();
        session.StartNew("workflow.stale-delete", "Stale delete");
        session.AddStep("editor:first");
        var saved = await session.SaveAsync(allowSensitiveInputs: false);
        var repository = new CommandWorkflowRepository(_root, "local-user");

        var result = await repository.DeleteManagedAsync(
            "workflow.stale-delete",
            new string('f', 64));

        Assert.False(result.IsSuccess);
        Assert.Contains("stale delete", result.Error);
        Assert.Equal(saved.DefinitionSha256, (await repository.LoadManagedAsync(
            "workflow.stale-delete")).DefinitionSha256);
    }

    [Fact]
    public async Task LoadedDraft_NoOpControlUpdatesRemainClean()
    {
        var session = Session();
        session.StartNew("workflow.clean", "Clean workflow");
        session.AddStep("editor:first");
        await session.SaveAsync(allowSensitiveInputs: false);
        await session.LoadAsync("workflow.clean");

        session.UpdateIdentity("workflow.clean", "Clean workflow");
        session.SetFailureMode(WorkflowFailureMode.Stop);
        session.UpdateStep("step-1", WorkflowStepEffect.ReadOnly, "editor:first", null);

        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public void AddStep_EnforcesMaximumBeforeDraftMutation()
    {
        var session = Session();
        session.StartNew("workflow.maximum", "Maximum workflow");
        for (var index = 0; index < CommandWorkflowPlanner.MaximumStepCount; index++)
            Assert.True(session.AddStep("editor:first"));

        var added = session.AddStep("editor:second");

        Assert.False(added);
        Assert.Equal(CommandWorkflowPlanner.MaximumStepCount, session.State.Draft!.Steps.Count);
        Assert.Contains("cannot contain more", session.State.Error);
    }

    [Fact]
    public async Task Save_RejectsDraftWhoseCommandDisappeared()
    {
        var registry = Registry();
        var session = Session(registry);
        session.StartNew("workflow.stale", "Stale workflow");
        session.AddStep("editor:first");
        registry.Unregister("editor");

        var result = await session.SaveAsync(allowSensitiveInputs: false);

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task ListManaged_ReturnsValidDocumentsAndReportsInvalidFiles()
    {
        var repository = new CommandWorkflowRepository(_root, "local-user");
        var session = Session(repository: repository);
        session.StartNew("workflow.listed", "Listed workflow");
        session.AddStep("editor:first");
        await session.SaveAsync(allowSensitiveInputs: false);
        await File.WriteAllTextAsync(Path.Combine(_root, "broken.workflow.json"), "not-json");

        var result = await repository.ListManagedAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(result.Workflows);
        Assert.Equal("workflow.listed", result.Workflows[0].Id);
        Assert.Single(result.Issues);
    }

    private CommandWorkflowEditorSession Session(
        PluginRegistry? registry = null,
        CommandWorkflowRepository? repository = null)
        => new(
            registry ?? Registry(),
            repository ?? new CommandWorkflowRepository(_root, "local-user"));

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
                    Command("first"),
                    Command("second"),
                    Command("undo"),
                ],
            },
            new TestPlugin(),
            null,
            "/editor");
        return registry;
    }

    private static PluginCommand Command(string id)
        => new()
        {
            Id = id,
            Title = id,
            AcceptedInputs = [AcceptedInputType.None],
        };

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
