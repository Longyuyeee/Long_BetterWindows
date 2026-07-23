using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    public sealed record CommandWorkflowExecutionReview(
        bool IsValid,
        string Fingerprint,
        IReadOnlyList<string> Issues,
        IReadOnlyList<WorkflowPermissionRequirement> Permissions,
        int StepCount,
        bool ContainsMutatingSteps);

    public sealed record CommandWorkflowRunResult(
        bool IsAccepted,
        CommandWorkflowExecutionResult? Execution,
        WorkflowExecutionReportSaveResult? ReportSave,
        string? Error);

    /// <summary>Turns an explicit permission review into one execution and one immutable report.</summary>
    public sealed class CommandWorkflowRunSession : IDisposable
    {
        private readonly object _sync = new();
        private readonly CommandWorkflowPlanner _planner;
        private readonly CommandWorkflowExecutor _executor;
        private readonly CommandWorkflowExecutionReportRepository _reports;
        private CommandWorkflowExecutionReview? _pendingReview;
        private CancellationTokenSource? _execution;
        private bool _disposed;

        public CommandWorkflowRunSession(
            PluginRegistry plugins,
            CommandWorkflowExecutionReportRepository reports,
            IWorkflowCommandRunner? runner = null)
        {
            ArgumentNullException.ThrowIfNull(plugins);
            _reports = reports ?? throw new ArgumentNullException(nameof(reports));
            _planner = new CommandWorkflowPlanner(plugins);
            _executor = new CommandWorkflowExecutor(plugins, runner);
        }

        public bool IsRunning
        {
            get { lock (_sync) return _execution is not null; }
        }

        public CommandWorkflowExecutionReview Prepare(
            CommandWorkflowDefinition workflow,
            string? expectedStateFingerprint = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(workflow);
            lock (_sync)
            {
                if (_execution is not null)
                {
                    return new CommandWorkflowExecutionReview(
                        false,
                        string.Empty,
                        ["Another workflow execution is already running."],
                        Array.Empty<WorkflowPermissionRequirement>(),
                        workflow.Steps.Count,
                        workflow.Steps.Any(step => step.Effect == WorkflowStepEffect.Mutating));
                }
                var preflight = _planner.Preflight(workflow);
                var review = new CommandWorkflowExecutionReview(
                    preflight.IsValid,
                    preflight.Fingerprint,
                    preflight.Issues.ToList(),
                    preflight.Permissions.ToList(),
                    workflow.Steps.Count,
                    workflow.Steps.Any(step => step.Effect == WorkflowStepEffect.Mutating));
                if (expectedStateFingerprint is not null
                    && !string.Equals(
                        review.Fingerprint,
                        expectedStateFingerprint,
                        StringComparison.Ordinal))
                {
                    review = review with
                    {
                        IsValid = false,
                        Issues =
                        [
                            "搜索结果已失效：组合动作定义或插件身份已发生变化，请重新搜索。",
                        ],
                    };
                }
                _pendingReview = review.IsValid ? review : null;
                return review;
            }
        }

        public void CancelReview()
        {
            lock (_sync) _pendingReview = null;
        }

        public async Task<CommandWorkflowRunResult> ExecuteApprovedAsync(
            CommandWorkflowDefinition workflow,
            string reviewedFingerprint,
            bool includeSensitiveMessages = false,
            bool includeTerminalOutputValues = false,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(workflow);
            CommandWorkflowExecutionReview review;
            CancellationTokenSource execution;
            lock (_sync)
            {
                if (_execution is not null)
                    return Rejected("Another workflow execution is already running.");
                if (_pendingReview is null
                    || !_pendingReview.IsValid
                    || !string.Equals(
                        _pendingReview.Fingerprint,
                        reviewedFingerprint,
                        StringComparison.Ordinal))
                {
                    return Rejected("Workflow execution approval is missing or no longer matches the review.");
                }
                review = _pendingReview;
                _pendingReview = null;
                execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _execution = execution;
            }

            try
            {
                var authorization = new CommandWorkflowAuthorization(
                    review.Fingerprint,
                    review.Permissions);
                var result = await _executor.ExecuteAsync(
                    workflow,
                    authorization,
                    execution.Token,
                    includeTerminalOutputValues);
                var report = CommandWorkflowExecutionReportCodec.Create(
                    workflow,
                    result,
                    includeSensitiveMessages);
                var reportSave = await _reports.SaveAsync(
                    report,
                    new WorkflowExecutionReportSaveOptions(includeSensitiveMessages),
                    CancellationToken.None);
                return new CommandWorkflowRunResult(
                    true,
                    result,
                    reportSave,
                    reportSave.IsSuccess ? null : reportSave.Error);
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_execution, execution)) _execution = null;
                }
                execution.Dispose();
            }
        }

        public bool CancelExecution()
        {
            lock (_sync)
            {
                if (_execution is null) return false;
                _execution.Cancel();
                return true;
            }
        }

        public void Dispose()
        {
            CancellationTokenSource? execution;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _pendingReview = null;
                execution = _execution;
            }
            execution?.Cancel();
        }

        private static CommandWorkflowRunResult Rejected(string error)
            => new(false, null, null, error);
    }
}
