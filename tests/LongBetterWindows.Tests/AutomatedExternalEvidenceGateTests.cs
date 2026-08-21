using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LongBetterWindows.Tests;

public sealed class AutomatedExternalEvidenceGateTests : IDisposable
{
    private const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "long-a009-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PhysicalDpiMatrix_VerifiesRealFilesAcrossFourScales()
    {
        var directories = new[] { 100, 125, 150, 200 }
            .Select(WritePhysicalDpiEvidence)
            .ToArray();
        var output = Path.Combine(_root, "dpi-summary.json");

        var result = await RunMatrixAsync(
            "verify-physical-dpi-matrix.ps1",
            directories,
            output);

        AssertSuccess(result);
        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.Equal(4, summary.RootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal(32, summary.RootElement.GetProperty("capture_count").GetInt32());
        Assert.Equal("automated_physical_device_dpi_matrix", summary.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public async Task PhysicalDpiMatrix_RejectsTamperedCaptureBytes()
    {
        var directories = new[] { 100, 125, 150, 200 }
            .Select(WritePhysicalDpiEvidence)
            .ToArray();
        await File.AppendAllTextAsync(Path.Combine(directories[2], "light-main.png"), "tampered");

        var result = await RunMatrixAsync(
            "verify-physical-dpi-matrix.ps1",
            directories,
            Path.Combine(_root, "dpi-tampered.json"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Evidence hash mismatch", result.Error);
    }

    [Fact]
    public async Task AccessibilityMatrix_VerifiesRealSettingsAndUiaEventFiles()
    {
        var directories = new[] { "high_contrast", "reduced_motion", "combined" }
            .Select(WriteAccessibilityEvidence)
            .ToArray();
        var output = Path.Combine(_root, "accessibility-summary.json");

        var result = await RunMatrixAsync(
            "verify-accessibility-matrix.ps1",
            directories,
            output);

        AssertSuccess(result);
        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.Equal(5, summary.RootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal(3, summary.RootElement.GetProperty("uia_event_profile_count").GetInt32());
        Assert.Equal("automated_physical_accessibility_matrix", summary.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public async Task CleanEnvironmentGate_VerifiesHashLockedAutomatedLifecycle()
    {
        var directory = WriteCleanEnvironmentEvidence();
        var output = Path.Combine(directory, "clean-summary.json");

        var result = await RunAsync(
            "verify-clean-environment-evidence.ps1",
            ["-EvidenceDirectory", directory, "-ExpectedSourceCommit", Commit,
                "-ExpectedDistributionChannel", "unsigned", "-OutputPath", output]);

        AssertSuccess(result);
        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.Equal(3, summary.RootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal("automated_clean_windows_release_gate", summary.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public async Task ReleaseDownloadGate_VerifiesProvenanceWithoutApprovalFile()
    {
        Directory.CreateDirectory(_root);
        var evidence = Path.Combine(_root, "download-evidence.json");
        var output = Path.Combine(_root, "download-summary.json");
        WriteJson(evidence, D(
            ("schema_version", 2),
            ("classification", "automated_release_download_provenance"),
            ("passed", true),
            ("release", D(
                ("version", "1.11.0"),
                ("source_commit", Commit),
                ("distribution_channel", "unsigned"),
                ("signed", false),
                ("release_eligible", true))),
            ("package", D(
                ("file", "LongAssistant.zip"),
                ("kind", "self-contained"),
                ("bytes", 512),
                ("sha256", new string('b', 64)))),
            ("windows_origin", D(
                ("zone_id", 3),
                ("host", D(("scheme", "https"), ("host", "github.com"), ("path", "/release.zip"))),
                ("referrer", null),
                ("zone_identifier_sha256", new string('c', 64)),
                ("query_parameters_recorded", false)))));

        var result = await RunAsync(
            "verify-release-download-evidence.ps1",
            ["-EvidencePath", evidence, "-ExpectedSourceCommit", Commit,
                "-ExpectedDistributionChannel", "unsigned", "-OutputPath", output]);

        AssertSuccess(result);
        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.Equal(4, summary.RootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal("automated_release_download_gate", summary.RootElement.GetProperty("classification").GetString());
        Assert.Equal(Hash(await File.ReadAllBytesAsync(evidence)), summary.RootElement.GetProperty("evidence").GetProperty("sha256").GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string WritePhysicalDpiEvidence(int scale)
    {
        var directory = Path.Combine(_root, $"dpi-{scale}");
        Directory.CreateDirectory(directory);
        var captures = new List<object>();
        foreach (var theme in new[] { "light", "dark" })
        foreach (var view in new[] { "main", "market", "palette", "plugin" })
        {
            var file = $"{theme}-{view}.png";
            var metadata = file + ".json";
            File.WriteAllBytes(Path.Combine(directory, file), Encoding.UTF8.GetBytes($"pixels:{scale}:{theme}:{view}"));
            File.WriteAllText(Path.Combine(directory, metadata), $"{{\"scale\":{scale}}}");
            captures.Add(D(
                ("file", file),
                ("metadata_file", metadata),
                ("theme", theme),
                ("view", view),
                ("actual_scale_percent", scale),
                ("sha256", Hash(File.ReadAllBytes(Path.Combine(directory, file)))),
                ("metadata_sha256", Hash(File.ReadAllBytes(Path.Combine(directory, metadata))))));
        }
        WriteJson(Path.Combine(directory, "physical-dpi-evidence.json"), D(
            ("schema_version", 3),
            ("classification", "automated_physical_device_dpi_evidence"),
            ("source_commit", Commit),
            ("expected_scale_percent", scale),
            ("automated_checks_passed", true),
            ("captures", captures)));
        return directory;
    }

    private string WriteAccessibilityEvidence(string profile)
    {
        var directory = Path.Combine(_root, $"a11y-{profile}");
        var smoke = Path.Combine(directory, "desktop-ui-smoke");
        Directory.CreateDirectory(smoke);
        var reportPath = Path.Combine(smoke, "desktop-ui-smoke.json");
        var logPath = Path.Combine(smoke, "desktop-ui-smoke.log");
        WriteJson(reportPath, D(("assistive_technology_events", D(
            ("passed", true), ("focus_event_count", 4), ("live_region_event_count", 3)))));
        File.WriteAllText(logPath, $"uia events for {profile}");
        var highContrast = profile is "high_contrast" or "combined";
        var reducedMotion = profile is "reduced_motion" or "combined";
        WriteJson(Path.Combine(directory, "accessibility-evidence.json"), D(
            ("schema_version", 4),
            ("classification", "automated_physical_accessibility_evidence"),
            ("source_commit", Commit),
            ("expected_profile", profile),
            ("automated_checks_passed", true),
            ("windows_settings", D(("high_contrast", highContrast), ("reduced_motion", reducedMotion))),
            ("screen_reader", D(("name", "None"), ("process_detected", false))),
            ("desktop_ui_report", D(("file", "desktop-ui-smoke/desktop-ui-smoke.json"), ("sha256", Hash(File.ReadAllBytes(reportPath))))),
            ("desktop_ui_log", D(("file", "desktop-ui-smoke/desktop-ui-smoke.log"), ("sha256", Hash(File.ReadAllBytes(logPath))))),
            ("assistive_technology_events", D(
                ("transport", "windows_ui_automation_events"),
                ("physical_keyboard_validated", true),
                ("focus_event_count", 4),
                ("live_region_event_count", 3),
                ("screen_reader_active_during_capture", false)))));
        return directory;
    }

    private string WriteCleanEnvironmentEvidence()
    {
        var directory = Path.Combine(_root, "clean-environment");
        var smoke = Path.Combine(directory, "desktop-ui-smoke");
        Directory.CreateDirectory(smoke);
        var releasePath = Path.Combine(directory, "release-manifest.json");
        var reportPath = Path.Combine(smoke, "desktop-ui-smoke.json");
        var logPath = Path.Combine(smoke, "desktop-ui-smoke.log");
        var commandPath = Path.Combine(directory, "command-smoke.log");
        WriteJson(releasePath, D(
            ("commit", Commit), ("distribution_channel", "unsigned"),
            ("release_eligible", true), ("signed", false),
            ("packages", new[] { D(("file", "candidate.zip"), ("sha256", new string('d', 64))) })));
        File.WriteAllText(reportPath, "{\"passed\":true}");
        File.WriteAllText(logPath, "desktop passed");
        File.WriteAllText(commandPath, "command passed");
        WriteJson(Path.Combine(directory, "clean-environment-evidence.json"), D(
            ("schema_version", 2),
            ("classification", "automated_clean_windows_release_evidence"),
            ("environment", D(("label", "fixture-vm"), ("operator_asserted_clean_user", true), ("interactive", true))),
            ("release", D(
                ("version", "1.11.0"), ("source_commit", Commit),
                ("distribution_channel", "unsigned"), ("package_file", "candidate.zip"),
                ("package_sha256", new string('d', 64)), ("signed", false),
                ("release_eligible", true),
                ("release_manifest", FileRef("release-manifest.json", releasePath)))),
            ("automated_checks", D(
                ("passed", true),
                ("desktop_ui_report", FileRef("desktop-ui-smoke/desktop-ui-smoke.json", reportPath)),
                ("desktop_ui_log", FileRef("desktop-ui-smoke/desktop-ui-smoke.log", logPath)),
                ("command_log", FileRef("command-smoke.log", commandPath))))));
        return directory;
    }

    private static Dictionary<string, object?> FileRef(string file, string path)
        => D(("file", file), ("sha256", Hash(File.ReadAllBytes(path))));

    private static Dictionary<string, object?> D(params (string Key, object? Value)[] values)
        => values.ToDictionary(value => value.Key, value => value.Value);

    private static void WriteJson(string path, object value)
        => File.WriteAllText(path, JsonSerializer.Serialize(value));

    private static string Hash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void AssertSuccess(ProcessResult result)
        => Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\nSTDOUT:\n{result.Output}\nSTDERR:\n{result.Error}");

    private static async Task<ProcessResult> RunAsync(string script, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = FindRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(FindRepositoryRoot(), script));
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start PowerShell.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private static async Task<ProcessResult> RunMatrixAsync(
        string script,
        IReadOnlyList<string> evidenceDirectories,
        string outputPath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var directoryArray = string.Join(",", evidenceDirectories.Select(QuotePowerShell));
        var command = $"& {QuotePowerShell(Path.Combine(repositoryRoot, script))} " +
                      $"-EvidenceDirectories @({directoryArray}) " +
                      $"-ExpectedSourceCommit {QuotePowerShell(Commit)} " +
                      $"-OutputPath {QuotePowerShell(outputPath)}";
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(encodedCommand);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start PowerShell.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private static string QuotePowerShell(string value)
        => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
