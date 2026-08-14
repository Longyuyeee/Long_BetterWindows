using System.IO;
using System.Diagnostics;
using LongBetterWindows.Host.Services;

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

    [Fact]
    public void Probe_IsAcceptedByTheWindowsPowerShellParser()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "verify-session-lock-recovery.ps1");
        var escapedPath = path.Replace("'", "''", StringComparison.Ordinal);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -Command \"[scriptblock]::Create([IO.File]::ReadAllText('{escapedPath}')) | Out-Null\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("PowerShell could not start.");
        Assert.True(process.WaitForExit(10_000));
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, error);
    }

    [Fact]
    public void SleepWakeProbe_RequiresPhysicalPowerEventsIdentityAndCleanup()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "verify-sleep-wake-recovery.ps1"));

        Assert.Contains("--quality-power-recovery-report", source);
        Assert.Contains("--porcelain --untracked-files=no", source);
        Assert.Contains("SetSuspendState", source);
        Assert.Contains("physical_sleep_wake_recovery", source);
        Assert.Contains("WM_POWERBROADCAST", source);
        Assert.Contains("unavailable_after_suspend", source);
        Assert.Contains("restored_host_state", source);
        Assert.Contains("identity_preserved", source);
        Assert.Contains("surface_preserved", source);
        Assert.Contains("cleanup_passed", source);
        Assert.Contains("taskkill.exe /PID $process.Id /T /F", source);
    }

    [Fact]
    public void SleepWakeProbe_UsesDedicatedPhysicalPowerTransitionContract()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "LongBetterWindows.Host",
            "Services",
            "PhysicalPowerRecoveryQualityProbe.cs"));

        Assert.Contains("PowerTransitionObserved", source);
        Assert.Contains("HostPowerTransition.Suspended", source);
        Assert.DoesNotContain("InteractionAvailabilityChanged", source);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void SleepWakeProbe_WaitsForUnlockBeforeCheckingHostVisibility(
        bool interactionRestored,
        bool hostVisibleAfterRestore,
        bool expected)
        => Assert.Equal(
            expected,
            PhysicalPowerRecoveryQualityProbe.IsRestoredHostStateForQuality(
                interactionRestored,
                hostVisibleAfterRestore));

    [Fact]
    public void SleepWakeProbe_IsAcceptedByTheWindowsPowerShellParser()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "verify-sleep-wake-recovery.ps1");
        var escapedPath = path.Replace("'", "''", StringComparison.Ordinal);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -Command \"[scriptblock]::Create([IO.File]::ReadAllText('{escapedPath}')) | Out-Null\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("PowerShell could not start.");
        Assert.True(process.WaitForExit(10_000));
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, error);
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
