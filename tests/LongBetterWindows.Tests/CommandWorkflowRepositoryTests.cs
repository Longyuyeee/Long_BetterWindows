using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class CommandWorkflowRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "long-workflow-repository-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoadManaged_FirstVersionUsesLocalProvenance()
    {
        var repository = Repository();

        var saved = await repository.SaveAsync(Workflow("workflow.local"));
        var loaded = await repository.LoadManagedAsync("workflow.local");

        Assert.True(saved.IsSuccess, saved.Error);
        Assert.True(loaded.IsSuccess, loaded.Error);
        Assert.Equal(saved.DefinitionSha256, loaded.DefinitionSha256);
        Assert.Equal(WorkflowDocumentTrustLevel.LocalManaged, loaded.TrustLevel);
        Assert.EndsWith("workflow.local.workflow.json", saved.Path);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task Save_SensitiveInputsRequireExplicitApproval()
    {
        var repository = Repository();
        var workflow = Workflow("workflow.sensitive", text: "private input");

        var rejected = await repository.SaveAsync(workflow);
        var saved = await repository.SaveAsync(
            workflow,
            new CommandWorkflowSaveOptions(AllowSensitiveInputs: true));

        Assert.False(rejected.IsSuccess);
        Assert.Contains("sensitive-input", rejected.Error);
        Assert.True(saved.IsSuccess, saved.Error);
    }

    [Fact]
    public async Task Save_UpdateRequiresCurrentDefinitionHashAndRejectsStaleWriter()
    {
        var repository = Repository();
        var initial = await repository.SaveAsync(Workflow("workflow.update") with { Name = "Initial" });
        var missingHash = await repository.SaveAsync(Workflow("workflow.update") with { Name = "Changed" });
        var updated = await repository.SaveAsync(
            Workflow("workflow.update") with { Name = "Changed" },
            new CommandWorkflowSaveOptions(
                ExpectedExistingDefinitionSha256: initial.DefinitionSha256));
        var stale = await repository.SaveAsync(
            Workflow("workflow.update") with { Name = "Stale" },
            new CommandWorkflowSaveOptions(
                ExpectedExistingDefinitionSha256: initial.DefinitionSha256));
        var loaded = await repository.LoadManagedAsync("workflow.update");

        Assert.False(missingHash.IsSuccess);
        Assert.True(updated.IsSuccess, updated.Error);
        Assert.False(stale.IsSuccess);
        Assert.Equal("Changed", loaded.Workflow!.Name);
    }

    [Fact]
    public async Task Save_InvalidExpectedHashReturnsFailure()
    {
        var repository = Repository();
        await repository.SaveAsync(Workflow("workflow.invalid-hash"));

        var result = await repository.SaveAsync(
            Workflow("workflow.invalid-hash") with { Name = "Changed" },
            new CommandWorkflowSaveOptions(ExpectedExistingDefinitionSha256: "not-a-hash"));

        Assert.False(result.IsSuccess);
        Assert.Contains("64 hexadecimal", result.Error);
    }

    [Fact]
    public async Task Save_ConcurrentUpdatesWithSameHashAllowOnlyOneWriter()
    {
        var repository = Repository();
        var initial = await repository.SaveAsync(Workflow("workflow.concurrent"));

        var updates = await Task.WhenAll(
            repository.SaveAsync(
                Workflow("workflow.concurrent") with { Name = "First" },
                new CommandWorkflowSaveOptions(
                    ExpectedExistingDefinitionSha256: initial.DefinitionSha256)),
            repository.SaveAsync(
                Workflow("workflow.concurrent") with { Name = "Second" },
                new CommandWorkflowSaveOptions(
                    ExpectedExistingDefinitionSha256: initial.DefinitionSha256)));

        Assert.Single(updates, result => result.IsSuccess);
        Assert.Single(updates, result => !result.IsSuccess);
    }

    [Fact]
    public async Task Save_InvalidIdentifierCannotEscapeManagedRoot()
    {
        var repository = Repository();

        var result = await repository.SaveAsync(Workflow("../escape"));

        Assert.False(result.IsSuccess);
        Assert.False(File.Exists(Path.Combine(Directory.GetParent(_root)!.FullName, "escape.workflow.json")));
    }

    [Fact]
    public async Task Import_ExternalLocalClaimRemainsUntrustedAndIsNotAdopted()
    {
        Directory.CreateDirectory(_root);
        var importPath = Path.Combine(_root, "external.json");
        await File.WriteAllTextAsync(
            importPath,
            CommandWorkflowDocumentCodec.Serialize(
                Workflow("workflow.external"),
                new WorkflowDocumentSource(WorkflowDocumentSourceKind.LocalManaged, "fake-local")));
        var managedRoot = Path.Combine(_root, "managed");
        var repository = new CommandWorkflowRepository(managedRoot, "local-user");

        var imported = await repository.ImportAsync(importPath);

        Assert.True(imported.IsSuccess, imported.Error);
        Assert.Equal(WorkflowDocumentTrustLevel.Untrusted, imported.TrustLevel);
        Assert.False(Directory.Exists(managedRoot));
    }

    [Fact]
    public async Task Import_AllowlistedDefinitionIsTrustedButNotAdopted()
    {
        Directory.CreateDirectory(_root);
        var workflow = Workflow("workflow.official");
        var hash = CommandWorkflowDocumentCodec.ComputeDefinitionSha256(workflow);
        var policy = new WorkflowSourceTrustPolicy(
            new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["official.templates"] = new[] { hash },
            });
        var importPath = Path.Combine(_root, "official.json");
        await File.WriteAllTextAsync(
            importPath,
            CommandWorkflowDocumentCodec.Serialize(
                workflow,
                new WorkflowDocumentSource(
                    WorkflowDocumentSourceKind.Imported,
                    "official.templates")));
        var managedRoot = Path.Combine(_root, "managed");
        var repository = new CommandWorkflowRepository(managedRoot, "local-user", policy);

        var imported = await repository.ImportAsync(importPath);

        Assert.Equal(WorkflowDocumentTrustLevel.TrustedSource, imported.TrustLevel);
        Assert.False(Directory.Exists(managedRoot));
    }

    [Fact]
    public async Task Import_OversizedDocumentIsRejectedBeforeParsing()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "oversized.json");
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(CommandWorkflowRepository.MaximumDocumentBytes + 1);
        }

        var result = await Repository().ImportAsync(path);

        Assert.False(result.IsSuccess);
        Assert.Contains("maximum size", result.Error);
    }

    [Fact]
    public async Task ExportManaged_RewritesSourceAndPreservesDefinitionHash()
    {
        var repository = Repository();
        var saved = await repository.SaveAsync(Workflow("workflow.export"));
        var exportRoot = Path.Combine(Path.GetTempPath(), "long-workflow-exports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exportRoot);
        var exportPath = Path.Combine(exportRoot, "shared.workflow.json");
        try
        {
            var exported = await repository.ExportManagedAsync(
                "workflow.export",
                saved.DefinitionSha256,
                exportPath);
            var imported = await repository.ImportAsync(exportPath);

            Assert.True(exported.IsSuccess, exported.Error);
            Assert.True(imported.IsSuccess, imported.Error);
            Assert.Equal(saved.DefinitionSha256, imported.DefinitionSha256);
            Assert.Equal(WorkflowDocumentSourceKind.Imported, imported.Source!.Kind);
            Assert.Equal("local-user:export", imported.Source.SourceId);
            Assert.Equal(WorkflowDocumentTrustLevel.Untrusted, imported.TrustLevel);
            Assert.Empty(Directory.GetFiles(exportRoot, "*.tmp"));
        }
        finally
        {
            Directory.Delete(exportRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExportManaged_RejectsStaleHashWithoutCreatingTarget()
    {
        var repository = Repository();
        await repository.SaveAsync(Workflow("workflow.stale-export"));
        Directory.CreateDirectory(_root);
        var exportPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.workflow.json");

        var result = await repository.ExportManagedAsync(
            "workflow.stale-export",
            new string('f', 64),
            exportPath);

        Assert.False(result.IsSuccess);
        Assert.Contains("stale export", result.Error);
        Assert.False(File.Exists(exportPath));
    }

    [Fact]
    public async Task ExportManaged_RejectsDestinationInsideManagedRoot()
    {
        var repository = Repository();
        var saved = await repository.SaveAsync(Workflow("workflow.internal-export"));
        var exportPath = Path.Combine(_root, "shared.json");

        var result = await repository.ExportManagedAsync(
            "workflow.internal-export",
            saved.DefinitionSha256,
            exportPath);

        Assert.False(result.IsSuccess);
        Assert.Contains("outside", result.Error);
        Assert.False(File.Exists(exportPath));
    }

    [Fact]
    public async Task LoadManaged_MalformedUtf8IsRejected()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "workflow.bad.workflow.json");
        await File.WriteAllBytesAsync(path, new byte[] { 0xff, 0xfe, 0xfd });

        var result = await Repository().LoadManagedAsync("workflow.bad");

        Assert.False(result.IsSuccess);
        Assert.Contains("could not be read", result.Error);
    }

    private CommandWorkflowRepository Repository()
        => new(_root, "local-user");

    private static CommandWorkflowDefinition Workflow(string id, string? text = null)
        => new(
            id,
            "Workflow",
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
                            Text = text,
                        })),
            ]);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
