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
            "--language", "EN-us",
            "--run-command", "SAMPLE.HELLO",
            "--command-text", "hello",
            "--command-path", @"C:\quality\one.txt",
            "--command-path", @"C:\quality\two.txt",
            "--plugins-dir", "test-plugins",
            "--exit-after-command",
            "--quality-command-report", "command-report.json",
            "--quality-command-fixture", "command-fixture.json",
            "--quality-capture", "capture.png",
            "--quality-capture-view", "plugins",
            "--quality-render-dpi", "144",
            "--quality-width", "1280",
            "--quality-height", "800",
            "--quality-high-contrast",
            "--quality-reduce-motion",
            "--quality-window-automation",
            "--quality-empty-context",
            "--quality-workflows-dir", "quality-workflows",
            "--quality-open-workflow", "workflow.quality.review",
            "--quality-edit-workflow", "workflow.quality.editor",
            "--quality-workflow-upgrade-package", "quality-v2.lpak",
            "--quality-terminal-export-dir", "quality-exports",
            "--quality-plugin-page-release-report", "plugin-release.json",
            "--quality-plugin-page-performance-report", "plugin-performance.json",
            "--quality-plugin-runtime-session-report", "plugin-runtime-session.json",
            "--quality-taskbar-identity-report", "taskbar-identity.json",
            "--quality-ui-service-theme-report", "ui-service-theme.json",
            "--quality-themed-message-dialog-report", "message-dialog.json",
            "--quality-plugin-settings-report", "plugin-settings.json",
            "--quality-plugin-settings-persistence-report", "plugin-settings-persistence.json",
            "--quality-open-plugin-settings", "com.long.folder-note",
            "--quality-open-plugin-runtime", "com.long.base64",
            "--quality-skip-auto-start-plugin", "com.long.clipboardhistory",
            "--quality-skip-auto-start-plugin", "COM.LONG.MACRO",
            "--quality-hide-window-during-idle",
            "--quality-startup-report", "startup.json",
            "--quality-management-card-shadows",
            "--quality-show-welcome",
            "--quality-market-list",
            "--quality-market-detail",
            "--quality-market-update-review",
        ]);

        Assert.Equal("dark", options.ThemeOverride);
        Assert.Equal("en-US", options.LanguageOverride);
        Assert.Equal("sample.hello", options.RequestedCommandKey);
        Assert.Equal("hello", options.RequestedCommandText);
        Assert.Equal(
            [@"C:\quality\one.txt", @"C:\quality\two.txt"],
            options.RequestedCommandPaths);
        Assert.Equal("test-plugins", options.RequestedPluginsDirectory);
        Assert.True(options.ExitAfterCommand);
        Assert.Equal("command-report.json", options.QualityCommandReportPath);
        Assert.Equal("command-fixture.json", options.QualityCommandFixturePath);
        Assert.Equal("capture.png", options.QualityCapturePath);
        Assert.Equal("plugins", options.QualityCaptureView);
        Assert.True(options.OpenPluginsForQuality);
        Assert.Equal(144, options.QualityRenderDpi);
        Assert.Equal(1280, options.QualityCaptureWidth);
        Assert.Equal(800, options.QualityCaptureHeight);
        Assert.True(options.ForceHighContrast);
        Assert.True(options.ForceReduceMotion);
        Assert.True(options.EnableWindowAutomationForQuality);
        Assert.True(options.UseEmptyContextForQuality);
        Assert.Equal("quality-workflows", options.QualityWorkflowsDirectory);
        Assert.Equal("workflow.quality.review", options.QualityWorkflowReviewId);
        Assert.Equal("workflow.quality.editor", options.QualityWorkflowEditorId);
        Assert.Equal("quality-v2.lpak", options.QualityWorkflowUpgradePackagePath);
        Assert.Equal("quality-exports", options.QualityTerminalExportDirectory);
        Assert.Equal(
            "plugin-release.json",
            options.QualityPluginPageReleaseReportPath);
        Assert.Equal(
            "plugin-performance.json",
            options.QualityPluginPagePerformanceReportPath);
        Assert.Equal(
            "plugin-runtime-session.json",
            options.QualityPluginRuntimeSessionReportPath);
        Assert.Equal(
            "taskbar-identity.json",
            options.QualityTaskbarIdentityReportPath);
        Assert.Equal(
            "ui-service-theme.json",
            options.QualityUiServiceThemeReportPath);
        Assert.Equal(
            "message-dialog.json",
            options.QualityThemedMessageDialogReportPath);
        Assert.Equal(
            "plugin-settings.json",
            options.QualityPluginSettingsReportPath);
        Assert.Equal(
            "plugin-settings-persistence.json",
            options.QualityPluginSettingsPersistenceReportPath);
        Assert.Equal(
            "com.long.folder-note",
            options.QualityPluginSettingsId);
        Assert.Equal(
            "com.long.base64",
            options.QualityPluginRuntimeId);
        Assert.Equal(
            2,
            options.QualitySkippedAutoStartPluginIds.Count);
        Assert.Contains(
            "com.long.clipboardhistory",
            options.QualitySkippedAutoStartPluginIds);
        Assert.Contains(
            "com.long.macro",
            options.QualitySkippedAutoStartPluginIds);
        Assert.True(options.QualityHideWindowDuringIdle);
        Assert.Equal("startup.json", options.QualityStartupReportPath);
        Assert.True(options.QualityManagementCardShadows);
        Assert.True(options.ShowWelcomeForQuality);
        Assert.True(options.ShowMarketListForQuality);
        Assert.True(options.ShowMarketDetailForQuality);
        Assert.True(options.ShowMarketUpdateReviewForQuality);
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
        Assert.Null(options.LanguageOverride);
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
        Assert.True(AppStartupOptions.Parse(
            ["--quality-capture-view", "system"]).OpenSystemForQuality);
        Assert.True(AppStartupOptions.Parse(
            ["--quality-capture-view", "settings"]).OpenSettingsForQuality);
        Assert.True(AppStartupOptions.Parse(
            ["--quality-capture-view", "developer"]).OpenDeveloperForQuality);
    }
}
