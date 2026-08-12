using System.IO;

namespace LongBetterWindows.Tests;

public sealed class WorkspaceSwitchScriptTests
{
    [Fact]
    public void Probe_RequiresStableModuleAndPluginIdentityMatrix()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "verify-workspace-switch-stability.ps1"));

        Assert.Contains("--quality-workspace-switch-report", source);
        Assert.Contains("--porcelain --untracked-files=no", source);
        Assert.Contains("expected_module_count -eq 7", source);
        Assert.Contains("expected_plugin_runtime_module_count -eq 3", source);
        Assert.Contains("$cycles.Count -eq 12", source);
        Assert.Contains("$samples.Count -eq 13", source);
        Assert.Contains("$invalidCycles.Count -eq 0", source);
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
