using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
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
    public async Task WebViewAdapter_ProjectsLocalizedNameIntoPresentation()
    {
        Directory.CreateDirectory(_root);
        var manifest = Manifest("web-localized", "webview", "index.html");
        using var adapter = new WebPluginAdapter(
            new WebPluginRuntime(manifest, _root),
            manifest.Id,
            manifest.Name,
            manifest.Version,
            _root,
            manifest.EntryPoint);

        await adapter.OnLanguageChangedAsync(new PluginLanguageContext(
            "en-US",
            "en-US",
            new Dictionary<string, string>
            {
                ["plugin.name"] = "Localized web plugin",
            }));

        Assert.Equal("Localized web plugin", adapter.Name);
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

    [Fact]
    public async Task WebBackgroundAdapter_ComposesLifecycleWithoutCreatingWebView()
    {
        Directory.CreateDirectory(_root);
        var manifest = Manifest("hybrid", "webview", "index.html");
        var web = new WebPluginAdapter(
            new WebPluginRuntime(manifest, _root),
            manifest.Id,
            manifest.Name,
            manifest.Version,
            _root,
            manifest.EntryPoint);
        var background = new TestBackgroundPlugin();
        using var adapter = new WebPluginWithBackgroundAdapter(web, background);

        Assert.True(await adapter.InitializeAsync(null!));
        Assert.True(await adapter.StartAsync());
        Assert.Equal(1, background.InitializeCount);
        Assert.Equal(1, background.StartCount);
        Assert.Equal(PluginState.Running, adapter.State);

        Assert.True(await adapter.EnterBackgroundAsync());
        Assert.Equal(PluginState.Background, adapter.State);
        Assert.True(await adapter.ResumeAsync());
        Assert.True(await adapter.StopAsync());
        Assert.Equal(1, background.StopCount);
        Assert.Equal(PluginState.Stopped, adapter.State);
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

    private sealed class TestBackgroundPlugin : ILongPlugin
    {
        public string Id => "hybrid";
        public string Name => "Hybrid background";
        public string Version => "1.0.0";
        public PluginState State { get; private set; } = PluginState.Loaded;
        public int InitializeCount { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public Task<bool> InitializeAsync(IHostApi host)
        {
            InitializeCount++;
            return Task.FromResult(true);
        }

        public Task<bool> StartAsync()
        {
            StartCount++;
            State = PluginState.Running;
            return Task.FromResult(true);
        }

        public Task<bool> StopAsync()
        {
            StopCount++;
            State = PluginState.Stopped;
            return Task.FromResult(true);
        }
    }
}
