using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace LongBetterWindows.Tests;

public sealed class ReleaseEvidenceIoScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "LongBetterWindows.ReleaseEvidenceIo.Tests",
        Guid.NewGuid().ToString("N"));

    public ReleaseEvidenceIoScriptTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task UpdateJsonFileAtomically_ReplacesValidatedFileWithoutResidue()
    {
        var path = WritePendingManifest();
        var expectedHash = Hash(path);

        var result = await RunUpdateAsync(path, expectedHash, "reviewer-a");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(
            "reviewer-a",
            document.RootElement.GetProperty("reviewer").GetString());
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task WriteNewTextFileAtomically_CreatesNewFileWithoutResidue()
    {
        var path = Path.Combine(_root, "SHA256SUMS.txt");

        var result = await RunWriteTextAsync(path, "abc  package.zip\n");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("abc  package.zip\n", await File.ReadAllTextAsync(path));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task WriteNewTextFileAtomically_RejectsExistingFile()
    {
        var path = Path.Combine(_root, "SHA256SUMS.txt");
        await File.WriteAllTextAsync(path, "old");

        var result = await RunWriteTextAsync(path, "new");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("already exists", result.Error);
        Assert.Equal("old", await File.ReadAllTextAsync(path));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task UpdateTextFileAtomically_ReplacesValidatedFileWithoutResidue()
    {
        var path = Path.Combine(_root, "SHA256SUMS.txt");
        await File.WriteAllTextAsync(path, "old");
        var expectedHash = Hash(path);

        var result = await RunUpdateTextAsync(path, expectedHash, "new\n");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("new\n", await File.ReadAllTextAsync(path));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task UpdateTextFileAtomically_RejectsChangedFileWithoutOverwriting()
    {
        var path = Path.Combine(_root, "SHA256SUMS.txt");
        await File.WriteAllTextAsync(path, "old");
        var staleHash = Hash(path);
        await File.WriteAllTextAsync(path, "changed");

        var result = await RunUpdateTextAsync(path, staleHash, "new\n");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("changed after validation", result.Error);
        Assert.Equal("changed", await File.ReadAllTextAsync(path));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task UpdateJsonFileAtomically_RejectsChangedFileWithoutOverwriting()
    {
        var path = WritePendingManifest();
        var staleHash = Hash(path);
        const string changedContent = """{"status":"changed"}""";
        await File.WriteAllTextAsync(path, changedContent);

        var result = await RunUpdateAsync(path, staleHash, "reviewer-a");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("changed after validation", result.Error);
        Assert.Equal(changedContent, await File.ReadAllTextAsync(path));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task UpdateJsonFileAtomically_AllowsOnlyOneConcurrentReviewer()
    {
        var path = WritePendingManifest();
        var expectedHash = Hash(path);

        var results = await Task.WhenAll(
            RunUpdateAsync(path, expectedHash, "reviewer-a"),
            RunUpdateAsync(path, expectedHash, "reviewer-b"));

        Assert.Single(results, result => result.ExitCode == 0);
        Assert.Single(results, result => result.ExitCode != 0);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Contains(
            document.RootElement.GetProperty("reviewer").GetString(),
            new[] { "reviewer-a", "reviewer-b" });
        AssertNoTemporaryFiles();
    }

    private string WritePendingManifest()
    {
        var path = Path.Combine(_root, "evidence.json");
        File.WriteAllText(path, """{"status":"pending"}""");
        return path;
    }

    private static async Task<ProcessResult> RunUpdateAsync(
        string path,
        string expectedHash,
        string reviewer)
    {
        var helper = Path.Combine(FindRepositoryRoot(), "release-evidence-io.ps1");
        var command =
            "$ErrorActionPreference = 'Stop'; " +
            $". {Quote(helper)}; " +
            $"$value = [ordered]@{{ reviewer = {Quote(reviewer)} }}; " +
            $"Update-JsonFileAtomically -Value $value -Path {Quote(path)} " +
            $"-ExpectedSha256 {Quote(expectedHash)} -Depth 4 -Label 'Test evidence'";
        return await RunPowerShellAsync(command);
    }

    private static Task<ProcessResult> RunWriteTextAsync(
        string path,
        string value)
    {
        var helper = Path.Combine(FindRepositoryRoot(), "release-evidence-io.ps1");
        var command =
            "$ErrorActionPreference = 'Stop'; " +
            $". {Quote(helper)}; " +
            $"Write-NewTextFileAtomically -Value {Quote(value)} " +
            $"-Path {Quote(path)} -Label 'Test ledger'";
        return RunPowerShellAsync(command);
    }

    private static Task<ProcessResult> RunUpdateTextAsync(
        string path,
        string expectedHash,
        string value)
    {
        var helper = Path.Combine(FindRepositoryRoot(), "release-evidence-io.ps1");
        var command =
            "$ErrorActionPreference = 'Stop'; " +
            $". {Quote(helper)}; " +
            $"Update-TextFileAtomically -Value {Quote(value)} " +
            $"-Path {Quote(path)} -ExpectedSha256 {Quote(expectedHash)} " +
            "-Label 'Test ledger'";
        return RunPowerShellAsync(command);
    }

    private static async Task<ProcessResult> RunPowerShellAsync(string command)
    {
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
            ?? throw new InvalidOperationException("PowerShell command did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private void AssertNoTemporaryFiles() =>
        Assert.Empty(Directory.GetFileSystemEntries(_root, ".*"));

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();

    private static string Quote(string value) =>
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

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
