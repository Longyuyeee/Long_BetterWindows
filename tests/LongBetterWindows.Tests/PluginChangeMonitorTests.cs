using System.Collections.Concurrent;
using System.IO;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public class PluginChangeMonitorTests
{
    [Fact]
    public async Task RepeatedPath_IsDebouncedIntoSingleChange()
    {
        var changes = new ConcurrentQueue<PluginFileChange>();
        using var signal = new SemaphoreSlim(0);
        using var monitor = CreateMonitor(changes, signal);
        var path = Path.Combine(Path.GetTempPath(), "plugin.dll");

        monitor.NotifyChanged(path);
        monitor.NotifyChanged(path);

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(2)));
        await Task.Delay(80);
        var change = Assert.Single(changes);
        Assert.Equal(Path.GetFullPath(path), change.NewPath);
    }

    [Fact]
    public async Task DifferentPaths_DoNotCancelEachOther()
    {
        var changes = new ConcurrentQueue<PluginFileChange>();
        using var signal = new SemaphoreSlim(0);
        using var monitor = CreateMonitor(changes, signal);
        var first = Path.Combine(Path.GetTempPath(), "one.dll");
        var second = Path.Combine(Path.GetTempPath(), "two.dll");

        monitor.NotifyChanged(first);
        monitor.NotifyChanged(second);

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(
            new[] { Path.GetFullPath(first), Path.GetFullPath(second) },
            changes.Select(change => change.NewPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Rename_PreservesOldAndNewPluginPathsAsOneChange()
    {
        var changes = new ConcurrentQueue<PluginFileChange>();
        using var signal = new SemaphoreSlim(0);
        using var monitor = CreateMonitor(changes, signal);
        var oldPath = Path.Combine(Path.GetTempPath(), "old.csx");
        var newPath = Path.Combine(Path.GetTempPath(), "new.csx");

        monitor.NotifyRenamed(oldPath, newPath);

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(2)));
        var change = Assert.Single(changes);
        Assert.Equal(Path.GetFullPath(oldPath), change.OldPath);
        Assert.Equal(Path.GetFullPath(newPath), change.NewPath);
    }

    [Fact]
    public async Task NonPluginFiles_AreIgnored()
    {
        var changes = new ConcurrentQueue<PluginFileChange>();
        using var signal = new SemaphoreSlim(0);
        using var monitor = CreateMonitor(changes, signal);

        monitor.NotifyChanged(Path.Combine(Path.GetTempPath(), "readme.txt"));

        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(150)));
        Assert.Empty(changes);
    }

    private static PluginChangeMonitor CreateMonitor(
        ConcurrentQueue<PluginFileChange> changes,
        SemaphoreSlim signal)
        => new(
            Array.Empty<string>(),
            change =>
            {
                changes.Enqueue(change);
                signal.Release();
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(30));
}
