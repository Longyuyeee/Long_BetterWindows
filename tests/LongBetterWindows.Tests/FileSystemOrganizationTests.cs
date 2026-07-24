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
    public async Task Execute_ReportsNullItemWithoutAbortingBatch()
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
        Assert.Equal(1, execution.Data.MovedCount);
        Assert.Single(execution.Data.Failures);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(item.DestinationPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
