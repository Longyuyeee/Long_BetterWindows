using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Views;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    internal sealed class QualityRuntimeService
    {
        private readonly Application _application;

        public QualityRuntimeService(Application application)
        {
            _application = application;
        }

        public async Task CaptureAsync(
            AppStartupOptions options,
            bool isLight,
            bool highContrast,
            bool reducedMotion)
        {
            await Task.Delay(options.QualityCaptureDelayMilliseconds);
            await _application.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);

            var target = options.QualityCaptureView switch
            {
                "palette" => _application.Windows.OfType<CommandPaletteWindow>()
                    .FirstOrDefault(window => window.IsVisible),
                "super-panel" => _application.Windows.OfType<SuperPanelWindow>()
                    .FirstOrDefault(window => window.IsVisible),
                "plugin" => _application.Windows.OfType<PluginWindowHost>()
                    .FirstOrDefault(window => window.IsVisible) ?? _application.MainWindow,
                "main" or "market" or "diagnostics" or "plugins" or "system"
                    or "settings" or "developer"
                    => _application.MainWindow,
                _ => throw new InvalidDataException(
                    $"Unsupported quality capture view: {options.QualityCaptureView}"),
            } ?? throw new InvalidDataException(
                $"Quality capture window is not visible: {options.QualityCaptureView}");

            if (options.QualityCaptureWidth > 0) target.Width = options.QualityCaptureWidth;
            if (options.QualityCaptureHeight > 0) target.Height = options.QualityCaptureHeight;
            target.WindowState = WindowState.Normal;
            target.UpdateLayout();
            await _application.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            if (options.ShowMarketListForQuality &&
                target is MainWindow marketplaceWindow)
            {
                marketplaceWindow.ShowMarketplaceListForQuality();
                target.UpdateLayout();
                await _application.Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Render);
            }

            var logicalWidth = Math.Max(1, target.ActualWidth);
            var logicalHeight = Math.Max(1, target.ActualHeight);
            var path = Path.GetFullPath(options.QualityCapturePath!);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (options.QualityCaptureView == "plugin")
            {
                await CaptureWebViewAsync(
                    target, path, options, logicalWidth, logicalHeight,
                    isLight, highContrast, reducedMotion);
                _application.Shutdown(0);
                return;
            }

            var pixelWidth = Math.Max(
                1, (int)Math.Ceiling(logicalWidth * options.QualityRenderDpi / 96d));
            var pixelHeight = Math.Max(
                1, (int)Math.Ceiling(logicalHeight * options.QualityRenderDpi / 96d));
            var bitmap = new RenderTargetBitmap(
                pixelWidth, pixelHeight,
                options.QualityRenderDpi, options.QualityRenderDpi,
                PixelFormats.Pbgra32);
            bitmap.Render(target);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using (var stream = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                encoder.Save(stream);
                await stream.FlushAsync();
            }

            var actualDpi = VisualTreeHelper.GetDpi(target);
            await WriteCaptureMetadataAsync(
                path, options, logicalWidth, logicalHeight, pixelWidth, pixelHeight,
                actualDpi.PixelsPerInchX, "wpf_render_target",
                isLight, highContrast, reducedMotion);
            Log.Information(
                "Quality capture complete: View={View}, RenderDpi={RenderDpi}, ActualDpi={ActualDpi}, Path={Path}",
                options.QualityCaptureView, options.QualityRenderDpi,
                actualDpi.PixelsPerInchX, path);
            _application.Shutdown(0);
        }

        public async Task RunIdleProbeAsync(
            int delayMilliseconds,
            int pluginCount,
            int commandCount,
            bool highContrast,
            bool reducedMotion)
        {
            await Task.Delay(delayMilliseconds);
            var process = System.Diagnostics.Process.GetCurrentProcess();
            process.Refresh();
            Log.Information(
                "Quality idle sample: Plugins={PluginCount}, Commands={CommandCount}, WorkingSetMB={WorkingSetMB:F1}, PrivateMB={PrivateMB:F1}, HighContrast={HighContrast}, ReducedMotion={ReducedMotion}",
                pluginCount,
                commandCount,
                process.WorkingSet64 / 1024d / 1024d,
                process.PrivateMemorySize64 / 1024d / 1024d,
                highContrast,
                reducedMotion);
            _application.Shutdown(0);
        }

        public async Task RunPluginPageReleaseProbeAsync(
            MainWindow window,
            string reportPath)
        {
            await Task.Delay(500);
            await _application.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ContextIdle);

            var process = System.Diagnostics.Process.GetCurrentProcess();
            process.Refresh();
            var beforeWorkingSet = process.WorkingSet64;
            var beforePrivate = process.PrivateMemorySize64;

            var reference = await _application.Dispatcher.InvokeAsync(
                window.ReleasePluginManagementForQuality,
                DispatcherPriority.Send);
            await Task.Delay(300);
            await _application.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ContextIdle);

            for (var attempt = 0; attempt < 3 && reference.IsAlive; attempt++)
            {
                GC.Collect(
                    GC.MaxGeneration,
                    GCCollectionMode.Forced,
                    blocking: true,
                    compacting: true);
                GC.WaitForPendingFinalizers();
            }

            process.Refresh();
            var collected = !reference.IsAlive;
            var fullPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(
                fullPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schema_version = 1,
                        captured_at = DateTimeOffset.UtcNow,
                        plugin_page_collected = collected,
                        before_working_set_mb =
                            Math.Round(beforeWorkingSet / 1024d / 1024d, 1),
                        after_working_set_mb =
                            Math.Round(process.WorkingSet64 / 1024d / 1024d, 1),
                        before_private_mb =
                            Math.Round(beforePrivate / 1024d / 1024d, 1),
                        after_private_mb =
                            Math.Round(process.PrivateMemorySize64 / 1024d / 1024d, 1),
                    },
                    new JsonSerializerOptions { WriteIndented = true }));

            Log.Information(
                "Plugin page release probe: Collected={Collected}, Report={Report}",
                collected,
                fullPath);
            _application.Shutdown(collected ? 0 : 4);
        }

        public async Task RunTaskbarIdentityProbeAsync(string reportPath)
        {
            const string firstPluginId = "com.long.quality-taskbar";
            const string secondPluginId = "com.long.quality_taskbar";
            var first = new PluginWindowHost(
                firstPluginId,
                "Taskbar Alpha",
                new System.Windows.Controls.Border());
            var second = new PluginWindowHost(
                secondPluginId,
                "Taskbar Beta",
                new System.Windows.Controls.Border());

            try
            {
                first.Show();
                second.Show();
                await _application.Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.ContextIdle);

                var firstExpected =
                    PluginTaskbarIdentity.CreateAppUserModelId(firstPluginId);
                var secondExpected =
                    PluginTaskbarIdentity.CreateAppUserModelId(secondPluginId);
                var firstActual =
                    PluginTaskbarIdentity.ReadAppUserModelId(first);
                var secondActual =
                    PluginTaskbarIdentity.ReadAppUserModelId(second);
                var firstIcon = GetIconFingerprint(first.Icon);
                var secondIcon = GetIconFingerprint(second.Icon);
                var passed =
                    firstActual == firstExpected
                    && secondActual == secondExpected
                    && firstActual != secondActual
                    && firstIcon is not null
                    && secondIcon is not null
                    && firstIcon != secondIcon
                    && first.Owner is null
                    && second.Owner is null
                    && first.ShowInTaskbar
                    && second.ShowInTaskbar;

                var fullPath = Path.GetFullPath(reportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(
                    fullPath,
                    JsonSerializer.Serialize(
                        new
                        {
                            schema_version = 1,
                            captured_at = DateTimeOffset.UtcNow,
                            passed,
                            windows = new[]
                            {
                                new
                                {
                                    plugin_id = firstPluginId,
                                    expected_app_user_model_id = firstExpected,
                                    actual_app_user_model_id = firstActual,
                                    icon_sha256 = firstIcon,
                                    has_owner = first.Owner is not null,
                                    show_in_taskbar = first.ShowInTaskbar,
                                },
                                new
                                {
                                    plugin_id = secondPluginId,
                                    expected_app_user_model_id = secondExpected,
                                    actual_app_user_model_id = secondActual,
                                    icon_sha256 = secondIcon,
                                    has_owner = second.Owner is not null,
                                    show_in_taskbar = second.ShowInTaskbar,
                                },
                            },
                        },
                        new JsonSerializerOptions { WriteIndented = true }));
                _application.Shutdown(passed ? 0 : 5);
            }
            finally
            {
                first.Close();
                second.Close();
            }
        }

        public async Task RunUiServiceThemeProbeAsync(string reportPath)
        {
            var originalTheme = App.IsLightTheme;
            var dialog = UIService.CreatePromptDialogForQuality();
            var fullPath = Path.GetFullPath(reportPath);
            var reportDirectory = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(reportDirectory);
            var darkScreenshotPath = Path.Combine(
                reportDirectory,
                "ui-service-dark.png");
            var lightScreenshotPath = Path.Combine(
                reportDirectory,
                "ui-service-light.png");
            try
            {
                dialog.Show();
                App.UpdateThemeResources(isLight: false);
                await _application.Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Render);
                dialog.UpdateLayout();
                var dark = CaptureDialogTheme(dialog, "dark");
                await CaptureWindowAsync(dialog, darkScreenshotPath);

                App.UpdateThemeResources(isLight: true);
                await _application.Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Render);
                dialog.UpdateLayout();
                var light = CaptureDialogTheme(dialog, "light");
                await CaptureWindowAsync(dialog, lightScreenshotPath);

                var changed =
                    dark.WindowBackground != light.WindowBackground
                    && dark.InputBackground != light.InputBackground
                    && dark.SecondaryButtonBackground
                        != light.SecondaryButtonBackground;
                var passed = changed && dark.Passed && light.Passed;
                await File.WriteAllTextAsync(
                    fullPath,
                    JsonSerializer.Serialize(
                        new
                        {
                            schema_version = 1,
                            captured_at = DateTimeOffset.UtcNow,
                            passed,
                            runtime_theme_changed = changed,
                            screenshots = new
                            {
                                dark = Path.GetFileName(darkScreenshotPath),
                                dark_sha256 = GetFileSha256(darkScreenshotPath),
                                light = Path.GetFileName(lightScreenshotPath),
                                light_sha256 = GetFileSha256(lightScreenshotPath),
                            },
                            themes = new[] { dark, light },
                        },
                        new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                        }));
                _application.Shutdown(passed ? 0 : 6);
            }
            finally
            {
                dialog.Close();
                App.UpdateThemeResources(originalTheme);
            }
        }

        public async Task RunPluginPagePerformanceProbeAsync(
            MainWindow window,
            PluginPagePerformanceTrace trace,
            PluginRuntimeStartResult result,
            int idleMilliseconds,
            bool hideWindowDuringIdle)
        {
            trace.Mark(
                "plugin_runtime_ready",
                window.GetPluginPageVisualMetricsForQuality());
            using var windowMessages = new WindowMessageActivityTrace(window);
            windowMessages.Mark("plugin_runtime_ready");
            await Task.Delay(200);
            await _application.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ContextIdle);
            trace.Mark(
                "plugin_page_settled",
                window.GetPluginPageVisualMetricsForQuality());
            windowMessages.Mark("plugin_page_settled");
            if (hideWindowDuringIdle)
            {
                await _application.Dispatcher.InvokeAsync(
                    window.Hide,
                    DispatcherPriority.Send);
                trace.SetWindowVisibleDuringIdle(false);
                trace.Mark("window_hidden");
            }

            var checkpoints = new SortedSet<int> { idleMilliseconds };
            if (idleMilliseconds > 1_000)
                checkpoints.Add(1_000);
            if (idleMilliseconds > 3_000)
                checkpoints.Add(3_000);

            var elapsed = 0;
            foreach (var checkpoint in checkpoints)
            {
                await Task.Delay(checkpoint - elapsed);
                elapsed = checkpoint;
                await _application.Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.ContextIdle);
                trace.Mark(
                    checkpoint == idleMilliseconds
                        ? "plugin_page_idle"
                        : $"plugin_page_idle_{checkpoint}ms",
                    window.GetPluginPageVisualMetricsForQuality());
                windowMessages.Mark(
                    checkpoint == idleMilliseconds
                        ? "plugin_page_idle"
                        : $"plugin_page_idle_{checkpoint}ms");
            }
            trace.SetWindowMessageCheckpoints(windowMessages.Checkpoints);
            var registry = HostProvider.Instance.PluginStore;
            await trace.WriteAsync(
                result,
                registry.Commands.Count,
                idleMilliseconds,
                registry.GetAll()
                    .Where(entry => entry.State == PluginState.Running)
                    .Select(entry => entry.Id)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            _application.Shutdown(0);
        }

        private static async Task CaptureWebViewAsync(
            DependencyObject target,
            string path,
            AppStartupOptions options,
            double logicalWidth,
            double logicalHeight,
            bool isLight,
            bool highContrast,
            bool reducedMotion)
        {
            var webView = FindVisualChild<WebView2>(target)
                ?? throw new InvalidDataException("Quality capture could not find an active WebView2.");
            if (webView.CoreWebView2 == null)
                throw new InvalidDataException("WebView2 is not initialized for quality capture.");

            await using (var stream = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await webView.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png, stream);
                await stream.FlushAsync();
            }

            var webDpi = VisualTreeHelper.GetDpi(webView);
            await WriteCaptureMetadataAsync(
                path, options, logicalWidth, logicalHeight,
                Math.Max(1, (int)Math.Ceiling(webView.ActualWidth * webDpi.DpiScaleX)),
                Math.Max(1, (int)Math.Ceiling(webView.ActualHeight * webDpi.DpiScaleY)),
                webDpi.PixelsPerInchX, "webview_preview",
                isLight, highContrast, reducedMotion);
        }

        private static string? GetIconFingerprint(ImageSource? icon)
        {
            if (icon is not BitmapSource bitmap)
                return null;

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return Convert.ToHexString(SHA256.HashData(stream.ToArray()))
                .ToLowerInvariant();
        }

        private static async Task CaptureWindowAsync(
            Window window,
            string path)
        {
            var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
            var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
            var bitmap = new RenderTargetBitmap(
                width,
                height,
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                true);
            encoder.Save(stream);
            await stream.FlushAsync();
        }

        private static string GetFileSha256(string path)
            => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                .ToLowerInvariant();

        private static DialogThemeSnapshot CaptureDialogTheme(
            Window dialog,
            string theme)
        {
            var label = FindVisualChildren<TextBlock>(dialog).First();
            var input = FindVisualChildren<System.Windows.Controls.TextBox>(
                dialog).Single();
            var buttons = FindVisualChildren<System.Windows.Controls.Button>(
                dialog).ToArray();
            var primary = buttons.Single(button =>
                System.Windows.Automation.AutomationProperties
                    .GetAutomationId(button)
                    .EndsWith(".Confirm", StringComparison.Ordinal));
            var secondary = buttons.Single(button =>
                System.Windows.Automation.AutomationProperties
                    .GetAutomationId(button)
                    .EndsWith(".Cancel", StringComparison.Ordinal));

            var windowBackground = GetColor(dialog.Background);
            var labelForeground = GetColor(label.Foreground);
            var inputBackground = GetColor(input.Background);
            var inputForeground = GetColor(input.Foreground);
            var primaryBackground = GetColor(primary.Background);
            var primaryForeground = GetColor(primary.Foreground);
            var secondaryBackground = GetColor(secondary.Background);
            var secondaryForeground = GetColor(secondary.Foreground);
            var labelContrast = ContrastRatio(
                labelForeground,
                windowBackground);
            var inputContrast = ContrastRatio(
                inputForeground,
                inputBackground);
            var primaryContrast = ContrastRatio(
                primaryForeground,
                primaryBackground);
            var secondaryContrast = ContrastRatio(
                secondaryForeground,
                secondaryBackground);

            return new DialogThemeSnapshot(
                theme,
                ToHex(windowBackground),
                ToHex(labelForeground),
                ToHex(inputBackground),
                ToHex(inputForeground),
                ToHex(primaryBackground),
                ToHex(primaryForeground),
                ToHex(secondaryBackground),
                ToHex(secondaryForeground),
                Math.Round(labelContrast, 2),
                Math.Round(inputContrast, 2),
                Math.Round(primaryContrast, 2),
                Math.Round(secondaryContrast, 2),
                labelContrast >= 4.5
                    && inputContrast >= 4.5
                    && primaryContrast >= 4.5
                    && secondaryContrast >= 4.5);
        }

        private static Color GetColor(Brush brush)
            => brush is SolidColorBrush solid
                ? solid.Color
                : throw new InvalidDataException(
                    "UI service theme probe requires solid semantic brushes.");

        private static double ContrastRatio(Color first, Color second)
        {
            var firstLuminance = RelativeLuminance(first);
            var secondLuminance = RelativeLuminance(second);
            return (Math.Max(firstLuminance, secondLuminance) + 0.05)
                / (Math.Min(firstLuminance, secondLuminance) + 0.05);
        }

        private static double RelativeLuminance(Color color)
        {
            static double Linearize(byte component)
            {
                var value = component / 255d;
                return value <= 0.04045
                    ? value / 12.92
                    : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Linearize(color.R)
                + 0.7152 * Linearize(color.G)
                + 0.0722 * Linearize(color.B);
        }

        private static string ToHex(Color color)
            => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        private static async Task WriteCaptureMetadataAsync(
            string path,
            AppStartupOptions options,
            double logicalWidth,
            double logicalHeight,
            int pixelWidth,
            int pixelHeight,
            double actualDpi,
            string captureKind,
            bool isLight,
            bool highContrast,
            bool reducedMotion)
        {
            var metadata = new
            {
                schema_version = 1,
                captured_at = DateTimeOffset.UtcNow,
                view = options.QualityCaptureView,
                theme = isLight ? "light" : "dark",
                render_dpi = options.QualityRenderDpi,
                actual_monitor_dpi = actualDpi,
                capture_kind = captureKind,
                logical_width = logicalWidth,
                logical_height = logicalHeight,
                pixel_width = pixelWidth,
                pixel_height = pixelHeight,
                high_contrast = highContrast,
                reduced_motion = reducedMotion,
            };
            await File.WriteAllTextAsync(
                path + ".json",
                System.Text.Json.JsonSerializer.Serialize(
                    metadata,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        private static T? FindVisualChild<T>(DependencyObject parent)
            where T : DependencyObject
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T match) return match;
                var nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }

            return null;
        }

        private static IEnumerable<T> FindVisualChildren<T>(
            DependencyObject parent)
            where T : DependencyObject
        {
            for (var index = 0;
                 index < VisualTreeHelper.GetChildrenCount(parent);
                 index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T match)
                    yield return match;
                foreach (var nested in FindVisualChildren<T>(child))
                    yield return nested;
            }
        }

        private sealed record DialogThemeSnapshot(
            string Theme,
            string WindowBackground,
            string LabelForeground,
            string InputBackground,
            string InputForeground,
            string PrimaryButtonBackground,
            string PrimaryButtonForeground,
            string SecondaryButtonBackground,
            string SecondaryButtonForeground,
            double LabelContrast,
            double InputContrast,
            double PrimaryButtonContrast,
            double SecondaryButtonContrast,
            bool Passed);
    }
}
