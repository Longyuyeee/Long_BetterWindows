using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class WorkflowErrorContractTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "long-workflow-error-tests-" + Guid.NewGuid().ToString("N"));

    public WorkflowErrorContractTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void WorkflowErrorCodes_HaveStablePublishedValues()
    {
        Assert.Equal(0, (int)WorkflowErrorCode.None);
        Assert.Equal(5000, (int)WorkflowErrorCode.DocumentEmpty);
        Assert.Equal(5001, (int)WorkflowErrorCode.SchemaMissing);
        Assert.Equal(5002, (int)WorkflowErrorCode.SchemaUnsupported);
        Assert.Equal(5003, (int)WorkflowErrorCode.DocumentIncomplete);
        Assert.Equal(5004, (int)WorkflowErrorCode.SourceInvalid);
        Assert.Equal(5005, (int)WorkflowErrorCode.StructureInvalid);
        Assert.Equal(5006, (int)WorkflowErrorCode.JsonInvalid);
        Assert.Equal(5100, (int)WorkflowErrorCode.PathInvalid);
        Assert.Equal(5101, (int)WorkflowErrorCode.DocumentNotFound);
        Assert.Equal(5102, (int)WorkflowErrorCode.ReparsePointRejected);
        Assert.Equal(5103, (int)WorkflowErrorCode.DocumentTooLarge);
        Assert.Equal(5104, (int)WorkflowErrorCode.ReadFailed);
        Assert.Equal(5105, (int)WorkflowErrorCode.StorageUnavailable);
        Assert.Equal(5110, (int)WorkflowErrorCode.SensitiveInputApprovalRequired);
        Assert.Equal(5111, (int)WorkflowErrorCode.ValidationFailed);
        Assert.Equal(5112, (int)WorkflowErrorCode.ExistingHashRequired);
        Assert.Equal(5113, (int)WorkflowErrorCode.ExpectedHashInvalid);
        Assert.Equal(5114, (int)WorkflowErrorCode.StaleWriteConflict);
        Assert.Equal(5115, (int)WorkflowErrorCode.ExpectedVersionMissing);
        Assert.Equal(5116, (int)WorkflowErrorCode.SaveFailed);
        Assert.Equal(5117, (int)WorkflowErrorCode.DeleteFailed);
        Assert.Equal(5118, (int)WorkflowErrorCode.ExportPathInvalid);
        Assert.Equal(5119, (int)WorkflowErrorCode.ExportLocationRejected);
        Assert.Equal(5120, (int)WorkflowErrorCode.ExportFailed);
        Assert.Equal(5200, (int)WorkflowErrorCode.TemplateCatalogUnavailable);
        Assert.Equal(5201, (int)WorkflowErrorCode.TemplateLimitExceeded);
        Assert.Equal(5202, (int)WorkflowErrorCode.TemplateDuplicateId);
        Assert.Equal(5203, (int)WorkflowErrorCode.TemplateKeyInvalid);
        Assert.Equal(5204, (int)WorkflowErrorCode.TemplateCatalogNotFound);
        Assert.Equal(5205, (int)WorkflowErrorCode.TemplatePathRejected);
        Assert.Equal(5206, (int)WorkflowErrorCode.TemplateChanged);
        Assert.Equal(5207, (int)WorkflowErrorCode.TemplateOpenFailed);
        Assert.Equal(5210, (int)WorkflowErrorCode.ImportReviewInvalid);
        Assert.Equal(5220, (int)WorkflowErrorCode.EditorIdentityConflict);
        Assert.Equal(5221, (int)WorkflowErrorCode.EditorLimitExceeded);
        Assert.Equal(5222, (int)WorkflowErrorCode.EditorCommandUnavailable);
        Assert.Equal(5223, (int)WorkflowErrorCode.EditorInputRejected);
        Assert.Equal(5224, (int)WorkflowErrorCode.EditorTargetUnavailable);
        Assert.Equal(5230, (int)WorkflowErrorCode.PreflightDefinitionInvalid);
        Assert.Equal(5231, (int)WorkflowErrorCode.PreflightCommandInvalid);
        Assert.Equal(5232, (int)WorkflowErrorCode.PreflightPluginUnavailable);
        Assert.Equal(5233, (int)WorkflowErrorCode.PreflightInputInvalid);
        Assert.Equal(5234, (int)WorkflowErrorCode.PreflightArgumentInvalid);
        Assert.Equal(5235, (int)WorkflowErrorCode.PreflightBindingInvalid);
        Assert.Equal(5236, (int)WorkflowErrorCode.PreflightCompensationRequired);
        Assert.Equal(5237, (int)WorkflowErrorCode.PreflightCatalogChanged);
    }

    [Fact]
    public void DocumentFailures_ReturnStableCodes()
    {
        var empty = CommandWorkflowDocumentCodec.Deserialize("", false);
        var missingSchema = CommandWorkflowDocumentCodec.Deserialize("{}", false);
        var unsupported = CommandWorkflowDocumentCodec.Deserialize(
            """{"schema_version":99}""",
            false);
        var invalidJson = CommandWorkflowDocumentCodec.Deserialize("{", false);

        Assert.Equal(WorkflowErrorCode.DocumentEmpty, empty.ErrorCode);
        Assert.Equal(WorkflowErrorCode.SchemaMissing, missingSchema.ErrorCode);
        Assert.Equal(WorkflowErrorCode.SchemaUnsupported, unsupported.ErrorCode);
        Assert.Equal(WorkflowErrorCode.JsonInvalid, invalidJson.ErrorCode);
    }

    [Fact]
    public async Task RepositoryFailuresAndListIssues_PropagateStableCodes()
    {
        var repository = new CommandWorkflowRepository(_root, "local-user");
        var invalidId = await repository.LoadManagedAsync("invalid/id");
        var missing = await repository.LoadManagedAsync("workflow.missing");
        var invalidImport = await repository.ImportAsync("");
        var sensitive = await repository.SaveAsync(Workflow("workflow.sensitive", "secret"));

        await File.WriteAllTextAsync(
            Path.Combine(_root, "broken.workflow.json"),
            "{");
        var listed = await repository.ListManagedAsync();

        Assert.Equal(WorkflowErrorCode.PathInvalid, invalidId.ErrorCode);
        Assert.Equal(WorkflowErrorCode.DocumentNotFound, missing.ErrorCode);
        Assert.Equal(WorkflowErrorCode.PathInvalid, invalidImport.ErrorCode);
        Assert.Equal(
            WorkflowErrorCode.SensitiveInputApprovalRequired,
            sensitive.ErrorCode);
        Assert.True(listed.IsSuccess, listed.Error);
        Assert.Equal(WorkflowErrorCode.None, listed.ErrorCode);
        Assert.Equal(WorkflowErrorCode.JsonInvalid, Assert.Single(listed.Issues).ErrorCode);
    }

    [Fact]
    public async Task ReviewEditorAndPreflightFailures_PropagateStableCodes()
    {
        var registry = new PluginRegistry();
        var repository = new CommandWorkflowRepository(_root, "local-user");
        var session = new CommandWorkflowEditorSession(registry, repository);
        session.StartNew("workflow.current", "Current");

        var duplicate = session.DuplicateCurrent("WORKFLOW.CURRENT", "Copy");
        var import = await session.PreviewImportAsync("");
        var template = await session.PreviewTemplateAsync(
            "missing.workflow.json",
            new string('0', 64));
        var preflight = new CommandWorkflowPlanner(registry).Preflight(
            Workflow("workflow.preflight"));

        Assert.False(duplicate);
        Assert.Equal(WorkflowErrorCode.EditorIdentityConflict, session.State.ErrorCode);
        Assert.Equal(WorkflowErrorCode.PathInvalid, import.ErrorCode);
        Assert.Equal(WorkflowErrorCode.TemplateCatalogUnavailable, template.ErrorCode);
        Assert.False(preflight.IsValid);
        Assert.Equal(WorkflowErrorCode.PreflightCommandInvalid, preflight.ErrorCode);
        Assert.All(
            preflight.IssueDetails,
            issue => Assert.NotEqual(WorkflowErrorCode.None, issue.ErrorCode));
    }

    [Fact]
    public void WorkflowFailures_HaveBilingualPresentationKeys()
    {
        var repository = FindRepositoryRoot();
        using var chinese = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repository,
            "src",
            "LongBetterWindows.Host",
            "i18n",
            "zh-CN.json")));
        using var english = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repository,
            "src",
            "LongBetterWindows.Host",
            "i18n",
            "en-US.json")));

        foreach (var code in Enum.GetValues<WorkflowErrorCode>()
                     .Where(code => code != WorkflowErrorCode.None))
        {
            var key = WorkflowErrorPresentation.GetResourceKey(code);
            Assert.True(chinese.RootElement.TryGetProperty(key, out _), key);
            Assert.True(english.RootElement.TryGetProperty(key, out _), key);
        }

        var view = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "WorkflowEditorControl.xaml.cs"));
        Assert.DoesNotContain(
            "SetListStatus(result.Error",
            view,
            StringComparison.Ordinal);
        Assert.Contains(
            "WorkflowErrorPresentation.GetResourceKey(result.ErrorCode)",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PreflightDetail.Text = state.Error",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "I18n(\"workflow.import.error.read\")",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "I18n(\"workflow.template.error.catalog\")",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "I18n(\"workflow.template.error.read\")",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Format(\"workflow.import.error.adopt\"",
            view,
            StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
