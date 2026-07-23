using System.Runtime.InteropServices;

namespace LongBetterWindows.Host.Automation;

internal enum QualityWorkflowAction
{
    CancelReview = 1,
    ApproveTerminalOutput = 2,
    ConfirmReview = 3,
    ClearTerminalOutput = 4,
    QueryReviewReady = 10,
    QueryTerminalOutputLength = 11,
    QueryTerminalOutputCleared = 12,
}

internal static class QualityWorkflowAutomation
{
    private const string MessageName = "LongBetterWindows.Quality.WorkflowAction.v1";

    internal static readonly int MessageId = checked((int)RegisterWindowMessage(MessageName));

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string messageName);
}
