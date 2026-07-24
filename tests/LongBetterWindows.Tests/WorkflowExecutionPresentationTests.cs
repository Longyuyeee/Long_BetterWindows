using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;
using LongBetterWindows.Host.Views;

namespace LongBetterWindows.Tests;

public sealed class WorkflowExecutionPresentationTests
{
    private static readonly Func<string, string> Zh = Translator("zh-CN");
    private static readonly Func<string, string> En = Translator("en-US");

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
            WorkflowFailureMode.Compensate,
            Zh);

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
            new CommandWorkflowRunResult(false, null, null, "approval expired"),
            Zh);

        Assert.Equal("执行未开始", presentation.Title);
        Assert.DoesNotContain("approval expired", presentation.Detail);
        Assert.Contains("执行批准", presentation.Detail);
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

        var presentation = WorkflowExecutionPresentation.DescribeRunResult(run, Zh);

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

        var presentation = WorkflowExecutionPresentation.DescribeReport(report, Zh);

        Assert.Equal("执行失败", presentation.Title);
        Assert.Contains("消息已脱敏", presentation.Meta);
        Assert.DoesNotContain("private", presentation.Meta);
        var eventItem = Assert.Single(presentation.Timeline);
        Assert.Equal("步骤失败", eventItem.Kind);
        Assert.Equal("step.one", eventItem.Step);
    }

    [Fact]
    public void EnglishProjection_LocalizesReviewResultAndTimeline()
    {
        var timestamp = new DateTimeOffset(2026, 7, 22, 8, 30, 0, TimeSpan.Zero);
        var review = new CommandWorkflowExecutionReview(
            true,
            "fingerprint",
            Array.Empty<string>(),
            [new WorkflowPermissionRequirement("plugin.files", "2.1.0", 7, ["files.read", "files.write"])],
            3,
            true);
        var reviewPresentation = WorkflowExecutionPresentation.DescribeReview(
            review,
            WorkflowFailureMode.Compensate,
            En);
        var report = new WorkflowExecutionReportDocument(
            1,
            "report-1",
            "workflow-1",
            "definition-sha",
            "fingerprint",
            WorkflowExecutionStatus.Completed,
            timestamp,
            timestamp.AddSeconds(1),
            false,
            null,
            [new WorkflowExecutionEvent(
                1,
                timestamp,
                WorkflowExecutionEventKind.WorkflowCompleted,
                null,
                null)]);
        var reportPresentation = WorkflowExecutionPresentation.DescribeReport(
            report,
            En);

        Assert.Contains("3 steps", reviewPresentation.Summary);
        Assert.Contains("Roll back", reviewPresentation.Summary);
        Assert.Equal(
            "files.read, files.write",
            Assert.Single(reviewPresentation.Permissions).Capabilities);
        Assert.Equal("Run completed", reportPresentation.Title);
        Assert.Contains("messages redacted", reportPresentation.Meta);
        Assert.Equal(
            "Workflow completed",
            Assert.Single(reportPresentation.Timeline).Kind);
        Assert.DoesNotMatch("[一-龥]", reviewPresentation.Summary);
        Assert.DoesNotMatch("[一-龥]", reportPresentation.Meta);
    }

    private static Func<string, string> Translator(string language)
    {
        var root = FindRepositoryRoot();
        var service = new I18nService(
            Path.Combine(root, "src", "LongBetterWindows.Host", "i18n"),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "language.json"));
        service.Initialize(language);
        return key => service.T(key);
    }

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
}
