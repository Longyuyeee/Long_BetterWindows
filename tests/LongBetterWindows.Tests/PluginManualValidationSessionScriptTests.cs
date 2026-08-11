using System.IO;

namespace LongBetterWindows.Tests;

public sealed class PluginManualValidationSessionScriptTests
{
    [Fact]
    public void SessionStarter_UsesTheVerifiedFrozenCandidateWithoutApprovingIt()
    {
        var source = Read("start-plugin-validation.ps1");

        Assert.Contains("plan-next-plugin-validation.ps1", source);
        Assert.Contains("artifacts/quality", source);
        Assert.Contains("Get-FileHash", source);
        Assert.Contains("subject_executable_sha256", source);
        Assert.Contains("schema_version = 2", source);
        Assert.Contains("candidate_directory", source);
        Assert.Contains("release_manifest_sha256", source);
        Assert.Contains("self_contained_package_sha256", source);
        Assert.Contains("existingSession.self_contained_package -ne", source);
        Assert.Contains("Get-Process -Name \"LongBetterWindows.Host\"", source);
        Assert.Contains("Start-Process", source);
        Assert.Contains("-FilePath $subjectPath", source);
        Assert.Contains("--open-plugin", source);
        Assert.Contains("-ArgumentList @($session.launch_arguments)", source);
        Assert.Contains("-WorkingDirectory", source);
        Assert.Contains("validation-session.json", source);
        Assert.Contains("pending_human_observation", source);
        Assert.Contains("Write-NewJsonFileAtomically", source);
        Assert.DoesNotContain("approve-plugin-manual-evidence.ps1 @", source);
        Assert.DoesNotContain("status = \"passed\"", source);
    }

    [Fact]
    public void SessionStarter_IsFailClosedForExistingProcessesAndEvidence()
    {
        var source = Read("start-plugin-validation.ps1");

        Assert.Contains("Evidence directory already exists", source);
        Assert.Contains("Close every running Long Assistant host", source);
        Assert.Contains("-PrepareOnly", source);
        Assert.Contains("launch_status", source);
        Assert.Contains("launch_error", source);
        Assert.Contains("selected plugin has no pending", source);
    }

    [Fact]
    public void SessionStarter_ResumesOnlyTheExactPreparedSessionAtomically()
    {
        var source = Read("start-plugin-validation.ps1");

        Assert.Contains("$resumePreparedSession", source);
        Assert.Contains("launch_status -ne \"prepared_only\"", source);
        Assert.Contains("existingSession.schema_version -ne 2", source);
        Assert.Contains("Existing validation session cannot be resumed safely", source);
        Assert.Contains("launch_status = if ($PrepareOnly)", source);
        Assert.Contains("\"launching\"", source);
        Assert.Contains("Update-JsonFileAtomically", source);
        Assert.Contains("$existingSessionHash", source);
        Assert.Contains("$launchingSessionHash", source);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            relativePath));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "LongBetterWindows.sln")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException(
            "Repository root was not found.");
    }
}
