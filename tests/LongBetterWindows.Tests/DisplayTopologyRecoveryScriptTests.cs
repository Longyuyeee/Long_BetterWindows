using System.Diagnostics;
using System.IO;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class DisplayTopologyRecoveryScriptTests
{
    [Fact]
    public void Probe_RequiresPhysicalDisplayEventsAndRestoresExtendedMode()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "verify-display-topology-recovery.ps1"));

        Assert.Contains("--quality-display-topology-report", source);
        Assert.Contains("--porcelain --untracked-files=no", source);
        Assert.Contains("DisplaySwitch.exe", source);
        Assert.Contains("/internal", source);
        Assert.Contains("/extend", source);
        Assert.Contains("physical_display_topology_recovery", source);
        Assert.Contains("WM_DISPLAYCHANGE", source);
        Assert.Contains("identity_preserved", source);
        Assert.Contains("cleanup_passed", source);
        Assert.Contains("taskkill.exe /PID $process.Id /T /F", source);
    }

    [Fact]
    public void Probe_IsAcceptedByTheWindowsPowerShellParser()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "verify-display-topology-recovery.ps1");
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
    public void Probe_AcceptsCompleteReducedAndRestoredTopology()
        => Assert.True(PhysicalDisplayTopologyQualityProbe.EvaluateForQuality(
            initialMonitorCount: 2,
            reducedMonitorCount: 1,
            restoredMonitorCount: 2,
            displayEventCount: 2,
            reducedWindowOnMonitor: true,
            restoredWindowOnMonitor: true,
            reducedHostState: true,
            restoredHostState: true,
            topologyRestored: true,
            identityPreserved: true,
            surfacePreserved: true,
            cleanupPassed: true));

    [Theory]
    [InlineData(2, 2, 2, 2)]
    [InlineData(2, 1, 1, 2)]
    [InlineData(2, 1, 2, 1)]
    public void Probe_RejectsMissingTopologyTransition(
        int initialMonitorCount,
        int reducedMonitorCount,
        int restoredMonitorCount,
        int displayEventCount)
        => Assert.False(PhysicalDisplayTopologyQualityProbe.EvaluateForQuality(
            initialMonitorCount,
            reducedMonitorCount,
            restoredMonitorCount,
            displayEventCount,
            reducedWindowOnMonitor: true,
            restoredWindowOnMonitor: true,
            reducedHostState: true,
            restoredHostState: true,
            topologyRestored: true,
            identityPreserved: true,
            surfacePreserved: true,
            cleanupPassed: true));

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
