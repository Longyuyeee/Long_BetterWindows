using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class WidgetLayoutCoordinatorTests
{
    [Fact]
    public void CatalogProjection_UsesOnlyWebWidgetsAndResolvesSafeIcons()
    {
        var root = CreateRoot();
        try
        {
            var iconPath = Path.Combine(root, "widget.png");
            File.WriteAllText(iconPath, "icon");
            var webEntry = CreateEntry(
                root,
                "com.test.web",
                CreateWidget("status", multipleInstances: false, icon: "widget.png"));
            var nativeManifest = new PluginManifest
            {
                Id = "com.test.native",
                Name = "Native",
                Version = "1.0.0",
                Runtime = "native",
                EntryPoint = "Native.dll",
                Widgets = [CreateWidget("ignored", false)],
            };
            var nativeEntry = new PluginEntry(nativeManifest, new object(), root, 2);

            var item = Assert.Single(WidgetCatalogProjection.Build(
                [nativeEntry, webEntry]));

            Assert.Equal("com.test.web", item.PluginId);
            Assert.Equal("status", item.WidgetId);
            Assert.Equal(Path.GetFullPath(iconPath), item.IconPath);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task LayoutStore_RoundTripsAtomicVersionedDocument()
    {
        var root = CreateRoot();
        try
        {
            var store = new WidgetLayoutStore(root);
            var snapshot = new WidgetLayoutSnapshot(
                7,
                [
                    new WidgetPlacement(
                        "instance-1",
                        "com.test.web",
                        "status",
                        2,
                        3,
                        4,
                        2),
                ]);

            var saved = await store.SaveAsync(snapshot);
            var loaded = await store.LoadAsync();

            Assert.True(saved.IsSuccess, saved.Error);
            Assert.True(loaded.IsSuccess, loaded.Error);
            AssertSnapshotsEqual(snapshot, loaded.Snapshot);
            Assert.True(File.Exists(store.LayoutPath));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
            var json = await File.ReadAllTextAsync(store.LayoutPath);
            Assert.Contains("\"schema_version\": 1", json);
            Assert.Contains("\"revision\": 7", json);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task LayoutStore_RejectsCorruptOrOutOfRangeDocuments()
    {
        var root = CreateRoot();
        try
        {
            var store = new WidgetLayoutStore(root);
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                store.LayoutPath,
                """
                {
                  "schema_version": 1,
                  "revision": 1,
                  "placements": [{
                    "instance_id": "instance-1",
                    "plugin_id": "com.test.web",
                    "widget_id": "status",
                    "column": 23,
                    "row": 0,
                    "columns": 2,
                    "rows": 1
                  }]
                }
                """);

            var invalidPlacement = await store.LoadAsync();
            Assert.False(invalidPlacement.IsSuccess);
            Assert.Empty(invalidPlacement.Snapshot.Placements);

            await File.WriteAllTextAsync(store.LayoutPath, "{not-json");
            var corrupt = await store.LoadAsync();
            Assert.False(corrupt.IsSuccess);
            Assert.Contains("could not be read", corrupt.Error);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Coordinator_AddsPersistsAndRemovesSingleInstance()
    {
        var root = CreateRoot();
        try
        {
            var catalog = WidgetCatalogProjection.Build(
                [CreateEntry(root, "com.test.web", CreateWidget("status", false))]);
            var store = new WidgetLayoutStore(root);
            var coordinator = new WidgetLayoutCoordinator(() => catalog, store);

            Assert.True((await coordinator.LoadAsync()).IsSuccess);
            var added = await coordinator.AddAsync("com.test.web", "status");
            var duplicate = await coordinator.AddAsync("com.test.web", "status");

            Assert.True(added.IsSuccess, added.TechnicalError);
            Assert.Equal(1, added.Snapshot.Revision);
            Assert.Equal(0, added.Placement!.Column);
            Assert.Equal(0, added.Placement.Row);
            Assert.Equal(2, added.Placement.Columns);
            Assert.False(duplicate.IsSuccess);
            Assert.Equal(
                WidgetLayoutMutationError.MultipleInstancesNotAllowed,
                duplicate.Error);
            Assert.Equal(added.Snapshot, duplicate.Snapshot);

            var reloaded = new WidgetLayoutCoordinator(() => catalog, store);
            Assert.True((await reloaded.LoadAsync()).IsSuccess);
            AssertSnapshotsEqual(added.Snapshot, reloaded.Snapshot);

            var removed = await reloaded.RemoveAsync(added.Placement.InstanceId);
            Assert.True(removed.IsSuccess);
            Assert.Empty(removed.Snapshot.Placements);
            Assert.Equal(2, removed.Snapshot.Revision);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Coordinator_PlacesMultipleInstancesAndRejectsOverlapOrBadSize()
    {
        var root = CreateRoot();
        try
        {
            var widget = CreateWidget("status", multipleInstances: true);
            var catalog = WidgetCatalogProjection.Build(
                [CreateEntry(root, "com.test.web", widget)]);
            var coordinator = new WidgetLayoutCoordinator(
                () => catalog,
                new WidgetLayoutStore(root));
            await coordinator.LoadAsync();

            var first = await coordinator.AddAsync("com.test.web", "status");
            var second = await coordinator.AddAsync("com.test.web", "status");
            Assert.True(first.IsSuccess);
            Assert.True(second.IsSuccess);
            Assert.Equal(2, second.Placement!.Column);

            var overlap = await coordinator.MoveResizeAsync(
                second.Placement.InstanceId,
                column: 0,
                row: 0,
                columns: 2,
                rows: 1);
            Assert.False(overlap.IsSuccess);
            Assert.Equal(WidgetLayoutMutationError.PlacementOccupied, overlap.Error);
            Assert.Equal(2, overlap.Snapshot.Revision);

            var badSize = await coordinator.MoveResizeAsync(
                second.Placement.InstanceId,
                column: 4,
                row: 0,
                columns: 5,
                rows: 1);
            Assert.False(badSize.IsSuccess);
            Assert.Equal(WidgetLayoutMutationError.SizeOutOfRange, badSize.Error);

            var moved = await coordinator.MoveResizeAsync(
                second.Placement.InstanceId,
                column: 4,
                row: 1,
                columns: 4,
                rows: 2);
            Assert.True(moved.IsSuccess);
            Assert.Equal(3, moved.Snapshot.Revision);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Coordinator_ReconcilesMissingDuplicateAndOverlappingInstances()
    {
        var root = CreateRoot();
        try
        {
            var catalog = WidgetCatalogProjection.Build(
                [CreateEntry(root, "com.test.web", CreateWidget("status", false))]);
            var store = new WidgetLayoutStore(root);
            var seed = new WidgetLayoutSnapshot(
                4,
                [
                    new WidgetPlacement(
                        "instance-1",
                        "com.test.web",
                        "status",
                        0,
                        0,
                        2,
                        1),
                    new WidgetPlacement(
                        "instance-2",
                        "com.test.web",
                        "status",
                        2,
                        0,
                        2,
                        1),
                    new WidgetPlacement(
                        "instance-3",
                        "missing.plugin",
                        "missing",
                        4,
                        0,
                        1,
                        1),
                ]);
            Assert.True((await store.SaveAsync(seed)).IsSuccess);

            var coordinator = new WidgetLayoutCoordinator(() => catalog, store);
            var loaded = await coordinator.LoadAsync();

            Assert.True(loaded.IsSuccess, loaded.Error);
            var placement = Assert.Single(loaded.Snapshot.Placements);
            Assert.Equal("instance-1", placement.InstanceId);
            Assert.Equal(5, loaded.Snapshot.Revision);
            AssertSnapshotsEqual(
                loaded.Snapshot,
                (await store.LoadAsync()).Snapshot);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static PluginEntry CreateEntry(
        string directory,
        string pluginId,
        PluginWidgetDefinition widget)
    {
        var manifest = new PluginManifest
        {
            Id = pluginId,
            Name = pluginId,
            Version = "1.0.0",
            Runtime = "webview",
            EntryPoint = "index.html",
            MinApiVersion = "1.1.0",
            Widgets = [widget],
        };
        return new PluginEntry(manifest, new object(), directory, 1);
    }

    private static PluginWidgetDefinition CreateWidget(
        string widgetId,
        bool multipleInstances,
        string? icon = null)
        => new()
        {
            Id = widgetId,
            Title = "System status",
            Description = "Shows system status.",
            EntryPoint = "widgets/status.html",
            Icon = icon,
            MultipleInstances = multipleInstances,
            DefaultSize = new PluginWidgetSize { Columns = 2, Rows = 1 },
            MinSize = new PluginWidgetSize { Columns = 1, Rows = 1 },
            MaxSize = new PluginWidgetSize { Columns = 4, Rows = 2 },
        };

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"long-widget-layout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
        }
    }

    private static void AssertSnapshotsEqual(
        WidgetLayoutSnapshot expected,
        WidgetLayoutSnapshot actual)
    {
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.Placements, actual.Placements);
    }
}
