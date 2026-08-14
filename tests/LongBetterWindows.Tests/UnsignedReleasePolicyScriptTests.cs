using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LongBetterWindows.Tests;

public sealed class UnsignedReleasePolicyScriptTests
{
    [Theory]
    [InlineData("1.11.0", "stable")]
    [InlineData("1.11.0-rc.8", "prerelease")]
    public void Policy_AcceptsStableAndPrereleaseVersions(
        string version,
        string expectedReleaseChannel)
    {
        using var document = RunPolicy(version);
        var policy = document.RootElement;

        Assert.Equal(version, policy.GetProperty("version").GetString());
        Assert.Equal(expectedReleaseChannel, policy.GetProperty("release_channel").GetString());
        Assert.Equal("unsigned", policy.GetProperty("distribution_channel").GetString());
        Assert.False(policy.GetProperty("signed").GetBoolean());
        Assert.Equal("unverified", policy.GetProperty("publisher_identity").GetString());
        Assert.Equal("not_signed", policy.GetProperty("authenticode_status").GetString());
        Assert.Equal("lowest", policy.GetProperty("installer_privileges").GetString());
        Assert.True(policy.GetProperty("smartscreen_disclosure_required").GetBoolean());
        Assert.True(policy.GetProperty("sha256_verification_required").GetBoolean());
        Assert.True(policy.GetProperty("update_manifest_signature_required").GetBoolean());
        Assert.Equal(
            "RSA-SHA256",
            policy.GetProperty("update_manifest_signature_algorithm").GetString());
        Assert.Contains("SmartScreen", policy.GetProperty("security_notice").GetString());
        Assert.Contains("SHA-256", policy.GetProperty("security_notice").GetString());
    }

    [Fact]
    public void Policy_RejectsInvalidVersion()
    {
        var result = RunPowerShell("bad version");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("semantic version", result.Error);
    }

    [Fact]
    public void ReleaseEntryPoints_ConsumeTheSharedPolicy()
    {
        var root = FindRepositoryRoot();
        var release = File.ReadAllText(Path.Combine(root, "release.ps1"));
        var installerBuild = File.ReadAllText(Path.Combine(root, "build-installer.ps1"));
        var installer = File.ReadAllText(Path.Combine(root, "installer", "LongAssistant.iss"));
        var updateSigner = File.ReadAllText(Path.Combine(root, "sign-update-manifest.ps1"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));

        Assert.Contains("release-policy.ps1", release);
        Assert.Contains("New-LongUnsignedReleasePolicy", release);
        Assert.Contains("release-policy.ps1", installerBuild);
        Assert.Contains("New-LongUnsignedReleasePolicy", installerBuild);
        Assert.Contains("PrivilegesRequired=lowest", installer);
        Assert.Contains("SignData($bytes, $sha256)", updateSigner);
        Assert.Contains("Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256", updateSigner);
        Assert.Contains("SmartScreen", readme);
        Assert.Contains("SHA-256", readme);
        Assert.Contains("SmartScreen", changelog);
        Assert.Contains("SHA-256", changelog);
    }

    private static JsonDocument RunPolicy(string version)
    {
        var result = RunPowerShell(version);
        Assert.True(result.ExitCode == 0, result.Error);
        return JsonDocument.Parse(result.Output);
    }

    private static ProcessResult RunPowerShell(string version)
    {
        var script = Path.Combine(FindRepositoryRoot(), "release-policy.ps1")
            .Replace("'", "''", StringComparison.Ordinal);
        var escapedVersion = version.Replace("'", "''", StringComparison.Ordinal);
        var command = $"$ErrorActionPreference='Stop'; . '{script}'; " +
            $"New-LongUnsignedReleasePolicy -Version '{escapedVersion}' | ConvertTo-Json -Compress";
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("PowerShell could not start.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000), "PowerShell policy evaluation timed out.");
        return new ProcessResult(process.ExitCode, output.Trim(), error.Trim());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
