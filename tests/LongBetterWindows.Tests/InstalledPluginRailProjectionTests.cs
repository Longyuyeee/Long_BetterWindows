using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class InstalledPluginRailProjectionTests
{
    [Fact]
    public void Build_SortsFiltersAndProjectsRuntimeAndUpdateState()
    {
        var running = Entry(
            "com.long.zeta",
            "Zeta",
            "1.0.0",
            PluginState.Running,
            revision: 4);
        var matching = Entry(
            "com.long.alpha-tools",
            "Alpha",
            "1.0.0",
            PluginState.Stopped,
            revision: 2);
        var catalog = new[]
        {
            new MarketplaceEntry
            {
                Id = matching.Id,
                Versions =
                [
                    new MarketplacePackageVersion { Version = "2.0.0" },
                ],
            },
        };

        var all = InstalledPluginRailProjection.Build(
            [running, matching],
            catalog,
            localize: key => $"localized:{key}");
        var filtered = InstalledPluginRailProjection.Build(
            [running, matching],
            catalog,
            query: "alpha",
            activePluginId: matching.Id);

        Assert.Equal(new[] { "Alpha", "Zeta" }, all.Select(item => item.Name));
        var item = Assert.Single(filtered);
        Assert.True(item.HasUpdate);
        Assert.True(item.IsActive);
        Assert.False(item.IsRunning);
        Assert.Equal("A", item.Monogram);
        Assert.Equal(2, item.RegistrationRevision);
    }

    [Fact]
    public void Reconcile_PreservesUnchangedRowsAndOnlyReplacesChangedRows()
    {
        var alpha = Item("alpha", "Alpha");
        var beta = Item("beta", "Beta");
        var current = new List<InstalledPluginRailItem> { beta, alpha };
        var changedBeta = beta with { IsRunning = true, StatusText = "Running" };

        var result = InstalledPluginRailProjection.Reconcile(
            current,
            [alpha, changedBeta, Item("gamma", "Gamma")]);

        Assert.Same(alpha, current[0]);
        Assert.Same(changedBeta, current[1]);
        Assert.Equal("gamma", current[2].Id);
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Moved);
        Assert.Equal(1, result.Replaced);
        Assert.Equal(0, result.Removed);
    }

    [Fact]
    public void Reconcile_RemovesUninstalledRowsWithoutRebuildingSurvivors()
    {
        var alpha = Item("alpha", "Alpha");
        var beta = Item("beta", "Beta");
        var current = new List<InstalledPluginRailItem> { alpha, beta };

        var result = InstalledPluginRailProjection.Reconcile(current, [beta]);

        Assert.Single(current);
        Assert.Same(beta, current[0]);
        Assert.Equal(1, result.Removed);
        Assert.Equal(0, result.Replaced);
    }

    [Fact]
    public void Build_MissingIconKeepsDeterministicMonogramFallback()
    {
        var entry = Entry(
            "com.long.notes",
            "Notes",
            "1.0.0",
            PluginState.Loaded,
            revision: 1,
            directory: Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N")));

        var item = Assert.Single(InstalledPluginRailProjection.Build([entry]));

        Assert.Null(item.IconPath);
        Assert.Equal("N", item.Monogram);
    }

    private static PluginEntry Entry(
        string id,
        string name,
        string version,
        PluginState state,
        long revision,
        string? directory = null)
        => new(
            new PluginManifest
            {
                Id = id,
                Name = name,
                Version = version,
                EntryPoint = $"{id}.dll",
            },
            new object(),
            directory ?? Path.GetTempPath(),
            revision)
        {
            State = state,
        };

    private static InstalledPluginRailItem Item(string id, string name)
        => new(
            id,
            name,
            "1.0.0",
            PluginState.Stopped,
            IsRunning: false,
            HasUpdate: false,
            CanOpen: true,
            IsActive: false,
            name[..1],
            IconPath: null,
            StatusText: "Stopped",
            RegistrationRevision: 1);
}
