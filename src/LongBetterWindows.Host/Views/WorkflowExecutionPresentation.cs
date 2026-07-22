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
            WorkflowFailureMode failureMode)
        {
            ArgumentNullException.ThrowIfNull(review);
            var summary = review.ContainsMutatingSteps
                ? $"{review.StepCount} 个步骤，包含系统或文件修改；失败策略为“{FailureLabel(failureMode)}”。"
                : $"{review.StepCount} 个只读步骤；失败策略为“{FailureLabel(failureMode)}”。";
            var permissions = review.Permissions
                .Select(permission => new WorkflowPermissionReviewItem(
                    $"{permission.PluginId}  v{permission.PluginVersion}",
                    permission.Capabilities.Count == 0
                        ? "无额外能力"
                        : string.Join("、", permission.Capabilities)))
                .ToList();
            return new WorkflowExecutionReviewPresentation(summary, permissions);
        }

        public static WorkflowExecutionResultPresentation DescribePrepareFailure(
            CommandWorkflowExecutionReview review)
        {
            ArgumentNullException.ThrowIfNull(review);
            return new WorkflowExecutionResultPresentation(
                "无法准备执行",
                string.Join(Environment.NewLine, review.Issues),
                Array.Empty<WorkflowOutputSummaryItem>(),
                Array.Empty<WorkflowTerminalOutputItem>());
        }

        public static WorkflowExecutionResultPresentation DescribeRunResult(
            CommandWorkflowRunResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (!result.IsAccepted || result.Execution is null)
            {
                return new WorkflowExecutionResultPresentation(
                    "执行未开始",
                    result.Error ?? "执行批准已经失效。",
                    Array.Empty<WorkflowOutputSummaryItem>(),
                    Array.Empty<WorkflowTerminalOutputItem>());
            }

            var outputs = result.Execution.OutputSummaries
                .Select(WorkflowOutputSummaryItem.From)
                .ToList();
            var terminalOutputs = result.Execution.TerminalOutputs
                .Select(WorkflowTerminalOutputItem.From)
                .ToList();
            var detail = result.ReportSave?.IsSuccess == true
                ? $"已记录 {result.Execution.Events.Count} 个脱敏事件。"
                : $"执行已结束，但报告保存失败：{result.ReportSave?.Error}";
            return new WorkflowExecutionResultPresentation(
                StatusLabel(result.Execution.Status),
                detail,
                outputs,
                terminalOutputs);
        }

        public static IReadOnlyList<WorkflowReportListItem> ToReportListItems(
            IEnumerable<WorkflowExecutionReportSummary> reports)
        {
            ArgumentNullException.ThrowIfNull(reports);
            return reports.Select(WorkflowReportListItem.From).ToList();
        }

        public static WorkflowReportDetailPresentation DescribeReport(
            WorkflowExecutionReportDocument report)
        {
            ArgumentNullException.ThrowIfNull(report);
            var messageState = report.MessagesIncluded ? "消息未在界面展示" : "消息已脱敏";
            var timeline = report.Events.Select(item => new WorkflowTimelineItem(
                item.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                EventLabel(item.Kind),
                item.StepId ?? "工作流"))
                .ToList();
            return new WorkflowReportDetailPresentation(
                StatusLabel(report.Status),
                $"{report.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {report.Events.Count} 个事件 · {messageState}",
                timeline);
        }

        public static string FailureLabel(WorkflowFailureMode mode)
            => mode == WorkflowFailureMode.Compensate ? "失败时回滚" : "失败时停止";

        public static string StatusLabel(WorkflowExecutionStatus status)
            => status switch
            {
                WorkflowExecutionStatus.Completed => "执行完成",
                WorkflowExecutionStatus.Compensated => "失败后已回滚",
                WorkflowExecutionStatus.CompensationFailed => "回滚未完全成功",
                WorkflowExecutionStatus.Cancelled => "执行已取消",
                WorkflowExecutionStatus.Rejected => "执行已拒绝",
                _ => "执行失败",
            };

        public static string EventLabel(WorkflowExecutionEventKind kind)
            => kind switch
            {
                WorkflowExecutionEventKind.PreflightPassed => "预检通过",
                WorkflowExecutionEventKind.AuthorizationApproved => "批准已确认",
                WorkflowExecutionEventKind.StepStarted => "步骤开始",
                WorkflowExecutionEventKind.StepSucceeded => "步骤成功",
                WorkflowExecutionEventKind.StepFailed => "步骤失败",
                WorkflowExecutionEventKind.StepCancelled => "步骤取消",
                WorkflowExecutionEventKind.CompensationStarted => "开始回滚",
                WorkflowExecutionEventKind.CompensationSucceeded => "回滚成功",
                WorkflowExecutionEventKind.CompensationFailed => "回滚失败",
                WorkflowExecutionEventKind.WorkflowCompleted => "流程完成",
                _ => "流程拒绝",
            };
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
        public static WorkflowOutputSummaryItem From(WorkflowOutputSummary summary)
            => new(
                summary.StepId,
                summary.OutputKey,
                $"{(summary.Role == WorkflowOutputRole.Compensation ? "补偿" : "命令")} · "
                    + $"{summary.Type} · {summary.ValueLength:N0} 字符");
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
        public static WorkflowReportListItem From(WorkflowExecutionReportSummary summary)
            => new(
                summary.ReportId,
                WorkflowExecutionPresentation.StatusLabel(summary.Status),
                $"{summary.StartedAt.ToLocalTime():MM-dd HH:mm} · {summary.EventCount} 事件");
    }
}
