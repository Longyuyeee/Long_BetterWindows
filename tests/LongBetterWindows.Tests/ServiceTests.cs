using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Interop;
using LongBetterWindows.Host.Contracts;
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
    public async Task StorageService_CompareExchange_RejectsStaleSnapshot()
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            $"test_storage_{Guid.NewGuid():N}");
        try
        {
            using var service = new StorageService(
                Path.Combine(dir, "test.json"));
            Assert.True((await service.SetAsync("history", "v1")).IsSuccess);

            var exchanged = await service.CompareExchangeAsync(
                "history",
                "stale",
                "v2");
            var current = await service.GetAsync("history");

            Assert.True(exchanged.IsSuccess, exchanged.ErrorMessage);
            Assert.False(exchanged.Data);
            Assert.Equal("v1", current.Data);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task StorageService_CompareExchange_AllowsOneConcurrentWriter()
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            $"test_storage_{Guid.NewGuid():N}");
        try
        {
            using var service = new StorageService(
                Path.Combine(dir, "test.json"));
            Assert.True((await service.SetAsync("history", "v0")).IsSuccess);

            var attempts = await Task.WhenAll(
                Enumerable.Range(1, 16).Select(index =>
                    service.CompareExchangeAsync(
                        "history",
                        "v0",
                        $"v{index}")));
            var current = await service.GetAsync("history");

            Assert.All(
                attempts,
                attempt => Assert.True(
                    attempt.IsSuccess,
                    attempt.ErrorMessage));
            Assert.Single(attempts, attempt => attempt.Data);
            Assert.Contains(
                current.Data,
                Enumerable.Range(1, 16).Select(index => $"v{index}"));
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

    [Fact]
    public async Task RollbackEngine_FailedRecordRemainsAvailableForRetry()
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            $"test_rb_retry_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var blockedParent = Path.Combine(dir, "blocked");
            File.WriteAllText(blockedParent, "file blocks directory creation");
            var rollback = new RollbackEngine(Path.Combine(dir, "logs"));
            rollback.RecordChange("plugin-retry", new ChangeRecord
            {
                Action = ChangeAction.AdsWrite,
                Target = Path.Combine(blockedParent, "note:stream"),
                StorageTarget = Path.Combine(blockedParent, "note:stream"),
                OldStorageTarget = Path.Combine(blockedParent, "note:stream"),
                OldValueExists = true,
                OldValue = "restore me",
            });

            var result = await rollback.RollbackAsync("plugin-retry");

            Assert.False(result.IsSuccess);
            Assert.Single(rollback.GetPluginChanges("plugin-retry"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
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
    public async Task HotKeyService_Register_StandaloneFunctionKey_IsValid()
    {
        var svc = new HotKeyService();
        var result = await svc.RegisterAsync("F6", () => { });

        Assert.False(result.IsSuccess);
        Assert.Contains("未初始化", result.ErrorMessage);
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

#pragma warning disable xUnit1031 // The isolated STA thread cannot use the xUnit async context.
    [Fact]
    public void HotKeyService_ChangeHotkey_IsAtomicAndDetectsSamePluginConflict()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var source = new HwndSource(new HwndSourceParameters(
                    "LongBetterWindows.HotkeyServiceTests")
                {
                    Width = 1,
                    Height = 1,
                    WindowStyle = unchecked((int)0x80000000),
                });
                using var service = new HotKeyService();
                service.Initialize(source.Handle);
                const string pluginId = "com.long.quality-hotkey";
                const string first = "Ctrl+Alt+Shift+F10";
                const string second = "Ctrl+Alt+Shift+F11";
                const string replacement = "Ctrl+Alt+Shift+F12";

                Assert.True(service.RegisterAsync(
                    first,
                    pluginId,
                    () => { }).GetAwaiter().GetResult().IsSuccess);
                Assert.True(service.RegisterAsync(
                    second,
                    pluginId,
                    () => { }).GetAwaiter().GetResult().IsSuccess);

                var conflict = service.IsConflictAsync(second, first)
                    .GetAwaiter()
                    .GetResult();
                Assert.True(conflict.IsSuccess);
                Assert.True(conflict.Data);

                var rejected = service.ChangeHotkeyAsync(
                        first,
                        second,
                        pluginId,
                        () => { })
                    .GetAwaiter()
                    .GetResult();
                Assert.False(rejected.IsSuccess);
                Assert.Equal(ApiErrorCode.HotKeyConflict, rejected.ErrorCode);
                Assert.Contains(first, service.GetAllHotkeys().Keys);
                Assert.Contains(second, service.GetAllHotkeys().Keys);

                var changed = service.ChangeHotkeyAsync(
                        first,
                        replacement,
                        pluginId,
                        () => { })
                    .GetAwaiter()
                    .GetResult();
                Assert.True(changed.IsSuccess);
                Assert.DoesNotContain(first, service.GetAllHotkeys().Keys);
                Assert.Contains(second, service.GetAllHotkeys().Keys);
                Assert.Contains(replacement, service.GetAllHotkeys().Keys);

                Assert.True(service.RegisterAsync(
                    "Ctrl+Alt+Shift+F9",
                    "command:com.long.quality-hotkey:open",
                    () => { }).GetAwaiter().GetResult().IsSuccess);
                Assert.Equal(
                    3,
                    service.UnregisterPluginAsync(pluginId)
                        .GetAwaiter()
                        .GetResult());
                Assert.Empty(service.GetAllHotkeys());
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
#pragma warning restore xUnit1031

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

            var rb = new RollbackEngine(Path.Combine(dir, "rollback"));
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
            var rb = new RollbackEngine(Path.Combine(dir, "rollback"));
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
