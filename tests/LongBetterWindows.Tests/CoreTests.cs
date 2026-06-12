using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public class CoreTests
{
    [Fact]
    public void ApiVersion_IsCompatible_HostNewerThanPlugin_ReturnsTrue()
    {
        var host = new ApiVersion(1, 5, 0);
        var plugin = new ApiVersion(1, 0, 0);
        Assert.True(host.IsCompatibleWith(plugin));
    }

    [Fact]
    public void ApiVersion_IsCompatible_HostOlderThanPlugin_ReturnsFalse()
    {
        var host = new ApiVersion(1, 0, 0);
        var plugin = new ApiVersion(1, 5, 0);
        Assert.False(host.IsCompatibleWith(plugin));
    }

    [Fact]
    public void ApiVersion_IsCompatible_DifferentMajor_ReturnsFalse()
    {
        var v1 = new ApiVersion(1, 0, 0);
        var v2 = new ApiVersion(2, 0, 0);
        Assert.False(v1.IsCompatibleWith(v2));
    }

    [Fact]
    public void HostApiResponse_Success_HasData()
    {
        var r = HostApiResponse<string>.Success("hello");
        Assert.True(r.IsSuccess);
        Assert.Equal("hello", r.Data);
        Assert.Null(r.ErrorMessage);
    }

    [Fact]
    public void HostApiResponse_Failure_HasError()
    {
        var r = HostApiResponse<string>.Failure(ApiErrorCode.NotFound, "not found");
        Assert.False(r.IsSuccess);
        Assert.Equal("not found", r.ErrorMessage);
    }

    [Fact]
    public async Task ManifestReader_ValidManifest_ReturnsSuccess()
    {
        var dir = CreateManifestDir(new { id = "com.test.app", version = "1.0.0", name = "Test", entry_point = "test.dll" });
        var result = await ManifestReader.ReadAsync(dir);
        Assert.True(result.IsSuccess);
        Assert.Equal("com.test.app", result.Manifest!.Id);
    }

    [Fact]
    public async Task ManifestReader_MissingId_ReturnsFailure()
    {
        var dir = CreateManifestDir(new { version = "1.0.0", name = "Test", entry_point = "test.dll" });
        var result = await ManifestReader.ReadAsync(dir);
        Assert.False(result.IsSuccess);
        Assert.Contains("id", result.Error);
    }

    [Fact]
    public async Task ManifestReader_InvalidVersion_ReturnsFailure()
    {
        var dir = CreateManifestDir(new { id = "com.test.app", version = "abc", name = "Test", entry_point = "test.dll" });
        var result = await ManifestReader.ReadAsync(dir);
        Assert.False(result.IsSuccess);
        Assert.Contains("版本", result.Error);
    }

    [Fact]
    public async Task ManifestReader_UnknownCapability_ReturnsFailure()
    {
        var dir = CreateManifestDir(new { id = "com.test.app", version = "1.0.0", name = "Test", entry_point = "test.dll", capabilities = new[] { "unknown.cap" } });
        var result = await ManifestReader.ReadAsync(dir);
        Assert.False(result.IsSuccess);
        Assert.Contains("未知能力", result.Error);
    }

    [Fact]
    public void PluginRegistry_Register_AddsPlugin()
    {
        var reg = new PluginRegistry();
        var manifest = new PluginManifest { Id = "test", Name = "Test", Version = "1.0.0", EntryPoint = "t.dll" };
        var plugin = new TestPlugin();
        Assert.True(reg.Register(manifest, plugin, null, "/test"));
        Assert.NotNull(reg.Get("test"));
        Assert.Single(reg.GetAll());
    }

    [Fact]
    public void PluginRegistry_DuplicateRegister_ReturnsFalse()
    {
        var reg = new PluginRegistry();
        var manifest = new PluginManifest { Id = "test", Name = "Test", Version = "1.0.0", EntryPoint = "t.dll" };
        var plugin = new TestPlugin();
        reg.Register(manifest, plugin, null, "/test");
        Assert.False(reg.Register(manifest, plugin, null, "/test"));
    }

    [Fact]
    public void PluginRegistry_HasCapability_ChecksCorrectly()
    {
        var reg = new PluginRegistry();
        var manifest = new PluginManifest { Id = "test", EntryPoint = "t.dll", Capabilities = new List<string> { "system.hotkey" } };
        reg.Register(manifest, new TestPlugin(), null, "/test");
        Assert.True(reg.HasCapability("test", "system.hotkey"));
        Assert.False(reg.HasCapability("test", "shell.selection"));
    }

    [Fact]
    public void PluginRegistry_Get_NotFound_ReturnsNull()
    {
        var reg = new PluginRegistry();
        Assert.Null(reg.Get("nonexistent"));
    }

    // ===== helpers =====

    private static string CreateManifestDir(object content)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_manifest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(content));
        return dir;
    }

    private class TestPlugin : ILongPlugin
    {
        public string Id => "test";
        public string Name => "Test";
        public string Version => "1.0.0";
        public PluginState State { get; private set; } = PluginState.Loaded;
        public Task<bool> InitializeAsync(IHostApi host) => Task.FromResult(true);
        public Task<bool> StartAsync() => Task.FromResult(true);
        public Task<bool> StopAsync() => Task.FromResult(true);
    }
}
