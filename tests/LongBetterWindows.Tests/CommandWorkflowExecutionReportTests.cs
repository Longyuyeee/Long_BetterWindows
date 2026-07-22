using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class CommandWorkflowExecutionReportTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "long-workflow-report-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Create_DefaultReportRedactsAllMessagesAndBindsDefinition()
    {
        var workflow = Workflow();

        var report = CommandWorkflowExecutionReportCodec.Create(
            workflow,
            Result("private result", "private event"),
            reportId: "report.redacted");

        Assert.False(report.MessagesIncluded);
        Assert.Null(report.Message);
        Assert.All(report.Events, item => Assert.Null(item.Message));
        Assert.Equal(
            CommandWorkflowDocumentCodec.ComputeDefinitionSha256(workflow),
            report.WorkflowDefinitionSha256);
    }

    [Fact]
    public void Create_DoesNotPersistInMemoryOutputSummaries()
    {
        var execution = Result() with
        {
            OutputSummaries =
            [
                new WorkflowOutputSummary(
                    "step",
                    WorkflowOutputRole.Primary,
                    "private-output-key",
                    PluginCommandOutputType.Text,
                    2048),
            ],
            TerminalOutputs =
            [
                new WorkflowTerminalOutput(
                    "step",
                    "terminal-output",
                    PluginCommandOutputType.Text,
                    "private-terminal-value"),
            ],
        };

        var report = CommandWorkflowExecutionReportCodec.Create(
            Workflow(),
            execution,
            reportId: "report.output-redaction");
        var json = CommandWorkflowExecutionReportCodec.Serialize(report);

        Assert.DoesNotContain("output_summaries", json);
        Assert.DoesNotContain("private-output-key", json);
        Assert.DoesNotContain("terminal-output", json);
        Assert.DoesNotContain("private-terminal-value", json);
        Assert.DoesNotContain("2048", json);
    }

    [Fact]
    public async Task Save_MessageReportRequiresExplicitApprovalAndRoundTrips()
    {
        var repository = new CommandWorkflowExecutionReportRepository(_root);
        var report = CommandWorkflowExecutionReportCodec.Create(
            Workflow(),
            Result("private result", "private event"),
            includeMessages: true,
            reportId: "report.sensitive");

        var rejected = await repository.SaveAsync(report);
        var saved = await repository.SaveAsync(
            report,
            new WorkflowExecutionReportSaveOptions(AllowSensitiveMessages: true));
        var loaded = await repository.LoadAsync("report.sensitive");

        Assert.False(rejected.IsSuccess);
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.True(loaded.IsSuccess, loaded.Error);
        Assert.Equal("private result", loaded.Report!.Message);
        Assert.Equal("private event", loaded.Report.Events[0].Message);
    }

    [Fact]
    public async Task Save_ExistingReportIsImmutable()
    {
        var repository = new CommandWorkflowExecutionReportRepository(_root);
        var report = CommandWorkflowExecutionReportCodec.Create(
            Workflow(),
            Result(),
            reportId: "report.immutable");

        var first = await repository.SaveAsync(report);
        var second = await repository.SaveAsync(report);

        Assert.True(first.IsSuccess, first.Error);
        Assert.False(second.IsSuccess);
        Assert.Contains("cannot be overwritten", second.Error);
    }

    [Fact]
    public void Deserialize_InvalidEventSequenceIsRejected()
    {
        var report = CommandWorkflowExecutionReportCodec.Create(
            Workflow(),
            Result(),
            reportId: "report.sequence");
        var invalid = report with
        {
            Events = report.Events.Select(item => item with { Sequence = 2 }).ToList(),
        };
        var json = System.Text.Json.JsonSerializer.Serialize(
            invalid,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
            });

        var result = CommandWorkflowExecutionReportCodec.Deserialize(json);

        Assert.False(result.IsSuccess);
        Assert.Contains("sequence", result.Error);
    }

    [Fact]
    public void Create_ReportIdMustBePortableToWindowsFileNames()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CommandWorkflowExecutionReportCodec.Create(
                Workflow(),
                Result(),
                reportId: "report:invalid"));

        Assert.Contains("report id", exception.Message);
    }

    [Fact]
    public async Task Load_MalformedUtf8IsRejected()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(
            Path.Combine(_root, "report.bad.workflow-report.json"),
            [0xff, 0xfe, 0xfd]);

        var result = await new CommandWorkflowExecutionReportRepository(_root)
            .LoadAsync("report.bad");

        Assert.False(result.IsSuccess);
        Assert.Contains("could not be read", result.Error);
    }

    [Fact]
    public async Task Load_OversizedReportIsRejectedBeforeParsing()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "report.large.workflow-report.json");
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(CommandWorkflowExecutionReportRepository.MaximumDocumentBytes + 1);
        }

        var result = await new CommandWorkflowExecutionReportRepository(_root)
            .LoadAsync("report.large");

        Assert.False(result.IsSuccess);
        Assert.Contains("maximum size", result.Error);
    }

    private static CommandWorkflowDefinition Workflow()
        => new(
            "workflow.report",
            "Report workflow",
            WorkflowFailureMode.Stop,
            [
                new CommandWorkflowStep(
                    "step",
                    WorkflowStepEffect.ReadOnly,
                    new WorkflowCommand(
                        "plugin:command",
                        new PluginCommandInvocation { CommandId = "command" })),
            ]);

    private static CommandWorkflowExecutionResult Result(
        string? message = null,
        string? eventMessage = null)
    {
        var timestamp = new DateTimeOffset(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);
        return new CommandWorkflowExecutionResult(
            WorkflowExecutionStatus.Completed,
            new string('a', 64),
            message,
            [
                new WorkflowExecutionEvent(
                    1,
                    timestamp,
                    WorkflowExecutionEventKind.WorkflowCompleted,
                    null,
                    eventMessage),
            ],
            Array.Empty<WorkflowOutputSummary>(),
            Array.Empty<WorkflowTerminalOutput>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
