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
    public async Task StorageService_Set_PersistsAcrossInstances()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_storage_{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "test.json");
        try
        {
            using (var writer = new StorageService(path))
            {
                var result = await writer.SetAsync("persisted", "value");
                Assert.True(result.IsSuccess, result.ErrorMessage);
            }

            using var reader = new StorageService(path);
            var persisted = await reader.GetAsync("persisted");
            Assert.True(persisted.IsSuccess, persisted.ErrorMessage);
            Assert.Equal("value", persisted.Data);
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

    // ===== HotKeyService 测试 =====

    [Fact]
    public async Task HotKeyService_IsConflict_NoConflict_ReturnsFalse()
    {
        var svc = new HotKeyService();
        var r = await svc.IsConflictAsync("Ctrl+Shift+X");
        Assert.True(r.IsSuccess);
        Assert.False(r.Data); // 无注册，无冲突
    }

    [Fact]
    public async Task HotKeyService_Register_InvalidFormat_Fails()
    {
        var svc = new HotKeyService();
        var r = await svc.RegisterAsync("InvalidKey", () => { });
        Assert.False(r.IsSuccess);
    }

    [Fact]
    public async Task HotKeyService_Unregister_NotFound_Fails()
    {
        var svc = new HotKeyService();
        var r = await svc.UnregisterAsync("Ctrl+Z");
        Assert.False(r.IsSuccess);
    }

    [Fact]
    public async Task HotKeyService_Register_NeedHWnd_Fails()
    {
        var svc = new HotKeyService();
        // 未初始化 HWnd，注册应失败
        var r = await svc.RegisterAsync("Ctrl+K", () => { });
        Assert.False(r.IsSuccess);
    }

    [Fact]
    public void HotKeyService_GetOwner_NoEntry_ReturnsNull()
    {
        var svc = new HotKeyService();
        Assert.Null(svc.GetOwner("Ctrl+X"));
    }

    [Fact]
    public void HotKeyService_GetAllHotkeys_Empty_ReturnsEmpty()
    {
        var svc = new HotKeyService();
        var all = svc.GetAllHotkeys();
        Assert.Empty(all);
    }

    // ===== ADSService 测试 =====

    [Fact]
    public async Task ADSService_WriteRead_TextFile_Works()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_ads_{Guid.NewGuid():N}");
        var file = Path.Combine(dir, "test.txt");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, "base");

            var rb = new RollbackEngine();
            var svc = new ADSService(rb);

            // 写入 ADS
            var w = await svc.WriteAsync(file, "note", "hello world");
            Assert.True(w.IsSuccess);

            // 读取 ADS
            var r = await svc.ReadAsync(file, "note");
            Assert.True(r.IsSuccess);
            Assert.Equal("hello world", r.Data);

            // 删除 ADS
            var d = await svc.DeleteAsync(file, "note");
            Assert.True(d.IsSuccess);
        }
        finally { if (Directory.Exists(dir)) try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task ADSService_WriteRead_Directory_Works()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_ads_dir_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var rb = new RollbackEngine();
            var svc = new ADSService(rb);

            var w = await svc.WriteAsync(dir, "note", "dir note");
            Assert.True(w.IsSuccess);

            var r = await svc.ReadAsync(dir, "note");
            Assert.True(r.IsSuccess);
            Assert.Equal("dir note", r.Data);
        }
        finally { if (Directory.Exists(dir)) try { Directory.Delete(dir, true); } catch { } }
    }

    // ===== FileOpsService 测试 =====

    [Fact]
    public async Task FileOpsService_Copy_Works()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_fo_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var src = Path.Combine(dir, "src.txt");
            var dst = Path.Combine(dir, "dst.txt");
            File.WriteAllText(src, "test");

            var svc = new FileOpsService();
            var r = await svc.CopyAsync(src, dst);
            Assert.True(r.IsSuccess);
            Assert.True(File.Exists(dst));
        }
        finally { if (Directory.Exists(dir)) try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task FileOpsService_Move_Works()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_fo_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var src = Path.Combine(dir, "src.txt");
            var dst = Path.Combine(dir, "moved.txt");
            File.WriteAllText(src, "test");

            var svc = new FileOpsService();
            var r = await svc.MoveAsync(src, dst);
            Assert.True(r.IsSuccess);
            Assert.False(File.Exists(src));
            Assert.True(File.Exists(dst));
        }
        finally { if (Directory.Exists(dir)) try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task FileOpsService_Delete_Works()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_fo_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "del.txt");
            File.WriteAllText(file, "test");

            var svc = new FileOpsService();
            var r = await svc.DeleteAsync(file);
            Assert.True(r.IsSuccess);
            Assert.False(File.Exists(file));
        }
        finally { if (Directory.Exists(dir)) try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task FileOpsService_Exists_ReturnsTrue()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_fo_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "exists.txt");
            File.WriteAllText(file, "test");

            var svc = new FileOpsService();
            var r = await svc.ExistsAsync(file);
            Assert.True(r.IsSuccess && r.Data);
        }
        finally { if (Directory.Exists(dir)) try { Directory.Delete(dir, true); } catch { } }
    }
}
