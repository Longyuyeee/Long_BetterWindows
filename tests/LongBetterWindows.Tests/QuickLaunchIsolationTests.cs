using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.PluginSdk.Testing;
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
    public void FileSearch_RotatesAcrossRootsBeforeCandidateLimit()
    {
        var crowdedRoot = Directory.CreateDirectory(
            Path.Combine(_root, "crowded")).FullName;
        var laterRoot = Directory.CreateDirectory(
            Path.Combine(_root, "later")).FullName;
        for (var index = 0; index < 100; index++)
        {
            File.WriteAllText(
                Path.Combine(crowdedRoot, $"candidate-{index:D3}.txt"),
                "fixture");
        }
        var target = Path.Combine(laterRoot, "project-needle.txt");
        File.WriteAllText(target, "fixture");
        var engine = new QuickLaunchDiskSearchEngine(
            [crowdedRoot, laterRoot],
            [crowdedRoot, laterRoot],
            maximumFileCandidates: 2);

        var result = engine.SearchFiles("needle", 1);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(Path.GetFullPath(target), entry.Path);
        Assert.Equal(2, result.InspectedCount);
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
    public async Task StoppedPlugin_RejectsLateSearchAndTargetExecution()
    {
        var plugin = new QuickLaunchPluginImpl();
        await plugin.InitializeAsync(new PluginTestHost());
        await plugin.StartAsync();
        await plugin.StopAsync();

        var invocation = new PluginCommandInvocation
        {
            CommandId = "launcher.open",
            Text = "42",
            Arguments = new Dictionary<string, string>
            {
                ["action"] = "open-result",
                ["category"] = "calculation",
            },
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            plugin.ExecuteCommandAsync(invocation));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            plugin.SearchAsync(new PluginSearchRequest(
                "Quick Launch: note",
                5)));
    }

    [Fact]
    public async Task CommandExecution_UsesHostServicesOnlyForValidatedTargets()
    {
        var document = Path.Combine(_root, "document.txt");
        var blocked = Path.Combine(_root, "installer.application");
        File.WriteAllText(document, "document fixture");
        File.WriteAllText(blocked, "deployment fixture");
        var shell = new RecordingShellExecuteService();
        var clipboard = new RecordingClipboardService();
        var host = new PluginTestHost()
            .Grant<IShellExecuteService>(shell)
            .Grant<IClipboardService>(clipboard);
        var plugin = new QuickLaunchPluginImpl();
        Assert.True(await plugin.InitializeAsync(host));
        Assert.True(await plugin.StartAsync());

        var fileResult = await plugin.ExecuteCommandAsync(
            CreateTargetInvocation("file", document));
        var calculationResult = await plugin.ExecuteCommandAsync(
            CreateTargetInvocation("calculation", "42"));
        var blockedResult = await plugin.ExecuteCommandAsync(
            CreateTargetInvocation("file", blocked));

        Assert.True(fileResult.IsSuccess);
        Assert.True(calculationResult.IsSuccess);
        Assert.False(blockedResult.IsSuccess);
        Assert.Equal([Path.GetFullPath(document)], shell.OpenedTargets);
        Assert.Equal(["42"], clipboard.WrittenTexts);
        Assert.True(await plugin.StopAsync());
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

        foreach (var extension in new[]
                 {
                     ".application", ".appref-ms", ".pif", ".scf", ".msc",
                     ".pyw", ".jar", ".chm", ".msix", ".iso", ".docm",
                 })
        {
            var launchSurface = Path.Combine(_root, "forged" + extension);
            File.WriteAllText(launchSurface, "unsafe fixture");
            Assert.False(policy.Validate("file", launchSurface).IsValid);
        }
    }

    private static PluginCommandInvocation CreateTargetInvocation(
        string category,
        string target)
        => new()
        {
            CommandId = "launcher.open",
            Text = target,
            Arguments = new Dictionary<string, string>
            {
                ["action"] = "open-result",
                ["category"] = category,
            },
        };

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

    private sealed class RecordingShellExecuteService : IShellExecuteService
    {
        public List<string> OpenedTargets { get; } = [];

        public Task<HostApiResponse> OpenUrlAsync(string url)
            => Task.FromResult(HostApiResponse.Success());

        public Task<HostApiResponse> OpenFolderAsync(string path)
            => Task.FromResult(HostApiResponse.Success());

        public Task<HostApiResponse> OpenWithDefaultAsync(string path)
        {
            OpenedTargets.Add(path);
            return Task.FromResult(HostApiResponse.Success());
        }
    }

    private sealed class RecordingClipboardService : IClipboardService
    {
        public List<string> WrittenTexts { get; } = [];
        public bool IsMonitoring => false;
        public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged
        {
            add { }
            remove { }
        }

        public Task<HostApiResponse<string?>> GetTextAsync()
            => Task.FromResult(HostApiResponse<string?>.Success(null));

        public Task<HostApiResponse<BitmapSource?>> GetImageAsync()
            => Task.FromResult(HostApiResponse<BitmapSource?>.Success(null));

        public Task<HostApiResponse> SetTextAsync(string text)
        {
            WrittenTexts.Add(text);
            return Task.FromResult(HostApiResponse.Success());
        }

        public Task<HostApiResponse> SetImageAsync(BitmapSource image)
            => Task.FromResult(HostApiResponse.Success());

        public Task<HostApiResponse> ClearAsync()
            => Task.FromResult(HostApiResponse.Success());

        public Task<HostApiResponse> StartMonitoringAsync()
            => Task.FromResult(HostApiResponse.Success());

        public Task<HostApiResponse> StopMonitoringAsync()
            => Task.FromResult(HostApiResponse.Success());
    }
}
