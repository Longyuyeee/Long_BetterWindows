using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public sealed class PluginRuntimeLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"long-runtime-loader-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(null, (int)PluginRuntimeKind.Native)]
    [InlineData("native", (int)PluginRuntimeKind.Native)]
    [InlineData("unknown-future-runtime", (int)PluginRuntimeKind.Native)]
    [InlineData(" csharp-script ", (int)PluginRuntimeKind.CSharpScript)]
    [InlineData("WEBVIEW", (int)PluginRuntimeKind.WebView)]
    public void RuntimeKind_PreservesNativeFallback(
        string? runtime,
        int expected)
        => Assert.Equal(
            (PluginRuntimeKind)expected,
            PluginRuntimeLoader.GetRuntimeKind(runtime));

    [Fact]
    public async Task WebViewManifest_CreatesUnifiedPluginAdapter()
    {
        Directory.CreateDirectory(_root);
        var loader = new PluginRuntimeLoader();
        var manifest = Manifest("web", "webview", "index.html");

        var result = await loader.LoadAsync(_root, manifest);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(PluginRuntimeKind.WebView, result.Kind);
        Assert.IsType<WebPluginAdapter>(result.Instance);
        Assert.Null(result.LoadContext);
        loader.Release(result, manifest.Id);
    }

    [Fact]
    public async Task CSharpScriptManifest_ExecutesAndCreatesAdapter()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "plugin.csx"),
            "Start = () => Task.FromResult(true); Stop = () => Task.FromResult(true);");
        var loader = new PluginRuntimeLoader();
        var manifest = Manifest("script", "csharp-script", "plugin.csx");

        var result = await loader.LoadAsync(_root, manifest);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(PluginRuntimeKind.CSharpScript, result.Kind);
        Assert.IsType<ScriptPluginAdapter>(result.Instance);
        loader.Release(result, manifest.Id);
    }

    [Fact]
    public async Task MissingNativeEntryPoint_PropagatesLoaderFailure()
    {
        Directory.CreateDirectory(_root);
        var loader = new PluginRuntimeLoader();
        var manifest = Manifest("native", null, "missing.dll");

        var result = await loader.LoadAsync(_root, manifest);

        Assert.False(result.IsSuccess);
        Assert.Equal(PluginRuntimeKind.Native, result.Kind);
        Assert.Contains("入口程序集未找到", result.Error);
        Assert.Null(result.Instance);
    }

    private static PluginManifest Manifest(
        string id,
        string? runtime,
        string entryPoint)
        => new()
        {
            Id = id,
            Name = id,
            Version = "1.0.0",
            Runtime = runtime,
            EntryPoint = entryPoint,
        };

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
