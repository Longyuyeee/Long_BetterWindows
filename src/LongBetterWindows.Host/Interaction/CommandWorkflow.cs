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
        IReadOnlyList<string> Capabilities);

    public sealed record CommandWorkflowPreflightResult(
        bool IsValid,
        string Fingerprint,
        IReadOnlyList<string> Issues,
        IReadOnlyList<WorkflowPermissionRequirement> Permissions);
}
