using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Views;

namespace LongBetterWindows.Tests;

public sealed class WorkflowExecutionPresentationTests
{
    [Fact]
    public void DescribeReview_ProjectsRiskFailureModeAndPermissions()
    {
        var review = new CommandWorkflowExecutionReview(
            true,
            "fingerprint",
            Array.Empty<string>(),
            [new WorkflowPermissionRequirement("plugin.files", "2.1.0", 7, ["files.read", "files.write"])],
            3,
            true);

        var presentation = WorkflowExecutionPresentation.DescribeReview(
            review,
            WorkflowFailureMode.Compensate);

        Assert.Contains("3", presentation.Summary);
        Assert.Contains("回滚", presentation.Summary);
        var permission = Assert.Single(presentation.Permissions);
        Assert.Equal("plugin.files  v2.1.0", permission.Plugin);
        Assert.Equal("files.read、files.write", permission.Capabilities);
    }

    [Fact]
    public void DescribeRunResult_RejectedApprovalDoesNotExposeOutputs()
    {
        var presentation = WorkflowExecutionPresentation.DescribeRunResult(
            new CommandWorkflowRunResult(false, null, null, "approval expired"));

        Assert.Equal("执行未开始", presentation.Title);
        Assert.Equal("approval expired", presentation.Detail);
        Assert.False(presentation.HasOutputs);
        Assert.False(presentation.HasTerminalOutputs);
    }

    [Fact]
    public void DescribeRunResult_ProjectsSummariesAndApprovedTerminalValues()
    {
        var timestamp = new DateTimeOffset(2026, 7, 22, 8, 30, 0, TimeSpan.Zero);
        var execution = new CommandWorkflowExecutionResult(
            WorkflowExecutionStatus.Completed,
            "fingerprint",
            null,
            [new WorkflowExecutionEvent(1, timestamp, WorkflowExecutionEventKind.WorkflowCompleted, null, null)],
            [new WorkflowOutputSummary("step.one", WorkflowOutputRole.Primary, "result", PluginCommandOutputType.Text, 12)],
            [new WorkflowTerminalOutput("step.one", "result", PluginCommandOutputType.Text, "terminal value")]);
        var run = new CommandWorkflowRunResult(
            true,
            execution,
            new WorkflowExecutionReportSaveResult(true, "report.json", null),
            null);

        var presentation = WorkflowExecutionPresentation.DescribeRunResult(run);

        Assert.Equal("执行完成", presentation.Title);
        Assert.Contains("1", presentation.Detail);
        Assert.True(presentation.HasOutputs);
        Assert.True(presentation.HasTerminalOutputs);
        Assert.Equal("result", Assert.Single(presentation.Outputs).Output);
        Assert.Equal("terminal value", Assert.Single(presentation.TerminalOutputs).Value);
    }

    [Fact]
    public void DescribeReport_ProjectsRedactedTimelineWithoutMessages()
    {
        var timestamp = new DateTimeOffset(2026, 7, 22, 8, 30, 0, TimeSpan.Zero);
        var report = new WorkflowExecutionReportDocument(
            1,
            "report-1",
            "workflow-1",
            "definition-sha",
            "fingerprint",
            WorkflowExecutionStatus.Failed,
            timestamp,
            timestamp.AddSeconds(2),
            false,
            "private workflow message",
            [new WorkflowExecutionEvent(
                1,
                timestamp,
                WorkflowExecutionEventKind.StepFailed,
                "step.one",
                "private event message")]);

        var presentation = WorkflowExecutionPresentation.DescribeReport(report);

        Assert.Equal("执行失败", presentation.Title);
        Assert.Contains("消息已脱敏", presentation.Meta);
        Assert.DoesNotContain("private", presentation.Meta);
        var eventItem = Assert.Single(presentation.Timeline);
        Assert.Equal("步骤失败", eventItem.Kind);
        Assert.Equal("step.one", eventItem.Step);
    }
}
