using System.IO;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class FileSystemOrganizationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"long-file-organization-{Guid.NewGuid():N}");

    public FileSystemOrganizationTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task PlanAndExecute_OrganizesTopLevelFilesWithoutOverwrite()
    {
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "notes");
        File.WriteAllText(Path.Combine(_root, "image.png"), "image");
        var nested = Directory.CreateDirectory(Path.Combine(_root, "nested")).FullName;
        File.WriteAllText(Path.Combine(nested, "ignored.txt"), "ignored");
        var service = new FileSystemService();

        var plan = await service.PlanFileOrganizationAsync(
            _root,
            ClassifyMode.ByExtension);

        Assert.True(plan.IsSuccess, plan.ErrorMessage);
        Assert.Equal(2, plan.Data!.Count);
        Assert.All(plan.Data, item => Assert.False(item.HasConflict));
        Assert.Contains(plan.Data, item => item.Category == "txt");
        Assert.Contains(plan.Data, item => item.Category == "png");

        var execution = await service.ExecuteFileOrganizationAsync(
            _root,
            ClassifyMode.ByExtension,
            plan.Data);

        Assert.True(execution.IsSuccess, execution.ErrorMessage);
        Assert.Equal(2, execution.Data!.PlannedCount);
        Assert.Equal(2, execution.Data.MovedCount);
        Assert.Empty(execution.Data.Failures);
        Assert.True(File.Exists(Path.Combine(
            _root,
            "Long Organized",
            "txt",
            "notes.txt")));
        Assert.True(File.Exists(Path.Combine(
            _root,
            "Long Organized",
            "png",
            "image.png")));
        Assert.True(File.Exists(Path.Combine(nested, "ignored.txt")));
    }

    [Fact]
    public async Task Execute_RejectsTamperedDestinationAndKeepsSource()
    {
        var source = Path.Combine(_root, "notes.txt");
        File.WriteAllText(source, "notes");
        var service = new FileSystemService();
        var plan = await service.PlanFileOrganizationAsync(
            _root,
            ClassifyMode.ByExtension);
        var item = Assert.Single(plan.Data!);
        item.DestinationPath = Path.Combine(_root, "outside.txt");

        var execution = await service.ExecuteFileOrganizationAsync(
            _root,
            ClassifyMode.ByExtension,
            [item]);

        Assert.True(execution.IsSuccess, execution.ErrorMessage);
        Assert.Equal(0, execution.Data!.MovedCount);
        Assert.Single(execution.Data.Failures);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(item.DestinationPath));
    }

    [Fact]
    public async Task Execute_ReportsConflictWithoutOverwritingTarget()
    {
        var source = Path.Combine(_root, "notes.txt");
        File.WriteAllText(source, "source");
        var service = new FileSystemService();
        var plan = await service.PlanFileOrganizationAsync(
            _root,
            ClassifyMode.ByExtension);
        var item = Assert.Single(plan.Data!);
        Directory.CreateDirectory(Path.GetDirectoryName(item.DestinationPath)!);
        File.WriteAllText(item.DestinationPath, "existing");

        var execution = await service.ExecuteFileOrganizationAsync(
            _root,
            ClassifyMode.ByExtension,
            [item]);

        Assert.True(execution.IsSuccess, execution.ErrorMessage);
        Assert.Equal(0, execution.Data!.MovedCount);
        Assert.Single(execution.Data.Failures);
        Assert.True(File.Exists(source));
        Assert.Equal("existing", File.ReadAllText(item.DestinationPath));
    }

    [Fact]
    public async Task Execute_RejectsInvalidItemBeforeApplyingBatch()
    {
        var source = Path.Combine(_root, "notes.txt");
        File.WriteAllText(source, "notes");
        var service = new FileSystemService();
        var plan = await service.PlanFileOrganizationAsync(
            _root,
            ClassifyMode.ByExtension);
        var item = Assert.Single(plan.Data!);

        var execution = await service.ExecuteFileOrganizationAsync(
            _root,
            ClassifyMode.ByExtension,
            [null!, item]);

        Assert.True(execution.IsSuccess, execution.ErrorMessage);
        Assert.Equal(2, execution.Data!.PlannedCount);
        Assert.Equal(0, execution.Data.MovedCount);
        Assert.Single(execution.Data.Failures);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(item.DestinationPath));
    }

    [Fact]
    public async Task Execute_PreflightConflictKeepsEarlierItemsUnchanged()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_root, "b.png"), "b");
        var service = new FileSystemService();
        var plan = await service.PlanFileOrganizationAsync(
            _root,
            ClassifyMode.ByExtension);
        Assert.Equal(2, plan.Data!.Count);
        var conflicting = plan.Data[1];
        Directory.CreateDirectory(
            Path.GetDirectoryName(conflicting.DestinationPath)!);
        File.WriteAllText(conflicting.DestinationPath, "existing");

        var execution = await service.ExecuteFileOrganizationAsync(
            _root,
            ClassifyMode.ByExtension,
            plan.Data);

        Assert.True(execution.IsSuccess, execution.ErrorMessage);
        Assert.Equal(0, execution.Data!.MovedCount);
        Assert.Single(execution.Data.Failures);
        Assert.All(plan.Data, item => Assert.True(File.Exists(item.SourcePath)));
        Assert.Equal(
            "existing",
            File.ReadAllText(conflicting.DestinationPath));
    }

    [Fact]
    public async Task BatchRename_PreflightsWholeBatchBeforeMovingFiles()
    {
        var first = Path.Combine(_root, "first.txt");
        var second = Path.Combine(_root, "second.txt");
        var conflict = Path.Combine(_root, "taken.txt");
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");
        File.WriteAllText(conflict, "taken");
        var service = new FileSystemService();

        var result = await service.BatchRenameAsync(
        [
            new RenameOperation { OldPath = first, NewName = "renamed.txt" },
            new RenameOperation { OldPath = second, NewName = "taken.txt" },
        ]);

        Assert.False(result.IsSuccess);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.False(File.Exists(Path.Combine(_root, "renamed.txt")));
        Assert.Equal("taken", File.ReadAllText(conflict));
    }

    [Fact]
    public async Task BatchRename_AppliesValidatedBatch()
    {
        var first = Path.Combine(_root, "first.txt");
        var second = Path.Combine(_root, "second.txt");
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");
        var service = new FileSystemService();

        var result = await service.BatchRenameAsync(
        [
            new RenameOperation { OldPath = first, NewName = "one.txt" },
            new RenameOperation { OldPath = second, NewName = "two.txt" },
        ]);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2, result.Data);
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.Equal("first", File.ReadAllText(Path.Combine(_root, "one.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(_root, "two.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
