using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace LongBetterWindows.Tests;

public sealed class PhysicalEvidenceGateScriptTests : IDisposable
{
    private const string Commit = "1234567890abcdef1234567890abcdef12345678";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "LongBetterWindows.PhysicalEvidence.Tests",
        Guid.NewGuid().ToString("N"));

    public PhysicalEvidenceGateScriptTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task AccessibilityVerifier_AcceptsCompleteV2Evidence()
    {
        var directories = WriteAccessibilityMatrix("accessibility-valid");
        var output = Path.Combine(_root, "accessibility-valid.json");

        var result = await RunMatrixVerifierAsync(
            "verify-accessibility-matrix.ps1",
            directories,
            output);

        Assert.True(
            result.ExitCode == 0,
            $"Exit={result.ExitCode}{Environment.NewLine}{result.StandardError}");
        using var summary = JsonDocument.Parse(File.ReadAllText(output));
        Assert.True(summary.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(3, summary.RootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal(
            1,
            summary.RootElement
                .GetProperty("screen_reader_approval_count")
                .GetInt32());
        AssertPortableSources(summary.RootElement, output, expectedCount: 3);
        AssertNoTemporaryOutputs(output);
    }

    [Fact]
    public async Task AccessibilityVerifier_RejectsV1Evidence()
    {
        var directories = WriteAccessibilityMatrix(
            "accessibility-v1",
            schemaVersion: 1);

        var result = await RunMatrixVerifierAsync(
            "verify-accessibility-matrix.ps1",
            directories,
            Path.Combine(_root, "accessibility-v1.json"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "schema version 2 is required",
            result.StandardError,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccessibilityVerifier_RejectsMissingManagementReview()
    {
        var directories = WriteAccessibilityMatrix(
            "accessibility-incomplete",
            managementTabOrderApproved: false);

        var result = await RunMatrixVerifierAsync(
            "verify-accessibility-matrix.ps1",
            directories,
            Path.Combine(_root, "accessibility-incomplete.json"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "Manual accessibility checklist is incomplete",
            result.StandardError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PhysicalDpiVerifier_AcceptsCompleteV2Evidence()
    {
        var directories = WritePhysicalDpiMatrix("dpi-valid");
        var output = Path.Combine(_root, "dpi-valid.json");

        var result = await RunMatrixVerifierAsync(
            "verify-physical-dpi-matrix.ps1",
            directories,
            output);

        Assert.True(
            result.ExitCode == 0,
            $"Exit={result.ExitCode}{Environment.NewLine}{result.StandardError}");
        using var summary = JsonDocument.Parse(File.ReadAllText(output));
        Assert.True(summary.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(3, summary.RootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal(32, summary.RootElement.GetProperty("capture_count").GetInt32());
        AssertPortableSources(summary.RootElement, output, expectedCount: 4);
        AssertNoTemporaryOutputs(output);
    }

    [Fact]
    public async Task PhysicalDpiVerifier_AllowsOnlyOneConcurrentSummaryWriter()
    {
        var directories = WritePhysicalDpiMatrix("dpi-concurrent");
        var output = Path.Combine(_root, "dpi-concurrent.json");

        var results = await Task.WhenAll(
            RunMatrixVerifierAsync(
                "verify-physical-dpi-matrix.ps1",
                directories,
                output),
            RunMatrixVerifierAsync(
                "verify-physical-dpi-matrix.ps1",
                directories,
                output));

        Assert.Single(results, result => result.ExitCode == 0);
        Assert.Single(results, result => result.ExitCode != 0);
        using var summary = JsonDocument.Parse(File.ReadAllText(output));
        AssertPortableSources(summary.RootElement, output, expectedCount: 4);
        AssertNoTemporaryOutputs(output);
    }

    [Fact]
    public async Task PhysicalDpiVerifier_RejectsV1Evidence()
    {
        var directories = WritePhysicalDpiMatrix("dpi-v1", schemaVersion: 1);

        var result = await RunMatrixVerifierAsync(
            "verify-physical-dpi-matrix.ps1",
            directories,
            Path.Combine(_root, "dpi-v1.json"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "schema version 2 is required",
            result.StandardError,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PhysicalDpiVerifier_RejectsMissingManagementReview()
    {
        var directories = WritePhysicalDpiMatrix(
            "dpi-incomplete",
            managementLayoutApproved: false);

        var result = await RunMatrixVerifierAsync(
            "verify-physical-dpi-matrix.ps1",
            directories,
            Path.Combine(_root, "dpi-incomplete.json"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "Manual physical DPI checklist is incomplete",
            result.StandardError,
            StringComparison.Ordinal);
    }

    private string[] WriteAccessibilityMatrix(
        string fixtureName,
        int schemaVersion = 2,
        bool managementTabOrderApproved = true)
    {
        var profiles = new[]
        {
            ("high_contrast", true, false),
            ("reduced_motion", false, true),
            ("combined", true, true),
        };
        return profiles.Select((profile, index) =>
        {
            var directory = Path.Combine(_root, fixtureName, profile.Item1);
            var smokeDirectory = Path.Combine(directory, "desktop-ui-smoke");
            Directory.CreateDirectory(smokeDirectory);
            var reportPath = Path.Combine(smokeDirectory, "desktop-ui-smoke.json");
            var logPath = Path.Combine(smokeDirectory, "desktop-ui-smoke.log");
            File.WriteAllText(reportPath, """{"passed":true}""");
            File.WriteAllText(logPath, "fixture log");
            var usesScreenReader = index == 0;
            var manifest = new
            {
                schema_version = schemaVersion,
                classification = "physical_accessibility_evidence",
                source_commit = Commit,
                expected_profile = profile.Item1,
                automated_checks_passed = true,
                windows_settings = new
                {
                    high_contrast = profile.Item2,
                    reduced_motion = profile.Item3,
                },
                screen_reader = new
                {
                    name = usesScreenReader ? "Narrator" : "None",
                    process_detected = usesScreenReader,
                },
                desktop_ui_report = new
                {
                    file = "desktop-ui-smoke/desktop-ui-smoke.json",
                    sha256 = Hash(reportPath),
                },
                desktop_ui_log = new
                {
                    file = "desktop-ui-smoke/desktop-ui-smoke.log",
                    sha256 = Hash(logPath),
                },
                human_review = new
                {
                    status = "approved",
                    reviewer = "fixture-reviewer",
                    reviewed_at = "2026-07-29T00:00:00Z",
                    checklist = new
                    {
                        keyboard_navigation = true,
                        focus_visibility = true,
                        motion_behavior = true,
                        management_destination_tab_order =
                            managementTabOrderApproved,
                        management_destination_activation = true,
                        management_module_close_mru = true,
                        screen_reader_announcements =
                            usesScreenReader ? true : (bool?)null,
                        management_close_announcements =
                            usesScreenReader ? true : (bool?)null,
                    },
                },
            };
            File.WriteAllText(
                Path.Combine(directory, "accessibility-evidence.json"),
                JsonSerializer.Serialize(manifest));
            return directory;
        }).ToArray();
    }

    private string[] WritePhysicalDpiMatrix(
        string fixtureName,
        int schemaVersion = 2,
        bool managementLayoutApproved = true)
    {
        return new[] { 100, 125, 150, 200 }.Select(scale =>
        {
            var directory = Path.Combine(_root, fixtureName, scale.ToString());
            Directory.CreateDirectory(directory);
            var captures = new List<object>();
            foreach (var theme in new[] { "light", "dark" })
            {
                foreach (var view in new[] { "main", "market", "palette", "plugin" })
                {
                    var fileName = $"{theme}-{scale}-{view}.png";
                    var metadataFileName = fileName + ".json";
                    var imagePath = Path.Combine(directory, fileName);
                    var metadataPath = Path.Combine(directory, metadataFileName);
                    File.WriteAllText(imagePath, $"{theme}:{scale}:{view}");
                    File.WriteAllText(metadataPath, """{"fixture":true}""");
                    captures.Add(new
                    {
                        file = fileName,
                        metadata_file = metadataFileName,
                        theme,
                        view,
                        actual_scale_percent = scale,
                        sha256 = Hash(imagePath),
                        metadata_sha256 = Hash(metadataPath),
                    });
                }
            }
            var manifest = new
            {
                schema_version = schemaVersion,
                classification = "physical_device_dpi_evidence",
                source_commit = Commit,
                expected_scale_percent = scale,
                automated_checks_passed = true,
                human_review = new
                {
                    status = "approved",
                    reviewer = "fixture-reviewer",
                    reviewed_at = "2026-07-29T00:00:00Z",
                    checklist = new
                    {
                        no_clipping_or_overflow = true,
                        text_and_icons_are_sharp = true,
                        keyboard_focus_is_visible = true,
                        light_and_dark_themes_are_consistent = true,
                        web_plugin_content_is_visible = true,
                        management_center_layout_is_stable =
                            managementLayoutApproved,
                        management_module_tabs_are_readable = true,
                    },
                },
                captures,
            };
            File.WriteAllText(
                Path.Combine(directory, "physical-dpi-evidence.json"),
                JsonSerializer.Serialize(manifest));
            return directory;
        }).ToArray();
    }

    private static async Task<ProcessResult> RunMatrixVerifierAsync(
        string script,
        IEnumerable<string> evidenceDirectories,
        string outputPath)
    {
        var scriptPath = Path.Combine(FindRepositoryRoot(), script);
        var directoryArray = string.Join(
            ",",
            evidenceDirectories.Select(QuoteForPowerShell));
        var command = string.Join(
            " ",
            "&",
            QuoteForPowerShell(scriptPath),
            "-EvidenceDirectories",
            $"@({directoryArray})",
            "-ExpectedSourceCommit",
            QuoteForPowerShell(Commit),
            "-OutputPath",
            QuoteForPowerShell(outputPath));
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-Command",
            command,
        })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("PowerShell verifier did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();

    private static void AssertPortableSources(
        JsonElement summary,
        string summaryPath,
        int expectedCount)
    {
        var evidence = summary.GetProperty("evidence").EnumerateArray().ToArray();
        Assert.Equal(expectedCount, evidence.Length);
        foreach (var entry in evidence)
        {
            var source = entry.GetProperty("source_manifest");
            var relativePath = source.GetProperty("file").GetString()!;
            Assert.DoesNotContain("..", relativePath, StringComparison.Ordinal);
            var path = Path.Combine(
                Path.GetDirectoryName(summaryPath)!,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Portable source was not found: {path}");
            Assert.Equal(source.GetProperty("sha256").GetString(), Hash(path));
        }
    }

    private static void AssertNoTemporaryOutputs(string summaryPath)
    {
        var directory = Path.GetDirectoryName(summaryPath)!;
        Assert.Empty(Directory.GetFileSystemEntries(directory, ".*.tmp"));
    }

    private static string QuoteForPowerShell(string value) =>
        $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
