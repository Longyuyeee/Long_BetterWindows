namespace LongBetterWindows.Host.Services
{
    internal sealed class AppStartupOptions
    {
        public string? DirectNotePath { get; private init; }
        public bool IsDirectNoteMode => !string.IsNullOrWhiteSpace(DirectNotePath);
        public bool ShowDesignSystemPreview { get; private init; }
        public string? ThemeOverride { get; private init; }
        public string? RequestedCommandKey { get; private init; }
        public string? RequestedCommandText { get; private init; }
        public string? RequestedPluginsDirectory { get; private init; }
        public bool ExitAfterCommand { get; private init; }
        public bool OpenPaletteForQuality { get; private init; }
        public bool OpenSuperPanelForQuality { get; private init; }
        public bool OpenMarketForQuality { get; private init; }
        public bool OpenDiagnosticsForQuality { get; private init; }
        public bool OpenPluginsForQuality { get; private init; }
        public string? MarketplaceCatalogPath { get; private init; }
        public string? MarketplaceTrustStorePath { get; private init; }
        public bool UseLiveContextForQuality { get; private init; }
        public bool UseEmptyContextForQuality { get; private init; }
        public string? QualityWorkflowsDirectory { get; private init; }
        public string? QualityWorkflowReviewId { get; private init; }
        public string? QualityWorkflowUpgradePackagePath { get; private init; }
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

            return new AppStartupOptions
            {
                DirectNotePath = ReadArgument(arguments, "--note"),
                ShowDesignSystemPreview = HasSwitch(arguments, "--design-system-preview"),
                ThemeOverride = themeOverride is "light" or "dark" or "system"
                    ? themeOverride
                    : null,
                RequestedCommandKey = ReadArgument(arguments, "--run-command")?.ToLowerInvariant(),
                RequestedCommandText = ReadArgument(arguments, "--command-text"),
                RequestedPluginsDirectory = ReadArgument(arguments, "--plugins-dir"),
                ExitAfterCommand = HasSwitch(arguments, "--exit-after-command"),
                OpenPaletteForQuality = HasSwitch(arguments, "--quality-open-palette"),
                OpenSuperPanelForQuality = HasSwitch(arguments, "--quality-open-super-panel"),
                OpenMarketForQuality = HasSwitch(arguments, "--quality-open-market") || captureView == "market",
                OpenDiagnosticsForQuality = HasSwitch(arguments, "--quality-open-diagnostics") || captureView == "diagnostics",
                OpenPluginsForQuality = HasSwitch(arguments, "--quality-open-plugins") || captureView == "plugins",
                MarketplaceCatalogPath = ReadArgument(arguments, "--quality-market-catalog"),
                MarketplaceTrustStorePath = ReadArgument(arguments, "--quality-market-trust-store"),
                UseLiveContextForQuality = HasSwitch(arguments, "--quality-live-context"),
                UseEmptyContextForQuality = HasSwitch(arguments, "--quality-empty-context"),
                QualityWorkflowsDirectory = ReadArgument(arguments, "--quality-workflows-dir"),
                QualityWorkflowReviewId = ReadArgument(arguments, "--quality-open-workflow"),
                QualityWorkflowUpgradePackagePath = ReadArgument(
                    arguments,
                    "--quality-workflow-upgrade-package"),
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
