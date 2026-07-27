namespace LongBetterWindows.Host.Services
{
    internal sealed class AppStartupOptions
    {
        public string? DirectNotePath { get; private init; }
        public bool IsDirectNoteMode => !string.IsNullOrWhiteSpace(DirectNotePath);
        public bool ShowDesignSystemPreview { get; private init; }
        public string? ThemeOverride { get; private init; }
        public string? LanguageOverride { get; private init; }
        public string? RequestedCommandKey { get; private init; }
        public string? RequestedCommandText { get; private init; }
        public string? RequestedPluginsDirectory { get; private init; }
        public bool ExitAfterCommand { get; private init; }
        public bool OpenPaletteForQuality { get; private init; }
        public bool OpenSuperPanelForQuality { get; private init; }
        public bool OpenMarketForQuality { get; private init; }
        public bool OpenDiagnosticsForQuality { get; private init; }
        public bool OpenPluginsForQuality { get; private init; }
        public string? QualityPluginPageReleaseReportPath { get; private init; }
        public string? QualityPluginPagePerformanceReportPath { get; private init; }
        public string? QualityTaskbarIdentityReportPath { get; private init; }
        public string? QualityUiServiceThemeReportPath { get; private init; }
        public string? QualityThemedMessageDialogReportPath { get; private init; }
        public string? QualityPluginSettingsReportPath { get; private init; }
        public string? QualityPluginSettingsPersistenceReportPath { get; private init; }
        public IReadOnlySet<string> QualitySkippedAutoStartPluginIds { get; private init; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool QualityHideWindowDuringIdle { get; private init; }
        public string? QualityStartupReportPath { get; private init; }
        public bool QualityManagementCardShadows { get; private init; }
        public bool OpenSystemForQuality { get; private init; }
        public bool OpenSettingsForQuality { get; private init; }
        public bool OpenDeveloperForQuality { get; private init; }
        public bool ShowWelcomeForQuality { get; private init; }
        public bool ShowMarketListForQuality { get; private init; }
        public string? MarketplaceCatalogPath { get; private init; }
        public string? MarketplaceTrustStorePath { get; private init; }
        public bool UseLiveContextForQuality { get; private init; }
        public bool UseEmptyContextForQuality { get; private init; }
        public string? QualityWorkflowsDirectory { get; private init; }
        public string? QualityWorkflowReviewId { get; private init; }
        public string? QualityWorkflowEditorId { get; private init; }
        public string? QualityWorkflowUpgradePackagePath { get; private init; }
        public string? QualityTerminalExportDirectory { get; private init; }
        public bool ForceHighContrast { get; private init; }
        public bool ForceReduceMotion { get; private init; }
        public int QualityIdleMilliseconds { get; private init; }
        public string? QualityCapturePath { get; private init; }
        public string QualityCaptureView { get; private init; } = "main";
        public int QualityRenderDpi { get; private init; } = 96;
        public int QualityCaptureDelayMilliseconds { get; private init; } = 700;
        public int QualityCaptureWidth { get; private init; }
        public int QualityCaptureHeight { get; private init; }

        public static AppStartupOptions Parse(IReadOnlyList<string> arguments)
        {
            var captureView = ReadArgument(arguments, "--quality-capture-view")?
                .ToLowerInvariant() ?? "main";
            var themeOverride = ReadArgument(arguments, "--theme")?.ToLowerInvariant();
            var languageOverride = ReadArgument(arguments, "--language");
            var pluginPageReleaseReport = ReadArgument(
                arguments,
                "--quality-plugin-page-release-report");
            var pluginPagePerformanceReport = ReadArgument(
                arguments,
                "--quality-plugin-page-performance-report");

            return new AppStartupOptions
            {
                DirectNotePath = ReadArgument(arguments, "--note"),
                ShowDesignSystemPreview = HasSwitch(arguments, "--design-system-preview"),
                ThemeOverride = themeOverride is "light" or "dark" or "system"
                    ? themeOverride
                    : null,
                LanguageOverride = I18nService.IsSupported(languageOverride)
                    ? I18nService.SupportedLanguages.First(language =>
                        string.Equals(
                            language,
                            languageOverride,
                            StringComparison.OrdinalIgnoreCase))
                    : null,
                RequestedCommandKey = ReadArgument(arguments, "--run-command")?.ToLowerInvariant(),
                RequestedCommandText = ReadArgument(arguments, "--command-text"),
                RequestedPluginsDirectory = ReadArgument(arguments, "--plugins-dir"),
                ExitAfterCommand = HasSwitch(arguments, "--exit-after-command"),
                OpenPaletteForQuality = HasSwitch(arguments, "--quality-open-palette"),
                OpenSuperPanelForQuality = HasSwitch(arguments, "--quality-open-super-panel"),
                OpenMarketForQuality = HasSwitch(arguments, "--quality-open-market") || captureView == "market",
                OpenDiagnosticsForQuality = HasSwitch(arguments, "--quality-open-diagnostics") || captureView == "diagnostics",
                OpenPluginsForQuality = HasSwitch(arguments, "--quality-open-plugins")
                    || captureView == "plugins"
                    || !string.IsNullOrWhiteSpace(pluginPageReleaseReport)
                    || !string.IsNullOrWhiteSpace(pluginPagePerformanceReport),
                QualityPluginPageReleaseReportPath = pluginPageReleaseReport,
                QualityPluginPagePerformanceReportPath =
                    pluginPagePerformanceReport,
                QualityTaskbarIdentityReportPath = ReadArgument(
                    arguments,
                    "--quality-taskbar-identity-report"),
                QualityUiServiceThemeReportPath = ReadArgument(
                    arguments,
                    "--quality-ui-service-theme-report"),
                QualityThemedMessageDialogReportPath = ReadArgument(
                    arguments,
                    "--quality-themed-message-dialog-report"),
                QualityPluginSettingsReportPath = ReadArgument(
                    arguments,
                    "--quality-plugin-settings-report"),
                QualityPluginSettingsPersistenceReportPath = ReadArgument(
                    arguments,
                    "--quality-plugin-settings-persistence-report"),
                QualitySkippedAutoStartPluginIds = ReadArguments(
                    arguments,
                    "--quality-skip-auto-start-plugin"),
                QualityHideWindowDuringIdle = HasSwitch(
                    arguments,
                    "--quality-hide-window-during-idle"),
                QualityStartupReportPath = ReadArgument(
                    arguments,
                    "--quality-startup-report"),
                QualityManagementCardShadows = HasSwitch(
                    arguments,
                    "--quality-management-card-shadows"),
                OpenSystemForQuality = HasSwitch(arguments, "--quality-open-system") || captureView == "system",
                OpenSettingsForQuality = HasSwitch(arguments, "--quality-open-settings") || captureView == "settings",
                OpenDeveloperForQuality = HasSwitch(arguments, "--quality-open-developer") || captureView == "developer",
                ShowWelcomeForQuality = HasSwitch(arguments, "--quality-show-welcome"),
                ShowMarketListForQuality = HasSwitch(arguments, "--quality-market-list"),
                MarketplaceCatalogPath = ReadArgument(arguments, "--quality-market-catalog"),
                MarketplaceTrustStorePath = ReadArgument(arguments, "--quality-market-trust-store"),
                UseLiveContextForQuality = HasSwitch(arguments, "--quality-live-context"),
                UseEmptyContextForQuality = HasSwitch(arguments, "--quality-empty-context"),
                QualityWorkflowsDirectory = ReadArgument(arguments, "--quality-workflows-dir"),
                QualityWorkflowReviewId = ReadArgument(arguments, "--quality-open-workflow"),
                QualityWorkflowEditorId = ReadArgument(arguments, "--quality-edit-workflow"),
                QualityWorkflowUpgradePackagePath = ReadArgument(
                    arguments,
                    "--quality-workflow-upgrade-package"),
                QualityTerminalExportDirectory = ReadArgument(
                    arguments,
                    "--quality-terminal-export-dir"),
                ForceHighContrast = HasSwitch(arguments, "--quality-high-contrast"),
                ForceReduceMotion = HasSwitch(arguments, "--quality-reduce-motion"),
                QualityIdleMilliseconds = ReadIntegerArgument(arguments, "--quality-idle-ms", 0, 60_000),
                QualityCapturePath = ReadArgument(arguments, "--quality-capture"),
                QualityCaptureView = captureView,
                QualityRenderDpi = ReadIntegerArgument(arguments, "--quality-render-dpi", 96, 384),
                QualityCaptureDelayMilliseconds = ReadIntegerArgument(
                    arguments, "--quality-capture-delay-ms", 700, 10_000, 100),
                QualityCaptureWidth = ReadIntegerArgument(arguments, "--quality-width", 0, 3840),
                QualityCaptureHeight = ReadIntegerArgument(arguments, "--quality-height", 0, 2160),
            };
        }

        private static bool HasSwitch(IReadOnlyList<string> arguments, string name)
            => arguments.Any(argument =>
                string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

        private static string? ReadArgument(IReadOnlyList<string> arguments, string name)
        {
            for (var index = 0; index < arguments.Count - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
                    return arguments[index + 1].Trim();
            }

            return null;
        }

        private static IReadOnlySet<string> ReadArguments(
            IReadOnlyList<string> arguments,
            string name)
        {
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < arguments.Count - 1; index++)
            {
                if (string.Equals(
                        arguments[index],
                        name,
                        StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(arguments[index + 1]))
                {
                    values.Add(arguments[index + 1].Trim());
                }
            }
            return values;
        }

        private static int ReadIntegerArgument(
            IReadOnlyList<string> arguments,
            string name,
            int fallback,
            int maximum,
            int minimum = 0)
        {
            var value = ReadArgument(arguments, name);
            return int.TryParse(value, out var parsed)
                ? Math.Clamp(parsed, minimum, maximum)
                : fallback;
        }
    }
}
