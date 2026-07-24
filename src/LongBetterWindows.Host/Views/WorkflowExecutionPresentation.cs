using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Host.Views
{
    /// <summary>
    /// Projects workflow execution domain state into values that the WPF view can bind.
    /// Keeping these rules outside the control makes approval and report rendering testable.
    /// </summary>
    internal static class WorkflowExecutionPresentation
    {
        public static WorkflowExecutionReviewPresentation DescribeReview(
            CommandWorkflowExecutionReview review,
            WorkflowFailureMode failureMode,
            Func<string, string> translate)
        {
            ArgumentNullException.ThrowIfNull(review);
            ArgumentNullException.ThrowIfNull(translate);
            var summary = review.ContainsMutatingSteps
                ? Format(
                    translate,
                    "workflow.execution.review.summaryMutating",
                    review.StepCount,
                    FailureLabel(failureMode, translate))
                : Format(
                    translate,
                    "workflow.execution.review.summaryReadOnly",
                    review.StepCount,
                    FailureLabel(failureMode, translate));
            var permissions = review.Permissions
                .Select(permission => new WorkflowPermissionReviewItem(
                    $"{permission.PluginId}  v{permission.PluginVersion}",
                    permission.Capabilities.Count == 0
                        ? translate("workflow.execution.review.noCapabilities")
                        : string.Join(
                            translate("workflow.execution.review.capabilitySeparator"),
                            permission.Capabilities)))
                .ToList();
            return new WorkflowExecutionReviewPresentation(summary, permissions);
        }

        public static WorkflowExecutionResultPresentation DescribePrepareFailure(
            CommandWorkflowExecutionReview review,
            Func<string, string> translate)
        {
            ArgumentNullException.ThrowIfNull(review);
            ArgumentNullException.ThrowIfNull(translate);
            return new WorkflowExecutionResultPresentation(
                translate("workflow.execution.prepareFailed.title"),
                Format(
                    translate,
                    "workflow.execution.prepareFailed.detail",
                    review.Issues.Count),
                Array.Empty<WorkflowOutputSummaryItem>(),
                Array.Empty<WorkflowTerminalOutputItem>());
        }

        public static WorkflowExecutionResultPresentation DescribeRunResult(
            CommandWorkflowRunResult result,
            Func<string, string> translate)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(translate);
            if (!result.IsAccepted || result.Execution is null)
            {
                return new WorkflowExecutionResultPresentation(
                    translate("workflow.execution.notStarted.title"),
                    translate("workflow.execution.notStarted.detail"),
                    Array.Empty<WorkflowOutputSummaryItem>(),
                    Array.Empty<WorkflowTerminalOutputItem>());
            }

            var outputs = result.Execution.OutputSummaries
                .Select(summary => WorkflowOutputSummaryItem.From(summary, translate))
                .ToList();
            var terminalOutputs = result.Execution.TerminalOutputs
                .Select(WorkflowTerminalOutputItem.From)
                .ToList();
            var detail = result.ReportSave?.IsSuccess == true
                ? Format(
                    translate,
                    "workflow.execution.report.recorded",
                    result.Execution.Events.Count)
                : translate("workflow.execution.report.saveFailed");
            return new WorkflowExecutionResultPresentation(
                StatusLabel(result.Execution.Status, translate),
                detail,
                outputs,
                terminalOutputs);
        }

        public static IReadOnlyList<WorkflowReportListItem> ToReportListItems(
            IEnumerable<WorkflowExecutionReportSummary> reports,
            Func<string, string> translate)
        {
            ArgumentNullException.ThrowIfNull(reports);
            ArgumentNullException.ThrowIfNull(translate);
            return reports
                .Select(summary => WorkflowReportListItem.From(summary, translate))
                .ToList();
        }

        public static WorkflowReportDetailPresentation DescribeReport(
            WorkflowExecutionReportDocument report,
            Func<string, string> translate)
        {
            ArgumentNullException.ThrowIfNull(report);
            ArgumentNullException.ThrowIfNull(translate);
            var messageState = translate(report.MessagesIncluded
                ? "workflow.reports.messageHidden"
                : "workflow.reports.messageRedacted");
            var timeline = report.Events.Select(item => new WorkflowTimelineItem(
                item.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                EventLabel(item.Kind, translate),
                item.StepId ?? translate("workflow.reports.workflowStep")))
                .ToList();
            return new WorkflowReportDetailPresentation(
                StatusLabel(report.Status, translate),
                Format(
                    translate,
                    "workflow.reports.detailMeta",
                    report.StartedAt.ToLocalTime(),
                    report.Events.Count,
                    messageState),
                timeline);
        }

        public static string FailureLabel(
            WorkflowFailureMode mode,
            Func<string, string> translate)
            => translate(mode == WorkflowFailureMode.Compensate
                ? "workflow.failure.compensate"
                : "workflow.failure.stop");

        public static string StatusLabel(
            WorkflowExecutionStatus status,
            Func<string, string> translate)
            => translate(status switch
            {
                WorkflowExecutionStatus.Completed => "workflow.execution.status.completed",
                WorkflowExecutionStatus.Compensated => "workflow.execution.status.compensated",
                WorkflowExecutionStatus.CompensationFailed => "workflow.execution.status.compensationFailed",
                WorkflowExecutionStatus.Cancelled => "workflow.execution.status.cancelled",
                WorkflowExecutionStatus.Rejected => "workflow.execution.status.rejected",
                _ => "workflow.execution.status.failed",
            });

        public static string EventLabel(
            WorkflowExecutionEventKind kind,
            Func<string, string> translate)
            => translate(kind switch
            {
                WorkflowExecutionEventKind.PreflightPassed => "workflow.execution.event.preflightPassed",
                WorkflowExecutionEventKind.AuthorizationApproved => "workflow.execution.event.authorizationApproved",
                WorkflowExecutionEventKind.StepStarted => "workflow.execution.event.stepStarted",
                WorkflowExecutionEventKind.StepSucceeded => "workflow.execution.event.stepSucceeded",
                WorkflowExecutionEventKind.StepFailed => "workflow.execution.event.stepFailed",
                WorkflowExecutionEventKind.StepCancelled => "workflow.execution.event.stepCancelled",
                WorkflowExecutionEventKind.CompensationStarted => "workflow.execution.event.compensationStarted",
                WorkflowExecutionEventKind.CompensationSucceeded => "workflow.execution.event.compensationSucceeded",
                WorkflowExecutionEventKind.CompensationFailed => "workflow.execution.event.compensationFailed",
                WorkflowExecutionEventKind.WorkflowCompleted => "workflow.execution.event.workflowCompleted",
                _ => "workflow.execution.event.workflowRejected",
            });

        private static string Format(
            Func<string, string> translate,
            string key,
            params object[] arguments)
            => string.Format(translate(key), arguments);
    }

    internal sealed record WorkflowExecutionReviewPresentation(
        string Summary,
        IReadOnlyList<WorkflowPermissionReviewItem> Permissions);

    internal sealed record WorkflowExecutionResultPresentation(
        string Title,
        string Detail,
        IReadOnlyList<WorkflowOutputSummaryItem> Outputs,
        IReadOnlyList<WorkflowTerminalOutputItem> TerminalOutputs)
    {
        public bool HasOutputs => Outputs.Count > 0;
        public bool HasTerminalOutputs => TerminalOutputs.Count > 0;
    }

    internal sealed record WorkflowPermissionReviewItem(string Plugin, string Capabilities);

    internal sealed record WorkflowTimelineItem(string Time, string Kind, string Step);

    internal sealed record WorkflowReportDetailPresentation(
        string Title,
        string Meta,
        IReadOnlyList<WorkflowTimelineItem> Timeline);

    internal sealed record WorkflowOutputSummaryItem(string Step, string Output, string Detail)
    {
        public static WorkflowOutputSummaryItem From(
            WorkflowOutputSummary summary,
            Func<string, string> translate)
            => new(
                summary.StepId,
                summary.OutputKey,
                string.Format(
                    translate("workflow.execution.output.detail"),
                    translate(summary.Role == WorkflowOutputRole.Compensation
                        ? "workflow.execution.output.role.compensation"
                        : "workflow.execution.output.role.primary"),
                    translate(summary.Type == PluginCommandOutputType.Path
                        ? "workflow.execution.output.type.path"
                        : "workflow.execution.output.type.text"),
                    summary.ValueLength));
    }

    internal sealed record WorkflowTerminalOutputItem(WorkflowTerminalOutput Source)
    {
        public string Detail => $"{Source.StepId} / {Source.OutputKey} / {Source.Type}";
        public string Value => Source.Value;

        public static WorkflowTerminalOutputItem From(WorkflowTerminalOutput output)
            => new(output);
    }

    internal sealed record WorkflowReportListItem(string ReportId, string Status, string Detail)
    {
        public static WorkflowReportListItem From(
            WorkflowExecutionReportSummary summary,
            Func<string, string> translate)
            => new(
                summary.ReportId,
                WorkflowExecutionPresentation.StatusLabel(summary.Status, translate),
                string.Format(
                    translate("workflow.reports.listDetail"),
                    summary.StartedAt.ToLocalTime(),
                    summary.EventCount));
    }
}
