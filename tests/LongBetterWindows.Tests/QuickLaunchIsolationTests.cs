using System.IO;
using System.Security.Cryptography;
using System.Text;
using QuickLaunchPlugin;

namespace LongBetterWindows.Tests;

public sealed class QuickLaunchIsolationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "LongAssistant-QuickLaunch-Isolation",
        Guid.NewGuid().ToString("N"));

    public QuickLaunchIsolationTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void LargeDirectorySearch_FindsNestedTargetWithoutMutation()
    {
        for (var directory = 0; directory < 30; directory++)
        {
            var current = Directory.CreateDirectory(
                Path.Combine(_root, $"group-{directory:D2}")).FullName;
            for (var file = 0; file < 40; file++)
            {
                File.WriteAllText(
                    Path.Combine(current, $"bulk-{directory:D2}-{file:D2}.txt"),
                    "fixture");
            }
        }
        var target = Path.Combine(
            _root,
            "group-17",
            "project-needle.txt");
        File.WriteAllText(target, "needle content");
        var fingerprintBefore = GetFingerprint(_root);
        var engine = new QuickLaunchDiskSearchEngine(
            [_root],
            [_root]);

        var result = engine.SearchFiles("needle", 3);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(Path.GetFullPath(target), entry.Path);
        Assert.InRange(result.InspectedCount, 1, 5_000);
        Assert.False(result.CandidateLimitReached);
        Assert.Equal(fingerprintBefore, GetFingerprint(_root));
    }

    [Fact]
    public void FileSearch_StopsAtGlobalCandidateLimit()
    {
        for (var index = 0; index < 200; index++)
        {
            File.WriteAllText(
                Path.Combine(_root, $"candidate-{index:D3}.txt"),
                "fixture");
        }
        var engine = new QuickLaunchDiskSearchEngine(
            [_root],
            [_root],
            maximumFileCandidates: 40);

        var result = engine.SearchFiles("not-present", 3);

        Assert.Empty(result.Entries);
        Assert.Equal(40, result.InspectedCount);
        Assert.True(result.CandidateLimitReached);
    }

    [Fact]
    public void DiskSearch_ObservesSupersedingCancellation()
    {
        File.WriteAllText(Path.Combine(_root, "needle.txt"), "fixture");
        var engine = new QuickLaunchDiskSearchEngine([_root], [_root]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            engine.SearchFiles("needle", 3, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() =>
            engine.SearchContent("fixture", 3, cancellation.Token));
    }

    [Fact]
    public void QueryGeneration_RejectsStaleSameTextRequest()
    {
        var generation = new QuickLaunchQueryGeneration();

        var firstA = generation.Begin();
        var second = generation.Begin();
        var latestA = generation.Begin();

        Assert.False(generation.IsCurrent(firstA));
        Assert.False(generation.IsCurrent(second));
        Assert.True(generation.IsCurrent(latestA));
        generation.Invalidate();
        Assert.False(generation.IsCurrent(latestA));
    }

    [Fact]
    public void TargetPolicy_AllowsOnlyProducedHarmlessTargetShapes()
    {
        var startMenu = Directory.CreateDirectory(
            Path.Combine(_root, "StartMenu")).FullName;
        var shortcut = Path.Combine(startMenu, "Fixture.lnk");
        var document = Path.Combine(_root, "document.txt");
        File.WriteAllText(shortcut, "shortcut fixture");
        File.WriteAllText(document, "document fixture");
        var policy = new QuickLaunchTargetPolicy([startMenu]);

        Assert.True(policy.Validate("application", shortcut).IsValid);
        Assert.True(policy.Validate("file", document).IsValid);
        Assert.True(policy.Validate("content", document).IsValid);
        Assert.True(policy.Validate(
            "link",
            "https://example.test/path").IsValid);
        Assert.True(policy.Validate("calculation", "42").IsValid);
    }

    [Fact]
    public void TargetPolicy_RejectsForgedOrMissingTargets()
    {
        var startMenu = Directory.CreateDirectory(
            Path.Combine(_root, "StartMenu")).FullName;
        var outsideShortcut = Path.Combine(_root, "Outside.lnk");
        var executable = Path.Combine(_root, "forged.cmd");
        File.WriteAllText(outsideShortcut, "shortcut fixture");
        File.WriteAllText(executable, "echo unsafe");
        var policy = new QuickLaunchTargetPolicy([startMenu]);

        Assert.False(policy.Validate(
            "application",
            outsideShortcut).IsValid);
        Assert.False(policy.Validate(
            "application",
            Path.Combine(startMenu, "missing.lnk")).IsValid);
        Assert.False(policy.Validate(
            "link",
            "file:///C:/Windows/System32/cmd.exe").IsValid);
        Assert.False(policy.Validate(
            "unknown",
            outsideShortcut).IsValid);
        Assert.False(policy.Validate("file", executable).IsValid);
    }

    private static string GetFingerprint(string root)
    {
        var lines = Directory
            .EnumerateFileSystemEntries(
                root,
                "*",
                SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var relative = Path.GetRelativePath(root, path)
                    .Replace(Path.DirectorySeparatorChar, '/');
                if (Directory.Exists(path))
                    return "D:" + relative;
                var hash = Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(path)));
                return $"F:{relative}:{hash}";
            });
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Join('\n', lines))));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
