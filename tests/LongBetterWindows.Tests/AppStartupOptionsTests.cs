using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public class AppStartupOptionsTests
{
    [Fact]
    public void Parse_MapsCommandAndQualityArguments()
    {
        var options = AppStartupOptions.Parse(
        [
            "--theme", "DARK",
            "--run-command", "SAMPLE.HELLO",
            "--command-text", "hello",
            "--plugins-dir", "test-plugins",
            "--exit-after-command",
            "--quality-capture", "capture.png",
            "--quality-capture-view", "plugins",
            "--quality-render-dpi", "144",
            "--quality-width", "1280",
            "--quality-height", "800",
            "--quality-high-contrast",
            "--quality-reduce-motion",
            "--quality-empty-context",
            "--quality-workflows-dir", "quality-workflows",
        ]);

        Assert.Equal("dark", options.ThemeOverride);
        Assert.Equal("sample.hello", options.RequestedCommandKey);
        Assert.Equal("hello", options.RequestedCommandText);
        Assert.Equal("test-plugins", options.RequestedPluginsDirectory);
        Assert.True(options.ExitAfterCommand);
        Assert.Equal("capture.png", options.QualityCapturePath);
        Assert.Equal("plugins", options.QualityCaptureView);
        Assert.True(options.OpenPluginsForQuality);
        Assert.Equal(144, options.QualityRenderDpi);
        Assert.Equal(1280, options.QualityCaptureWidth);
        Assert.Equal(800, options.QualityCaptureHeight);
        Assert.True(options.ForceHighContrast);
        Assert.True(options.ForceReduceMotion);
        Assert.True(options.UseEmptyContextForQuality);
        Assert.Equal("quality-workflows", options.QualityWorkflowsDirectory);
    }

    [Fact]
    public void Parse_UsesDefaultsAndClampsNumericArguments()
    {
        var options = AppStartupOptions.Parse(
        [
            "--theme", "unknown",
            "--quality-render-dpi", "999",
            "--quality-capture-delay-ms", "1",
            "--quality-width", "invalid",
        ]);

        Assert.Null(options.ThemeOverride);
        Assert.Equal("main", options.QualityCaptureView);
        Assert.Equal(384, options.QualityRenderDpi);
        Assert.Equal(100, options.QualityCaptureDelayMilliseconds);
        Assert.Equal(0, options.QualityCaptureWidth);
    }

    [Fact]
    public void Parse_CaptureViewOpensItsHostSurface()
    {
        Assert.True(AppStartupOptions.Parse(
            ["--quality-capture-view", "market"]).OpenMarketForQuality);
        Assert.True(AppStartupOptions.Parse(
            ["--quality-capture-view", "diagnostics"]).OpenDiagnosticsForQuality);
        Assert.True(AppStartupOptions.Parse(
            ["--quality-capture-view", "plugins"]).OpenPluginsForQuality);
    }
}
