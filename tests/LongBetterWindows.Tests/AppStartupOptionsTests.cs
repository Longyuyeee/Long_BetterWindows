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
            "--open-plugin", "com.long.json",
            "--exit-after-command",
            "--quality-command-report", "command-report.json",
            "--quality-command-fixture", "command-fixture.json",
            "--quality-capture", "capture.png",
            "--quality-capture-view", "plugins",
            "--quality-render-dpi", "144",
            "--quality-width", "1280",
            "--quality-height", "800",
            "--quality-monitor-device", @"\\.\DISPLAY2",
            "--quality-high-contrast",
            "--quality-reduce-motion",
            "--quality-window-automation",
            "--quality-empty-context",
            "--quality-context", "IMAGE",
            "--quality-origin-window", "12345",
            "--quality-workflows-dir", "quality-workflows",
            "--quality-storage-path", "quality-storage.json",
            "--quality-open-workflow", "workflow.quality.review",
            "--quality-edit-workflow", "workflow.quality.editor",
            "--quality-workflow-upgrade-package", "quality-v2.lpak",
            "--quality-terminal-export-dir", "quality-exports",
            "--quality-plugin-page-release-report", "plugin-release.json",
            "--quality-plugin-page-performance-report", "plugin-performance.json",
            "--quality-plugin-runtime-session-report", "plugin-runtime-session.json",
            "--quality-webview-lifecycle-report", "webview-lifecycle.json",
            "--quality-workspace-switch-report", "workspace-switch.json",
            "--quality-background-activity-report", "background-activity.json",
            "--quality-tray-recovery-report", "tray-recovery.json",
            "--quality-session-recovery-report", "session-recovery.json",
            "--quality-power-recovery-report", "power-recovery.json",
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
            "--quality-compare-window-idle",
            "--quality-startup-report", "startup.json",
            "--quality-shutdown-report", "shutdown.json",
            "--quality-source-commit", "abc123",
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
        Assert.Equal("com.long.json", options.RequestedPluginId);
        Assert.True(options.ExitAfterCommand);
        Assert.Equal("command-report.json", options.QualityCommandReportPath);
        Assert.Equal("command-fixture.json", options.QualityCommandFixturePath);
        Assert.Equal("capture.png", options.QualityCapturePath);
        Assert.Equal("plugins", options.QualityCaptureView);
        Assert.True(options.OpenPluginsForQuality);
        Assert.Equal(144, options.QualityRenderDpi);
        Assert.Equal(1280, options.QualityCaptureWidth);
        Assert.Equal(800, options.QualityCaptureHeight);
        Assert.Equal(@"\\.\DISPLAY2", options.QualityMonitorDeviceName);
        Assert.True(options.ForceHighContrast);
        Assert.True(options.ForceReduceMotion);
        Assert.True(options.EnableWindowAutomationForQuality);
        Assert.True(options.UseEmptyContextForQuality);
        Assert.Equal("image", options.QualityContextProfile);
        Assert.Equal(new nint(12345), options.QualityOriginWindowHandle);
        Assert.Equal("quality-workflows", options.QualityWorkflowsDirectory);
        Assert.Equal("quality-storage.json", options.QualityStoragePath);
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
            "webview-lifecycle.json",
            options.QualityWebViewLifecycleReportPath);
        Assert.Equal(
            "workspace-switch.json",
            options.QualityWorkspaceSwitchReportPath);
        Assert.Equal(
            "background-activity.json",
            options.QualityBackgroundActivityReportPath);
        Assert.Equal(
            "tray-recovery.json",
            options.QualityTrayRecoveryReportPath);
        Assert.Equal(
            "session-recovery.json",
            options.QualitySessionRecoveryReportPath);
        Assert.Equal(
            "power-recovery.json",
            options.QualityPowerRecoveryReportPath);
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
        Assert.True(options.QualityCompareWindowIdle);
        Assert.Equal("startup.json", options.QualityStartupReportPath);
        Assert.Equal("shutdown.json", options.QualityShutdownReportPath);
        Assert.Equal("abc123", options.QualitySourceCommit);
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

    [Fact]
    public void Parse_FolderNoteCaptureViewIsPreserved()
    {
        var options = AppStartupOptions.Parse(
            ["--quality-capture-view", "folder-note"]);

        Assert.Equal("folder-note", options.QualityCaptureView);
    }

    [Fact]
    public void Parse_MissingOpenPluginValueDoesNotCreateARequest()
    {
        var options = AppStartupOptions.Parse(["--open-plugin"]);

        Assert.Null(options.RequestedPluginId);
    }
}
