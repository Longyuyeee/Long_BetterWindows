using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public class PluginRegistryBatchTests
{
    [Fact]
    public void ChangeBatch_CoalescesMultipleRegistryChanges()
    {
        var registry = new PluginRegistry();
        var changes = 0;
        registry.PluginsChanged += () => changes++;

        using (registry.BeginChangeBatch())
        {
            Assert.True(registry.RegisterDeferred(
                CreateManifest("batch.first"),
                "first",
                _ => Task.FromResult<object?>(new object())));
            Assert.True(registry.RegisterDeferred(
                CreateManifest("batch.second"),
                "second",
                _ => Task.FromResult<object?>(new object())));
            Assert.True(registry.SetState(
                "batch.first",
                LongBetterWindows.Host.Core.PluginState.Stopped));
            Assert.Equal(0, changes);
        }

        Assert.Equal(1, changes);
        Assert.Equal(2, registry.Count);
    }

    [Fact]
    public void ChangeBatch_NestsAndDoesNotPublishWhenNothingChanged()
    {
        var registry = new PluginRegistry();
        var changes = 0;
        registry.PluginsChanged += () => changes++;

        using (registry.BeginChangeBatch())
        {
            using (registry.BeginChangeBatch())
            {
                Assert.True(registry.RegisterDeferred(
                    CreateManifest("batch.nested"),
                    "nested",
                    _ => Task.FromResult<object?>(new object())));
            }
            Assert.Equal(0, changes);
        }
        Assert.Equal(1, changes);

        using (registry.BeginChangeBatch())
        {
        }
        Assert.Equal(1, changes);
    }

    private static PluginManifest CreateManifest(string id)
        => new()
        {
            Id = id,
            Name = id,
            Version = "1.0.0",
            EntryPoint = "plugin.dll",
        };
}
