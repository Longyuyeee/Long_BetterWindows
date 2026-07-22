using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    /// <summary>Runs an authorized workflow and performs best-effort reverse compensation.</summary>
    public sealed class CommandWorkflowExecutor
    {
        private static readonly TimeSpan DefaultCompensationTimeout = TimeSpan.FromSeconds(30);

        private readonly CommandWorkflowPlanner _planner;
        private readonly IWorkflowCommandRunner _runner;
        private readonly TimeSpan _compensationTimeout;

        public CommandWorkflowExecutor(
            PluginRegistry plugins,
            IWorkflowCommandRunner? runner = null,
            TimeSpan? compensationTimeout = null)
        {
            ArgumentNullException.ThrowIfNull(plugins);
            _planner = new CommandWorkflowPlanner(plugins);
            _runner = runner ?? new CommandExecutor(plugins);
            _compensationTimeout = compensationTimeout ?? DefaultCompensationTimeout;
            if (_compensationTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(compensationTimeout));
        }

        public async Task<CommandWorkflowExecutionResult> ExecuteAsync(
            CommandWorkflowDefinition workflow,
            CommandWorkflowAuthorization? authorization,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(workflow);
            var preflight = _planner.Preflight(workflow);
            var events = new List<WorkflowExecutionEvent>();
            var sequence = 0;
            void AddEvent(
                WorkflowExecutionEventKind kind,
                string? stepId = null,
                string? message = null)
                => events.Add(new WorkflowExecutionEvent(
                    ++sequence,
                    DateTimeOffset.UtcNow,
                    kind,
                    stepId,
                    message));

            if (!preflight.IsValid)
            {
                var message = string.Join(" ", preflight.Issues);
                AddEvent(WorkflowExecutionEventKind.WorkflowRejected, message: message);
                return Result(WorkflowExecutionStatus.Rejected, preflight.Fingerprint, message, events);
            }
            AddEvent(WorkflowExecutionEventKind.PreflightPassed);

            if (!IsAuthorized(preflight, authorization))
            {
                const string message = "Workflow authorization does not match its fingerprint and permission plan.";
                AddEvent(WorkflowExecutionEventKind.WorkflowRejected, message: message);
                return Result(WorkflowExecutionStatus.Rejected, preflight.Fingerprint, message, events);
            }
            AddEvent(WorkflowExecutionEventKind.AuthorizationApproved);
            var approvedAuthorization = authorization!;

            var completed = new List<CommandWorkflowStep>();
            foreach (var step in workflow.Steps)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    AddEvent(WorkflowExecutionEventKind.StepCancelled, step.Id, "Workflow was cancelled.");
                    return await FinishFailureAsync(
                        workflow,
                        preflight.Fingerprint,
                        completed,
                        WorkflowExecutionStatus.Cancelled,
                        "Workflow was cancelled.",
                        events,
                        AddEvent,
                        approvedAuthorization);
                }

                var currentPreflight = _planner.Preflight(workflow);
                if (!currentPreflight.IsValid
                    || !string.Equals(
                        currentPreflight.Fingerprint,
                        preflight.Fingerprint,
                        StringComparison.Ordinal)
                    || !IsAuthorized(currentPreflight, approvedAuthorization))
                {
                    const string message = "Plugin catalog or workflow permissions changed after authorization.";
                    AddEvent(WorkflowExecutionEventKind.StepFailed, step.Id, message);
                    return await FinishFailureAsync(
                        workflow,
                        preflight.Fingerprint,
                        completed,
                        WorkflowExecutionStatus.Failed,
                        message,
                        events,
                        AddEvent,
                        approvedAuthorization);
                }

                AddEvent(WorkflowExecutionEventKind.StepStarted, step.Id);
                PluginCommandResult commandResult;
                try
                {
                    commandResult = await _runner.ExecuteAsync(
                        step.Command!.CommandKey,
                        step.Command.Invocation,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    commandResult = PluginCommandResult.Failure("Workflow was cancelled.");
                }
                catch (Exception ex)
                {
                    commandResult = PluginCommandResult.Failure(ex.Message);
                }

                if (!commandResult.IsSuccess)
                {
                    var cancelled = cancellationToken.IsCancellationRequested;
                    AddEvent(
                        cancelled
                            ? WorkflowExecutionEventKind.StepCancelled
                            : WorkflowExecutionEventKind.StepFailed,
                        step.Id,
                        commandResult.Message);
                    return await FinishFailureAsync(
                        workflow,
                        preflight.Fingerprint,
                        completed,
                        cancelled ? WorkflowExecutionStatus.Cancelled : WorkflowExecutionStatus.Failed,
                        commandResult.Message,
                        events,
                        AddEvent,
                        approvedAuthorization);
                }

                completed.Add(step);
                AddEvent(WorkflowExecutionEventKind.StepSucceeded, step.Id, commandResult.Message);
            }

            AddEvent(WorkflowExecutionEventKind.WorkflowCompleted);
            return Result(
                WorkflowExecutionStatus.Completed,
                preflight.Fingerprint,
                "Workflow completed.",
                events);
        }

        private async Task<CommandWorkflowExecutionResult> FinishFailureAsync(
            CommandWorkflowDefinition workflow,
            string fingerprint,
            IReadOnlyList<CommandWorkflowStep> completed,
            WorkflowExecutionStatus originalStatus,
            string? originalMessage,
            List<WorkflowExecutionEvent> events,
            Action<WorkflowExecutionEventKind, string?, string?> addEvent,
            CommandWorkflowAuthorization authorization)
        {
            if (workflow.FailureMode != WorkflowFailureMode.Compensate)
                return Result(originalStatus, fingerprint, originalMessage, events);

            var compensationAttempted = false;
            var compensationFailed = false;
            var recoveryPreflight = _planner.Preflight(workflow);
            if (!recoveryPreflight.IsValid || !IsAuthorized(recoveryPreflight, authorization))
            {
                addEvent(
                    WorkflowExecutionEventKind.CompensationFailed,
                    null,
                    "Compensation was blocked because its workflow or plugin identity changed.");
                return Result(
                    WorkflowExecutionStatus.CompensationFailed,
                    fingerprint,
                    originalMessage,
                    events);
            }
            foreach (var step in completed.Reverse())
            {
                if (step.Compensation is null) continue;
                compensationAttempted = true;
                addEvent(WorkflowExecutionEventKind.CompensationStarted, step.Id, null);
                PluginCommandResult compensationResult;
                using var timeout = new CancellationTokenSource(_compensationTimeout);
                try
                {
                    compensationResult = await _runner.ExecuteAsync(
                        step.Compensation.CommandKey,
                        step.Compensation.Invocation,
                        timeout.Token);
                }
                catch (Exception ex)
                {
                    compensationResult = PluginCommandResult.Failure(ex.Message);
                }

                if (compensationResult.IsSuccess)
                {
                    addEvent(
                        WorkflowExecutionEventKind.CompensationSucceeded,
                        step.Id,
                        compensationResult.Message);
                }
                else
                {
                    compensationFailed = true;
                    addEvent(
                        WorkflowExecutionEventKind.CompensationFailed,
                        step.Id,
                        compensationResult.Message);
                }
            }

            if (!compensationAttempted)
                return Result(originalStatus, fingerprint, originalMessage, events);
            return Result(
                compensationFailed
                    ? WorkflowExecutionStatus.CompensationFailed
                    : WorkflowExecutionStatus.Compensated,
                fingerprint,
                originalMessage,
                events);
        }

        private static bool IsAuthorized(
            CommandWorkflowPreflightResult preflight,
            CommandWorkflowAuthorization? authorization)
        {
            if (authorization is null
                || authorization.Permissions is null
                || !string.Equals(
                    preflight.Fingerprint,
                    authorization.Fingerprint,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var expected = preflight.Permissions
                .OrderBy(permission => permission.PluginId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var actual = authorization.Permissions
                .OrderBy(permission => permission.PluginId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (expected.Count != actual.Count) return false;
            for (var index = 0; index < expected.Count; index++)
            {
                if (!PermissionMatches(expected[index], actual[index])) return false;
            }
            return true;
        }

        private static bool PermissionMatches(
            WorkflowPermissionRequirement expected,
            WorkflowPermissionRequirement actual)
        {
            if (!string.Equals(expected.PluginId, actual.PluginId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(expected.PluginVersion, actual.PluginVersion, StringComparison.Ordinal)
                || expected.RegistrationRevision != actual.RegistrationRevision)
            {
                return false;
            }
            var expectedCapabilities = expected.Capabilities
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var actualCapabilities = actual.Capabilities
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return expectedCapabilities.Count == actualCapabilities.Count
                && expectedCapabilities.SequenceEqual(
                    actualCapabilities,
                    StringComparer.OrdinalIgnoreCase);
        }

        private static CommandWorkflowExecutionResult Result(
            WorkflowExecutionStatus status,
            string fingerprint,
            string? message,
            IReadOnlyList<WorkflowExecutionEvent> events)
            => new(status, fingerprint, message, events.ToList());
    }
}
