using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    /// <summary>Runs an authorized workflow and performs best-effort reverse compensation.</summary>
    public sealed class CommandWorkflowExecutor
    {
        private static readonly TimeSpan DefaultCompensationTimeout = TimeSpan.FromSeconds(30);

        private readonly CommandWorkflowPlanner _planner;
        private readonly PluginRegistry _plugins;
        private readonly IWorkflowCommandRunner _runner;
        private readonly TimeSpan _compensationTimeout;

        public CommandWorkflowExecutor(
            PluginRegistry plugins,
            IWorkflowCommandRunner? runner = null,
            TimeSpan? compensationTimeout = null)
        {
            ArgumentNullException.ThrowIfNull(plugins);
            _plugins = plugins;
            _planner = new CommandWorkflowPlanner(plugins);
            _runner = runner ?? new CommandExecutor(plugins);
            _compensationTimeout = compensationTimeout ?? DefaultCompensationTimeout;
            if (_compensationTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(compensationTimeout));
        }

        public async Task<CommandWorkflowExecutionResult> ExecuteAsync(
            CommandWorkflowDefinition workflow,
            CommandWorkflowAuthorization? authorization,
            CancellationToken cancellationToken = default,
            bool includeTerminalOutputValues = false)
        {
            ArgumentNullException.ThrowIfNull(workflow);
            var preflight = _planner.Preflight(workflow);
            var events = new List<WorkflowExecutionEvent>();
            var outputSummaries = new List<WorkflowOutputSummary>();
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
            var stepOutputs = new Dictionary<
                string,
                IReadOnlyDictionary<string, PluginCommandOutput>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var step in workflow.Steps)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    AddEvent(WorkflowExecutionEventKind.StepCancelled, step.Id, "Workflow was cancelled.");
                    return await FinishFailureAsync(
                        workflow,
                        preflight.Fingerprint,
                        completed,
                        stepOutputs,
                        outputSummaries,
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
                        stepOutputs,
                        outputSummaries,
                        WorkflowExecutionStatus.Failed,
                        message,
                        events,
                        AddEvent,
                        approvedAuthorization);
                }

                AddEvent(WorkflowExecutionEventKind.StepStarted, step.Id);
                var binding = CommandWorkflowBindingResolver.Resolve(step.Command!, stepOutputs);
                if (!binding.IsSuccess)
                {
                    AddEvent(WorkflowExecutionEventKind.StepFailed, step.Id, binding.Error);
                    return await FinishFailureAsync(
                        workflow,
                        preflight.Fingerprint,
                        completed,
                        stepOutputs,
                        outputSummaries,
                        WorkflowExecutionStatus.Failed,
                        binding.Error,
                        events,
                        AddEvent,
                        approvedAuthorization);
                }
                PluginCommandResult commandResult;
                try
                {
                    commandResult = await _runner.ExecuteAsync(
                        step.Command!.CommandKey,
                        binding.Invocation,
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
                        stepOutputs,
                        outputSummaries,
                        cancelled ? WorkflowExecutionStatus.Cancelled : WorkflowExecutionStatus.Failed,
                        commandResult.Message,
                        events,
                        AddEvent,
                        approvedAuthorization);
                }

                var declaration = _plugins.Commands.Get(step.Command!.CommandKey);
                if (declaration is null)
                {
                    const string error = "Command declaration is unavailable after execution.";
                    AddEvent(WorkflowExecutionEventKind.StepFailed, step.Id, error);
                    return await FinishFailureAsync(
                        workflow,
                        preflight.Fingerprint,
                        completed,
                        stepOutputs,
                        outputSummaries,
                        WorkflowExecutionStatus.Failed,
                        error,
                        events,
                        AddEvent,
                        approvedAuthorization);
                }
                if (!CommandWorkflowBindingResolver.TrySnapshotDeclaredOutputs(
                    commandResult,
                    declaration.Command.Outputs,
                    out var outputs,
                    out var outputError))
                {
                    AddEvent(WorkflowExecutionEventKind.StepFailed, step.Id, outputError);
                    return await FinishFailureAsync(
                        workflow,
                        preflight.Fingerprint,
                        completed,
                        stepOutputs,
                        outputSummaries,
                        WorkflowExecutionStatus.Failed,
                        outputError,
                        events,
                        AddEvent,
                        approvedAuthorization);
                }

                completed.Add(step);
                stepOutputs[step.Id] = outputs;
                AddOutputSummaries(
                    outputSummaries,
                    step.Id,
                    WorkflowOutputRole.Primary,
                    outputs);
                AddEvent(WorkflowExecutionEventKind.StepSucceeded, step.Id, commandResult.Message);
            }

            AddEvent(WorkflowExecutionEventKind.WorkflowCompleted);
            var terminalOutputs = includeTerminalOutputValues
                ? BuildTerminalOutputs(workflow, stepOutputs)
                : Array.Empty<WorkflowTerminalOutput>();
            return Result(
                WorkflowExecutionStatus.Completed,
                preflight.Fingerprint,
                "Workflow completed.",
                events,
                outputSummaries,
                terminalOutputs);
        }

        private async Task<CommandWorkflowExecutionResult> FinishFailureAsync(
            CommandWorkflowDefinition workflow,
            string fingerprint,
            IReadOnlyList<CommandWorkflowStep> completed,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, PluginCommandOutput>> stepOutputs,
            List<WorkflowOutputSummary> outputSummaries,
            WorkflowExecutionStatus originalStatus,
            string? originalMessage,
            List<WorkflowExecutionEvent> events,
            Action<WorkflowExecutionEventKind, string?, string?> addEvent,
            CommandWorkflowAuthorization authorization)
        {
            if (workflow.FailureMode != WorkflowFailureMode.Compensate)
                return Result(originalStatus, fingerprint, originalMessage, events, outputSummaries);

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
                    events,
                    outputSummaries);
            }
            foreach (var step in completed.Reverse())
            {
                if (step.Compensation is null) continue;
                compensationAttempted = true;
                addEvent(WorkflowExecutionEventKind.CompensationStarted, step.Id, null);
                var binding = CommandWorkflowBindingResolver.Resolve(step.Compensation, stepOutputs);
                if (!binding.IsSuccess)
                {
                    compensationFailed = true;
                    addEvent(
                        WorkflowExecutionEventKind.CompensationFailed,
                        step.Id,
                        binding.Error);
                    continue;
                }
                PluginCommandResult compensationResult;
                using var timeout = new CancellationTokenSource(_compensationTimeout);
                try
                {
                    compensationResult = await _runner.ExecuteAsync(
                        step.Compensation.CommandKey,
                        binding.Invocation,
                        timeout.Token);
                }
                catch (Exception ex)
                {
                    compensationResult = PluginCommandResult.Failure(ex.Message);
                }

                var declaration = _plugins.Commands.Get(step.Compensation.CommandKey);
                IReadOnlyDictionary<string, PluginCommandOutput> compensationOutputs =
                    new Dictionary<string, PluginCommandOutput>();
                if (compensationResult.IsSuccess && declaration is null)
                {
                    compensationResult = PluginCommandResult.Failure(
                        "Compensation command declaration is unavailable.");
                }
                else if (compensationResult.IsSuccess
                    && !CommandWorkflowBindingResolver.TrySnapshotDeclaredOutputs(
                            compensationResult,
                            declaration!.Command.Outputs,
                            out compensationOutputs,
                            out var outputError))
                {
                    compensationResult = PluginCommandResult.Failure(outputError!);
                }

                if (compensationResult.IsSuccess)
                {
                    AddOutputSummaries(
                        outputSummaries,
                        step.Id,
                        WorkflowOutputRole.Compensation,
                        compensationOutputs);
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
                return Result(originalStatus, fingerprint, originalMessage, events, outputSummaries);
            return Result(
                compensationFailed
                    ? WorkflowExecutionStatus.CompensationFailed
                    : WorkflowExecutionStatus.Compensated,
                fingerprint,
                originalMessage,
                events,
                outputSummaries);
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
            IReadOnlyList<WorkflowExecutionEvent> events,
            IReadOnlyList<WorkflowOutputSummary>? outputSummaries = null,
            IReadOnlyList<WorkflowTerminalOutput>? terminalOutputs = null)
            => new(
                status,
                fingerprint,
                message,
                events.ToList(),
                outputSummaries is null
                    ? Array.Empty<WorkflowOutputSummary>()
                    : outputSummaries.ToList(),
                terminalOutputs is null
                    ? Array.Empty<WorkflowTerminalOutput>()
                    : terminalOutputs.ToList());

        private static void AddOutputSummaries(
            ICollection<WorkflowOutputSummary> summaries,
            string stepId,
            WorkflowOutputRole role,
            IReadOnlyDictionary<string, PluginCommandOutput> outputs)
        {
            foreach (var output in outputs.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                summaries.Add(new WorkflowOutputSummary(
                    stepId,
                    role,
                    output.Key,
                    output.Value.Type,
                    output.Value.Value.Length));
            }
        }

        private static IReadOnlyList<WorkflowTerminalOutput> BuildTerminalOutputs(
            CommandWorkflowDefinition workflow,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, PluginCommandOutput>> stepOutputs)
        {
            var consumed = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var step in workflow.Steps)
            {
                AddConsumedBindings(consumed, step.Command?.Bindings);
                AddConsumedBindings(consumed, step.Compensation?.Bindings);
            }

            var result = new List<WorkflowTerminalOutput>();
            foreach (var step in workflow.Steps)
            {
                if (!stepOutputs.TryGetValue(step.Id, out var outputs)) continue;
                consumed.TryGetValue(step.Id, out var consumedKeys);
                foreach (var output in outputs.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    if (consumedKeys?.Contains(output.Key) == true) continue;
                    result.Add(new WorkflowTerminalOutput(
                        step.Id,
                        output.Key,
                        output.Value.Type,
                        output.Value.Value));
                }
            }
            return result;
        }

        private static void AddConsumedBindings(
            IDictionary<string, HashSet<string>> consumed,
            IReadOnlyList<WorkflowValueBinding>? bindings)
        {
            foreach (var binding in bindings ?? Array.Empty<WorkflowValueBinding>())
            {
                if (!consumed.TryGetValue(binding.SourceStepId, out var keys))
                {
                    keys = new HashSet<string>(StringComparer.Ordinal);
                    consumed[binding.SourceStepId] = keys;
                }
                keys.Add(binding.OutputKey);
            }
        }
    }
}
