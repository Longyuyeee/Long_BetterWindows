using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Interaction
{
    public enum WorkflowFailureMode
    {
        Stop,
        Compensate,
    }

    public enum WorkflowStepEffect
    {
        ReadOnly,
        Mutating,
    }

    public sealed record WorkflowCommand(
        string CommandKey,
        PluginCommandInvocation? Invocation);

    public sealed record CommandWorkflowStep(
        string Id,
        WorkflowStepEffect Effect,
        WorkflowCommand? Command,
        WorkflowCommand? Compensation = null);

    public sealed record CommandWorkflowDefinition(
        string Id,
        string Name,
        WorkflowFailureMode FailureMode,
        IReadOnlyList<CommandWorkflowStep> Steps);

    public sealed record WorkflowPermissionRequirement(
        string PluginId,
        string PluginVersion,
        long RegistrationRevision,
        IReadOnlyList<string> Capabilities);

    public sealed record CommandWorkflowPreflightResult(
        bool IsValid,
        string Fingerprint,
        IReadOnlyList<string> Issues,
        IReadOnlyList<WorkflowPermissionRequirement> Permissions);

    public sealed record CommandWorkflowAuthorization(
        string Fingerprint,
        IReadOnlyList<WorkflowPermissionRequirement> Permissions);

    public enum WorkflowExecutionStatus
    {
        Rejected,
        Completed,
        Failed,
        Cancelled,
        Compensated,
        CompensationFailed,
    }

    public enum WorkflowExecutionEventKind
    {
        PreflightPassed,
        AuthorizationApproved,
        StepStarted,
        StepSucceeded,
        StepFailed,
        StepCancelled,
        CompensationStarted,
        CompensationSucceeded,
        CompensationFailed,
        WorkflowCompleted,
        WorkflowRejected,
    }

    public sealed record WorkflowExecutionEvent(
        int Sequence,
        DateTimeOffset Timestamp,
        WorkflowExecutionEventKind Kind,
        string? StepId,
        string? Message);

    public sealed record CommandWorkflowExecutionResult(
        WorkflowExecutionStatus Status,
        string Fingerprint,
        string? Message,
        IReadOnlyList<WorkflowExecutionEvent> Events);

    public interface IWorkflowCommandRunner
    {
        Task<PluginCommandResult> ExecuteAsync(
            string commandKey,
            PluginCommandInvocation? invocation = null,
            CancellationToken cancellationToken = default);
    }
}
