using System.IO;
using System.Text.Json;
using System.Windows;
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
    }
}
