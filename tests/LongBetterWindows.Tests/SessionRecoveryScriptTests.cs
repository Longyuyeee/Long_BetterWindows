using System.IO;

namespace LongBetterWindows.Tests;

public sealed class SessionRecoveryScriptTests
{
    [Fact]
    public void Probe_RequiresPhysicalLockUnlockIdentityAndCleanup()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "verify-session-lock-recovery.ps1"));

        Assert.Contains("--quality-session-recovery-report", source);
        Assert.Contains("--porcelain --untracked-files=no", source);
        Assert.Contains("LockWorkStation", source);
        Assert.Contains("physical_session_lock_recovery", source);
        Assert.Contains("unavailable_after_lock", source);
        Assert.Contains("restored_host_state", source);
        Assert.Contains("identity_preserved", source);
        Assert.Contains("surface_preserved", source);
        Assert.Contains("cleanup_passed", source);
        Assert.Contains("taskkill.exe /PID $process.Id /T /F", source);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "LongBetterWindows.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
