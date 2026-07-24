using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Interaction;

internal static class SparsePackagePresentation
{
    public static string GetErrorResourceKey(SparsePackageErrorCode code)
        => code switch
        {
            SparsePackageErrorCode.ScriptMissing =>
                "system.sparse.error.scriptMissing",
            SparsePackageErrorCode.ProcessFailed =>
                "system.sparse.error.processFailed",
            SparsePackageErrorCode.InvalidState =>
                "system.sparse.error.invalidState",
            SparsePackageErrorCode.TimedOut =>
                "system.sparse.error.timedOut",
            SparsePackageErrorCode.Cancelled =>
                "system.sparse.error.cancelled",
            SparsePackageErrorCode.UnexpectedFailure =>
                "system.sparse.error.unexpected",
            _ => "system.sparse.error.unexpected",
        };
}
