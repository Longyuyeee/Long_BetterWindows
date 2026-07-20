using System.IO;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public sealed class StandalonePluginLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"long-standalone-loader-{Guid.NewGuid():N}");

    [Fact]
    public void JavaScriptWrapper_EncodesSourceAndEscapesDisplayName()
    {
        const string source = "console.log('</script>');";

        var html = StandalonePluginLoader.BuildJavaScriptWrapper(
            "Tool <Preview>", source, isTypeScript: true);

        Assert.Contains("Tool &lt;Preview&gt;", html);
        Assert.DoesNotContain(source, html);
        Assert.Contains(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(source)), html);
        Assert.Contains("typescript.js", html);
        Assert.Contains("ts.transpileModule", html);
    }

    [Fact]
    public async Task CSharpScript_LoadsTracksAndUnloadsCleanly()
    {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, "working.csx");
        File.WriteAllText(sourcePath,
            "Start = () => Task.FromResult(true); Stop = () => Task.FromResult(true);");
        var registry = new PluginRegistry();
        var loader = new StandalonePluginLoader(registry);

        var result = await loader.LoadAsync(sourcePath);

        Assert.True(result.IsSuccess, result.Error);
        var handle = Assert.IsType<StandalonePluginHandle>(result.Handle);
        Assert.Equal("script-working", handle.Manifest.Id);
        Assert.Equal(PluginState.Running, registry.Get(handle.Manifest.Id)!.State);

        await loader.UnloadAsync(handle);

        Assert.Null(registry.Get(handle.Manifest.Id));
    }

    [Fact]
    public async Task CSharpScript_StartFailure_RemovesPartialRegistration()
    {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, "failing.csx");
        File.WriteAllText(sourcePath,
            "Start = () => Task.FromException(new System.Exception(\"failed\")); " +
            "Stop = () => Task.FromResult(true);");
        var registry = new PluginRegistry();
        var loader = new StandalonePluginLoader(registry);

        var result = await loader.LoadAsync(sourcePath);

        Assert.False(result.IsSuccess);
        Assert.Contains("启动失败", result.Error);
        Assert.Null(registry.Get("script-failing"));
    }

    [Fact]
    public async Task UnsupportedFile_FailsWithoutRegisteringPlugin()
    {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, "notes.txt");
        File.WriteAllText(sourcePath, "plain text");
        var registry = new PluginRegistry();
        var loader = new StandalonePluginLoader(registry);

        var result = await loader.LoadAsync(sourcePath);

        Assert.False(result.IsSuccess);
        Assert.Contains("不支持", result.Error);
        Assert.Empty(registry.GetAll());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
