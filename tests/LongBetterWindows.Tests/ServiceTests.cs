using System.IO;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public class ServiceTests
{
    [Fact]
    public async Task StorageService_SetAndGet_Works()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_storage_{Guid.NewGuid():N}");
        try
        {
            var svc = new StorageService(Path.Combine(dir, "test.json"));
            await svc.SetAsync("key1", "value1");
            var r = await svc.GetAsync("key1");
            Assert.True(r.IsSuccess);
            Assert.Equal("value1", r.Data);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task StorageService_ContainsKey_ReturnsTrue()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_storage_{Guid.NewGuid():N}");
        try
        {
            var svc = new StorageService(Path.Combine(dir, "test.json"));
            await svc.SetAsync("exists", "yes");
            var r = await svc.ContainsKeyAsync("exists");
            Assert.True(r.IsSuccess && r.Data);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task StorageService_Delete_RemovesKey()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_storage_{Guid.NewGuid():N}");
        try
        {
            var svc = new StorageService(Path.Combine(dir, "test.json"));
            await svc.SetAsync("temp", "x");
            await svc.DeleteAsync("temp");
            var r = await svc.GetAsync("temp");
            Assert.Null(r.Data);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void RollbackEngine_RecordAndGetChanges_Works()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_rb_{Guid.NewGuid():N}");
        try
        {
            var rb = new RollbackEngine(Path.Combine(dir, "logs"));
            rb.RecordChange("plugin-a", new ChangeRecord { Action = ChangeAction.RegistryWrite, Target = "key1", ValueName = "val", OldValue = "old", NewValue = "new" });
            rb.RecordChange("plugin-a", new ChangeRecord { Action = ChangeAction.AdsWrite, Target = "path:stream" });
            var changes = rb.GetPluginChanges("plugin-a");
            Assert.Equal(2, changes.Count);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task RollbackEngine_Rollback_ClearsLog()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_rb_{Guid.NewGuid():N}");
        try
        {
            var rb = new RollbackEngine(Path.Combine(dir, "logs"));
            rb.RecordChange("plugin-x", new ChangeRecord { Action = ChangeAction.AdsWrite, Target = "test" });
            var r = await rb.RollbackAsync("plugin-x");
            Assert.True(r.IsSuccess);
            Assert.Empty(rb.GetPluginChanges("plugin-x"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
