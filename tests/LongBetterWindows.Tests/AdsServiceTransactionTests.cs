using System.IO;
using System.Text;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class AdsServiceTransactionTests
{
    [Fact]
    public async Task WriteAsync_OverwritesAndReadsBackLargeUnicodeContent()
    {
        using var scope = new AdsTestScope();
        var content = string.Concat(
            Enumerable.Repeat("跨页备注-Long Better Windows\r\n", 400));

        var write = await scope.Service.WriteAsync(
            scope.TargetDirectory,
            "long_note",
            content);
        var read = await scope.Service.ReadAsync(
            scope.TargetDirectory,
            "long_note");

        Assert.True(write.IsSuccess, write.ErrorMessage);
        Assert.True(read.IsSuccess, read.ErrorMessage);
        Assert.Equal(content, read.Data);
        Assert.False(File.Exists(
            Path.Combine(scope.TargetDirectory, "long_note.json")));
    }

    [Fact]
    public async Task RollbackAsync_AfterOverwrite_RestoresOriginalAds()
    {
        using var scope = new AdsTestScope();
        WriteDirectAds(scope.TargetDirectory, "原始备注");

        var write = await scope.Service.WriteAsync(
            scope.TargetDirectory,
            "long_note",
            "覆盖后的备注");
        var rollback = await scope.Rollback.RollbackAsync("builtin");
        var read = await scope.Service.ReadAsync(
            scope.TargetDirectory,
            "long_note");

        Assert.True(write.IsSuccess, write.ErrorMessage);
        Assert.True(rollback.IsSuccess, rollback.ErrorMessage);
        Assert.True(read.IsSuccess, read.ErrorMessage);
        Assert.Equal("原始备注", read.Data);
    }

    [Fact]
    public async Task RollbackAsync_AfterDelete_RestoresOriginalAds()
    {
        using var scope = new AdsTestScope();
        WriteDirectAds(scope.TargetDirectory, "待恢复备注");

        var delete = await scope.Service.DeleteAsync(
            scope.TargetDirectory,
            "long_note");
        var missing = await scope.Service.ReadAsync(
            scope.TargetDirectory,
            "long_note");
        var rollback = await scope.Rollback.RollbackAsync("builtin");
        var restored = await scope.Service.ReadAsync(
            scope.TargetDirectory,
            "long_note");

        Assert.True(delete.IsSuccess, delete.ErrorMessage);
        Assert.False(missing.IsSuccess);
        Assert.Equal(ApiErrorCode.StreamNotFound, missing.ErrorCode);
        Assert.True(rollback.IsSuccess, rollback.ErrorMessage);
        Assert.True(restored.IsSuccess, restored.ErrorMessage);
        Assert.Equal("待恢复备注", restored.Data);
    }

    [Fact]
    public async Task RollbackAsync_PreservesExistingEmptyStream()
    {
        using var scope = new AdsTestScope();
        WriteDirectAds(scope.TargetDirectory, string.Empty);

        var write = await scope.Service.WriteAsync(
            scope.TargetDirectory,
            "long_note",
            "临时内容");
        var rollback = await scope.Rollback.RollbackAsync("builtin");
        var restored = await scope.Service.ReadAsync(
            scope.TargetDirectory,
            "long_note");

        Assert.True(write.IsSuccess, write.ErrorMessage);
        Assert.True(rollback.IsSuccess, rollback.ErrorMessage);
        Assert.True(restored.IsSuccess, restored.ErrorMessage);
        Assert.Equal(string.Empty, restored.Data);
    }

    [Fact]
    public async Task WriteAsync_SharingViolationFailsWithoutFallbackOrDataLoss()
    {
        using var scope = new AdsTestScope();
        var adsPath = WriteDirectAds(scope.TargetDirectory, "锁定内容");
        HostApiResponse result;

        using (var lockStream = new FileStream(
                   adsPath,
                   FileMode.Open,
                   FileAccess.ReadWrite,
                   FileShare.None))
        {
            result = await scope.Service.WriteAsync(
                scope.TargetDirectory,
                "long_note",
                "不应写入");
        }

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCode.Win32Error, result.ErrorCode);
        Assert.Equal("锁定内容", File.ReadAllText(adsPath, Encoding.UTF8));
        Assert.False(File.Exists(
            Path.Combine(scope.TargetDirectory, "long_note.json")));
    }

    [Fact]
    public async Task WriteAsync_MissingTargetDoesNotCreateSidecar()
    {
        using var scope = new AdsTestScope();
        var missing = Path.Combine(scope.Root, "missing");

        var result = await scope.Service.WriteAsync(
            missing,
            "long_note",
            "不能保存");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCode.NotFound, result.ErrorCode);
        Assert.False(Directory.Exists(missing));
        Assert.False(File.Exists(Path.Combine(missing, "long_note.json")));
    }

    [Fact]
    public async Task DeleteAsync_RollbackLogFailureRestoresDeletedNote()
    {
        using var scope = new AdsTestScope();
        WriteDirectAds(scope.TargetDirectory, "不能丢失");
        Directory.Delete(scope.RollbackPath, true);
        File.WriteAllText(scope.RollbackPath, "blocks rollback directory");

        var delete = await scope.Service.DeleteAsync(
            scope.TargetDirectory,
            "long_note");
        var restored = await scope.Service.ReadAsync(
            scope.TargetDirectory,
            "long_note");

        Assert.False(delete.IsSuccess);
        Assert.True(restored.IsSuccess, restored.ErrorMessage);
        Assert.Equal("不能丢失", restored.Data);
        Assert.Empty(scope.Rollback.GetPluginChanges("builtin"));
    }

    [Fact]
    public async Task WriteAsync_RejectsContentLargerThanOneMiB()
    {
        using var scope = new AdsTestScope();

        var result = await scope.Service.WriteAsync(
            scope.TargetDirectory,
            "long_note",
            new string('x', (1024 * 1024) + 1));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCode.InvalidArgument, result.ErrorCode);
    }

    private static string WriteDirectAds(string directory, string content)
    {
        var path = $"{directory}:long_note";
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private sealed class AdsTestScope : IDisposable
    {
        public AdsTestScope()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                $"lbw-ads-{Guid.NewGuid():N}");
            TargetDirectory = Path.Combine(Root, "target");
            Directory.CreateDirectory(TargetDirectory);
            RollbackPath = Path.Combine(Root, "rollback");
            Rollback = new RollbackEngine(RollbackPath);
            Service = new ADSService(Rollback);
        }

        public string Root { get; }
        public string TargetDirectory { get; }
        public string RollbackPath { get; }
        public RollbackEngine Rollback { get; }
        public ADSService Service { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
    }
}
