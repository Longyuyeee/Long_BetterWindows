using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class CommandWorkflowRunSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "long-workflow-run-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteApproved_RequiresMatchingOneTimeReview()
    {
        var registry = Registry();
        using var session = Session(registry);
        var workflow = Workflow();

        var missing = await session.ExecuteApprovedAsync(workflow, new string('a', 64));
        var review = session.Prepare(workflow);
        var mismatched = await session.ExecuteApprovedAsync(workflow, new string('b', 64));

        Assert.False(missing.IsAccepted);
        Assert.True(review.IsValid);
        Assert.False(mismatched.IsAccepted);
    }

    [Fact]
    public async Task ExecuteApproved_RunsOnceAndPersistsRedactedReport()
    {
        var registry = Registry();
        var runner = new FakeRunner((_, _) => Task.FromResult(PluginCommandResult.Success("private")));
        using var session = Session(registry, runner);
        var workflow = Workflow();
        var review = session.Prepare(workflow);

        var result = await session.ExecuteApprovedAsync(workflow, review.Fingerprint);
        var reused = await session.ExecuteApprovedAsync(workflow, review.Fingerprint);
        var reports = await Reports().ListAsync(workflow.Id);
        var loaded = await Reports().LoadAsync(reports.Reports[0].ReportId);

        Assert.True(result.IsAccepted, result.Error);
        Assert.Equal(WorkflowExecutionStatus.Completed, result.Execution!.Status);
        Assert.True(result.ReportSave!.IsSuccess, result.ReportSave.Error);
        Assert.False(reused.IsAccepted);
        Assert.Single(reports.Reports);
        Assert.False(loaded.Report!.MessagesIncluded);
        Assert.All(loaded.Report.Events, item => Assert.Null(item.Message));
    }

    [Fact]
    public async Task ExecuteApproved_RevealsTerminalValuesOnlyWhenExplicitlyRequested()
    {
        var registry = Registry();
        var runner = new FakeRunner((_, _) => Task.FromResult(PluginCommandResult.Success(
            outputs: new Dictionary<string, PluginCommandOutput>
            {
                ["result"] = new(PluginCommandOutputType.Text, "private terminal value"),
            })));
        using var session = Session(registry, runner);
        var workflow = Workflow();

        var defaultReview = session.Prepare(workflow);
        var redacted = await session.ExecuteApprovedAsync(workflow, defaultReview.Fingerprint);
        var approvedReview = session.Prepare(workflow);
        var revealed = await session.ExecuteApprovedAsync(
            workflow,
            approvedReview.Fingerprint,
            includeTerminalOutputValues: true);

        Assert.Empty(redacted.Execution!.TerminalOutputs);
        var output = Assert.Single(revealed.Execution!.TerminalOutputs);
        Assert.Equal("step-1", output.StepId);
        Assert.Equal("result", output.OutputKey);
        Assert.Equal("private terminal value", output.Value);
    }

    [Fact]
    public async Task ExecuteApproved_PluginReregisteredAfterReviewIsRejectedAndAudited()
    {
        var registry = Registry();
        var runner = new FakeRunner((_, _) => Task.FromResult(PluginCommandResult.Success()));
        using var session = Session(registry, runner);
        var workflow = Workflow();
        var review = session.Prepare(workflow);
        registry.Unregister("runner");
        Register(registry);

        var result = await session.ExecuteApprovedAsync(workflow, review.Fingerprint);

        Assert.True(result.IsAccepted);
        Assert.Equal(WorkflowExecutionStatus.Rejected, result.Execution!.Status);
        Assert.Empty(runner.Calls);
        Assert.Single((await Reports().ListAsync(workflow.Id)).Reports);
    }

    [Fact]
    public async Task PrepareAndExecute_RejectReentryWhileCommandIsRunning()
    {
        var registry = Registry();
        var release = new TaskCompletionSource<PluginCommandResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeRunner((_, _) => release.Task);
        using var session = Session(registry, runner);
        var workflow = Workflow();
        var review = session.Prepare(workflow);
        var running = session.ExecuteApprovedAsync(workflow, review.Fingerprint);
        await runner.Called.Task;

        var busyReview = session.Prepare(workflow);
        var busyRun = await session.ExecuteApprovedAsync(workflow, review.Fingerprint);
        release.SetResult(PluginCommandResult.Success());
        await running;

        Assert.False(busyReview.IsValid);
        Assert.False(busyRun.IsAccepted);
    }

    [Fact]
    public async Task CancelExecution_CancelsRunnerAndStillPersistsReport()
    {
        var registry = Registry();
        var runner = new FakeRunner(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return PluginCommandResult.Success();
        });
        using var session = Session(registry, runner);
        var workflow = Workflow();
        var review = session.Prepare(workflow);
        var running = session.ExecuteApprovedAsync(workflow, review.Fingerprint);
        await runner.Called.Task;

        Assert.True(session.CancelExecution());
        var result = await running;

        Assert.Equal(WorkflowExecutionStatus.Cancelled, result.Execution!.Status);
        Assert.True(result.ReportSave!.IsSuccess, result.ReportSave.Error);
    }

    [Fact]
    public async Task ReportList_SkipsMalformedFilesAndOrdersNewestFirst()
    {
        Directory.CreateDirectory(_root);
        var repository = Reports();
        var workflow = Workflow();
        var first = CommandWorkflowExecutionReportCodec.Create(
            workflow,
            ResultAt(new DateTimeOffset(2026, 7, 22, 8, 0, 0, TimeSpan.Zero)),
            reportId: "report.first");
        var second = CommandWorkflowExecutionReportCodec.Create(
            workflow,
            ResultAt(new DateTimeOffset(2026, 7, 22, 9, 0, 0, TimeSpan.Zero)),
            reportId: "report.second");
        await repository.SaveAsync(first);
        await repository.SaveAsync(second);
        await File.WriteAllTextAsync(
            Path.Combine(_root, "broken.workflow-report.json"),
            "not-json");

        var result = await repository.ListAsync(workflow.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { "report.second", "report.first" }, result.Reports.Select(item => item.ReportId));
        Assert.Single(result.Issues);
    }

    private CommandWorkflowRunSession Session(
        PluginRegistry registry,
        IWorkflowCommandRunner? runner = null)
        => new(registry, Reports(), runner);

    private CommandWorkflowExecutionReportRepository Reports() => new(_root);

    private static CommandWorkflowDefinition Workflow()
        => new(
            "workflow.run",
            "Run workflow",
            WorkflowFailureMode.Stop,
            [
                new CommandWorkflowStep(
                    "step-1",
                    WorkflowStepEffect.ReadOnly,
                    new WorkflowCommand(
                        "runner:run",
                        new PluginCommandInvocation { CommandId = "run" })),
            ]);

    private static CommandWorkflowExecutionResult ResultAt(DateTimeOffset timestamp)
        => new(
            WorkflowExecutionStatus.Completed,
            new string('a', 64),
            null,
            [new WorkflowExecutionEvent(1, timestamp, WorkflowExecutionEventKind.WorkflowCompleted, null, null)],
            Array.Empty<WorkflowOutputSummary>(),
            Array.Empty<WorkflowTerminalOutput>());

    private static PluginRegistry Registry()
    {
        var registry = new PluginRegistry();
        Register(registry);
        return registry;
    }

    private static void Register(PluginRegistry registry)
        => registry.Register(
            new PluginManifest
            {
                Id = "runner",
                Name = "Runner",
                Version = "1.0.0",
                EntryPoint = "runner.dll",
                Capabilities = ["file.ops"],
                Commands =
                [
                    new PluginCommand
                    {
                        Id = "run",
                        Title = "Run",
                        AcceptedInputs = [AcceptedInputType.None],
                        Outputs =
                        [
                            new PluginCommandOutputDeclaration
                            {
                                Key = "result",
                                Type = PluginCommandOutputType.Text,
                            },
                        ],
                    },
                ],
            },
            new TestPlugin(),
            null,
            "/runner");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeRunner : IWorkflowCommandRunner
    {
        private readonly Func<string, CancellationToken, Task<PluginCommandResult>> _execute;

        public FakeRunner(Func<string, CancellationToken, Task<PluginCommandResult>> execute)
        {
            _execute = execute;
        }

        public List<string> Calls { get; } = new();
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PluginCommandResult> ExecuteAsync(
            string commandKey,
            PluginCommandInvocation? invocation = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(commandKey);
            Called.TrySetResult();
            return _execute(commandKey, cancellationToken);
        }
    }

    private sealed class TestPlugin : ILongPlugin
    {
        public string Id => "runner";
        public string Name => "Runner";
        public string Version => "1.0.0";
        public PluginState State => PluginState.Loaded;
        public Task<bool> InitializeAsync(IHostApi host) => Task.FromResult(true);
        public Task<bool> StartAsync() => Task.FromResult(true);
        public Task<bool> StopAsync() => Task.FromResult(true);
    }
}
