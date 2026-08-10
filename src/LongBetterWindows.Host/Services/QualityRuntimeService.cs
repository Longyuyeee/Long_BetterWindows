using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Views;
using LongBetterWindows.PluginSdk.Wpf;
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
                "folder-note" => _application.Windows
                    .OfType<AnchoredTextEditorWindow>()
                    .FirstOrDefault(window => window.IsVisible),
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
            if (options.ShowMarketDetailForQuality
                && target is MainWindow marketplaceDetailWindow)
            {
                if (!marketplaceDetailWindow.ShowMarketplaceDetailForQuality())
                {
                    throw new InvalidOperationException(
                        "Marketplace detail route could not open.");
                }
                target.UpdateLayout();
                await _application.Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Render);
            }
            if (options.ShowMarketUpdateReviewForQuality
                && target is MainWindow marketplaceUpdateWindow)
            {
                if (!marketplaceUpdateWindow.ShowMarketplaceUpdateReviewForQuality())
                {
                    throw new InvalidOperationException(
                        "Marketplace update review could not open.");
                }
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
            const int expectedPluginCount = 25;
            var entries = HostProvider.Instance.PluginStore.GetAll()
                .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var windows = entries
                .Select(entry =>
                {
                    var window = new PluginWindowHost(
                        entry.Id,
                        entry.DisplayName,
                        new Border())
                    {
                        ShowActivated = false,
                    };
                    return (Entry: entry, Window: window);
                })
                .ToArray();

            try
            {
                foreach (var item in windows)
                    item.Window.Show();
                await _application.Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.ContextIdle);

                var results = windows
                    .Select(item =>
                    {
                        var expected = PluginTaskbarIdentity
                            .CreateAppUserModelId(item.Entry.Id);
                        var actual = PluginTaskbarIdentity
                            .ReadAppUserModelId(item.Window);
                        var icon = GetIconFingerprint(item.Window.Icon);
                        return new
                        {
                            plugin_id = item.Entry.Id,
                            plugin_title = item.Entry.DisplayName,
                            expected_app_user_model_id = expected,
                            actual_app_user_model_id = actual,
                            icon_sha256 = icon,
                            has_owner = item.Window.Owner is not null,
                            show_in_taskbar = item.Window.ShowInTaskbar,
                        };
                    })
                    .ToArray();
                var distinctExpectedCount = results
                    .Select(result => result.expected_app_user_model_id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                var distinctActualCount = results
                    .Select(result => result.actual_app_user_model_id)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                var distinctIconCount = results
                    .Select(result => result.icon_sha256)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                var passed =
                    results.Length == expectedPluginCount
                    && distinctExpectedCount == expectedPluginCount
                    && distinctActualCount == expectedPluginCount
                    && distinctIconCount == expectedPluginCount
                    && results.All(result =>
                        result.actual_app_user_model_id
                            == result.expected_app_user_model_id
                        && result.icon_sha256 is not null
                        && !result.has_owner
                        && result.show_in_taskbar);

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
                            expected_plugin_count = expectedPluginCount,
                            plugin_count = results.Length,
                            distinct_expected_identity_count =
                                distinctExpectedCount,
                            distinct_actual_identity_count =
                                distinctActualCount,
                            distinct_icon_count = distinctIconCount,
                            windows = results,
                        },
                        new JsonSerializerOptions { WriteIndented = true }));
                _application.Shutdown(passed ? 0 : 5);
            }
            finally
            {
                foreach (var item in windows)
                    item.Window.Close();
            }
        }

        public async Task RunPluginRuntimeSessionProbeAsync(
            MainWindow mainWindow,
            string reportPath)
        {
            const string pluginId = "com.long.base64";
            var registry = HostProvider.Instance.PluginStore;
            var entry = registry.Get(pluginId)
                ?? throw new InvalidOperationException(
                    $"Quality runtime plugin was not found: {pluginId}");

            async Task OpenAsync()
                => await OpenPluginRuntimeAsync(mainWindow, pluginId);

            await OpenAsync();
            var embeddedReady = await WaitUntilAsync(
                () =>
                {
                    var state = mainWindow.GetPluginRuntimeQualityState();
                    return state.IsVisible
                        && !state.IsDetached
                        && state.ContentIdentity != 0;
                },
                15_000);
            var initial = mainWindow.GetPluginRuntimeQualityState();

            var webView = mainWindow.GetPluginRuntimeContentForQuality()
                as WebView2;
            var webViewReady = await WaitUntilAsync(
                () => webView?.CoreWebView2 is not null,
                15_000);
            PluginRuntimeInputProbeResult? inputProbe = null;
            if (webViewReady && webView?.CoreWebView2 is not null)
                inputProbe = await PluginRuntimeInputProbe.RunAsync(
                    mainWindow,
                    webView);

            var detachRequested = inputProbe?.ControlDSent == true;
            var detachedReady = await WaitUntilAsync(
                () =>
                    mainWindow.GetPluginRuntimeQualityState().IsDetached
                    && _application.Windows.OfType<PluginWindowHost>()
                        .Any(window => window.IsVisible),
                10_000);
            var detached = mainWindow.GetPluginRuntimeQualityState();
            var detachedDom = webView?.CoreWebView2 is null
                ? new PluginRuntimeDomSnapshot(string.Empty, 0)
                : await PluginRuntimeInputProbe.CaptureAsync(webView);
            var detachedWindow = _application.Windows
                .OfType<PluginWindowHost>()
                .FirstOrDefault(window => window.IsVisible);
            detachedWindow?.ReturnToOwnerForQuality();

            var returnedReady = await WaitUntilAsync(
                () =>
                {
                    var state = mainWindow.GetPluginRuntimeQualityState();
                    return state.IsVisible
                        && !state.IsDetached
                        && state.ContentIdentity == initial.ContentIdentity;
                },
                10_000);
            var returned = mainWindow.GetPluginRuntimeQualityState();
            var returnedDom = webView?.CoreWebView2 is null
                ? new PluginRuntimeDomSnapshot(string.Empty, 0)
                : await PluginRuntimeInputProbe.CaptureAsync(webView);

            var closeRequested =
                await mainWindow.ClosePluginRuntimeForQualityAsync();
            var closeStopped = await WaitUntilAsync(
                () => entry.State == PluginState.Stopped,
                10_000);
            var firstSessionEnded =
                initial.SessionId is not null
                && ServicesInitializer.PluginSessions.GetBySessionId(
                    initial.SessionId) is null;

            await OpenAsync();
            var reopenedReady = await WaitUntilAsync(
                () =>
                {
                    var state = mainWindow.GetPluginRuntimeQualityState();
                    return state.IsVisible
                        && !state.IsDetached
                        && state.SessionId is not null
                        && state.SessionId != initial.SessionId;
                },
                15_000);
            var reopened = mainWindow.GetPluginRuntimeQualityState();
            var endRequested =
                await mainWindow.EndPluginRuntimeForQualityAsync();
            var endStopped = await WaitUntilAsync(
                () => entry.State == PluginState.Stopped,
                10_000);
            var secondSessionEnded =
                reopened.SessionId is not null
                && ServicesInitializer.PluginSessions.GetBySessionId(
                    reopened.SessionId) is null;

            const string backgroundPluginId = "com.long.clipboardhistory";
            var backgroundEntry = registry.Get(backgroundPluginId)
                ?? throw new InvalidOperationException(
                    $"Quality background plugin was not found: {backgroundPluginId}");

            async Task OpenBackgroundAsync()
            {
                if (backgroundEntry.State != PluginState.Running
                    && !await registry.StartPluginAsync(
                        backgroundPluginId,
                        persistAutoStart: false))
                {
                    throw new InvalidOperationException(
                        "Quality background plugin could not start or resume.");
                }
                if (backgroundEntry.Instance is not IHasMainUI mainUi)
                {
                    throw new InvalidOperationException(
                        "Quality background plugin does not expose a main UI.");
                }
                mainUi.ShowMainUI();
            }

            await OpenBackgroundAsync();
            var backgroundEmbeddedReady = await WaitUntilAsync(
                () =>
                {
                    var state = mainWindow.GetPluginRuntimeQualityState();
                    return backgroundEntry.State == PluginState.Running
                        && state.IsVisible
                        && !state.IsDetached
                        && state.ContentIdentity != 0;
                },
                15_000);
            var backgroundInitial =
                mainWindow.GetPluginRuntimeQualityState();

            var backgroundCloseRequested =
                await mainWindow.ClosePluginRuntimeForQualityAsync();
            var backgroundStateReady = await WaitUntilAsync(
                () => backgroundEntry.State == PluginState.Background,
                10_000);
            var backgroundSessionHidden =
                backgroundInitial.SessionId is not null
                && ServicesInitializer.PluginSessions.GetBySessionId(
                    backgroundInitial.SessionId)?.State.Placement
                    == PluginWorkspacePlacement.Hidden;

            await OpenBackgroundAsync();
            var backgroundResumedReady = await WaitUntilAsync(
                () =>
                {
                    var state = mainWindow.GetPluginRuntimeQualityState();
                    return backgroundEntry.State == PluginState.Running
                        && state.IsVisible
                        && !state.IsDetached
                        && state.SessionId == backgroundInitial.SessionId
                        && state.ContentIdentity
                            == backgroundInitial.ContentIdentity;
                },
                15_000);
            var backgroundResumed =
                mainWindow.GetPluginRuntimeQualityState();
            var backgroundEndRequested =
                await mainWindow.EndPluginRuntimeForQualityAsync();
            var backgroundEndStopped = await WaitUntilAsync(
                () => backgroundEntry.State == PluginState.Stopped,
                10_000);
            var backgroundSessionEnded =
                backgroundInitial.SessionId is not null
                && ServicesInitializer.PluginSessions.GetBySessionId(
                    backgroundInitial.SessionId) is null;

            var settingsOpenError =
                await mainWindow.OpenWorkspaceModuleForQualityAsync(
                    "settings:root");
            var fallbackModule =
                mainWindow.GetActiveWorkspaceModuleKeyForQuality();

            async Task OpenCurrentPluginAsync()
            {
                var currentEntry = registry.Get(pluginId)
                    ?? throw new InvalidOperationException(
                        $"Quality runtime plugin was not registered: {pluginId}");
                if (currentEntry.State is not
                    (PluginState.Running or PluginState.Background)
                    && !await registry.StartPluginAsync(
                        pluginId,
                        persistAutoStart: false))
                {
                    throw new InvalidOperationException(
                        "Quality runtime plugin could not start.");
                }
                if (currentEntry.Instance is not IHasMainUI mainUi)
                {
                    throw new InvalidOperationException(
                        "Quality runtime plugin does not expose a main UI.");
                }
                mainUi.ShowMainUI();
            }

            await OpenCurrentPluginAsync();
            var upgradeEmbeddedReady = await WaitUntilAsync(
                () =>
                {
                    var state = mainWindow.GetPluginRuntimeQualityState();
                    return state.IsVisible
                        && !state.IsDetached
                        && state.SessionId is not null;
                },
                15_000);
            var upgradeInitial =
                mainWindow.GetPluginRuntimeQualityState();
            var upgradePreviousRevision =
                registry.Get(pluginId)?.RegistrationRevision ?? 0;
            var pluginDirectory = registry.Get(pluginId)?.Directory
                ?? throw new InvalidOperationException(
                    "Quality runtime plugin directory is unavailable.");
            var app = _application as App
                ?? throw new InvalidOperationException(
                    "Quality runtime probe requires the Long application.");

            await app.ReloadPluginDirectoryForQualityAsync(pluginDirectory);
            var upgradeCleaned = await WaitUntilAsync(
                () =>
                {
                    var currentEntry = registry.Get(pluginId);
                    return currentEntry is not null
                        && currentEntry.RegistrationRevision
                            != upgradePreviousRevision
                        && upgradeInitial.SessionId is not null
                        && ServicesInitializer.PluginSessions.GetBySessionId(
                            upgradeInitial.SessionId) is null
                        && !mainWindow.HasPluginRuntimeModuleForQuality(
                            upgradeInitial.SessionId)
                        && mainWindow.GetActiveWorkspaceModuleKeyForQuality()
                            == fallbackModule;
                },
                15_000);
            var upgradeRegistrationRestored =
                registry.Get(pluginId) is not null
                && registry.Get(pluginId)!.RegistrationRevision
                    != upgradePreviousRevision;
            var upgradeSessionEnded =
                upgradeInitial.SessionId is not null
                && ServicesInitializer.PluginSessions.GetBySessionId(
                    upgradeInitial.SessionId) is null;
            var upgradeModuleRemoved =
                upgradeInitial.SessionId is not null
                && !mainWindow.HasPluginRuntimeModuleForQuality(
                    upgradeInitial.SessionId);
            var upgradeMruRestored =
                mainWindow.GetActiveWorkspaceModuleKeyForQuality()
                == fallbackModule;

            await OpenCurrentPluginAsync();
            var upgradeReopenedReady = await WaitUntilAsync(
                () =>
                {
                    var state = mainWindow.GetPluginRuntimeQualityState();
                    return state.IsVisible
                        && !state.IsDetached
                        && state.SessionId is not null
                        && state.SessionId != upgradeInitial.SessionId;
                },
                15_000);
            var uninstallInitial =
                mainWindow.GetPluginRuntimeQualityState();
            var uninstallRequested =
                await app.UnloadPluginForQualityAsync(pluginId);
            var uninstallCleaned = await WaitUntilAsync(
                () =>
                    registry.Get(pluginId) is null
                    && uninstallInitial.SessionId is not null
                    && ServicesInitializer.PluginSessions.GetBySessionId(
                        uninstallInitial.SessionId) is null
                    && !mainWindow.HasPluginRuntimeModuleForQuality(
                        uninstallInitial.SessionId)
                    && mainWindow.GetActiveWorkspaceModuleKeyForQuality()
                        == fallbackModule,
                15_000);
            var uninstallRegistryRemoved = registry.Get(pluginId) is null;
            var uninstallSessionEnded =
                uninstallInitial.SessionId is not null
                && ServicesInitializer.PluginSessions.GetBySessionId(
                    uninstallInitial.SessionId) is null;
            var uninstallModuleRemoved =
                uninstallInitial.SessionId is not null
                && !mainWindow.HasPluginRuntimeModuleForQuality(
                    uninstallInitial.SessionId);
            var uninstallMruRestored =
                mainWindow.GetActiveWorkspaceModuleKeyForQuality()
                == fallbackModule;

            const string nativeDetachedPluginId = "com.long.quicklaunch";
            var nativeDetachedEntry = registry.Get(nativeDetachedPluginId)
                ?? throw new InvalidOperationException(
                    $"Quality native plugin was not found: {nativeDetachedPluginId}");
            var nativeSurfaceOwnedByPlugin =
                PluginWorkspacePresentationPolicy.Resolve(
                    nativeDetachedEntry.Manifest)
                == PluginSurfaceOwnership.PluginOwned;
            if (nativeDetachedEntry.State is not
                (PluginState.Running or PluginState.Background)
                && !await registry.StartPluginAsync(
                    nativeDetachedPluginId,
                    persistAutoStart: false))
            {
                throw new InvalidOperationException(
                    "Quality native plugin could not start.");
            }
            if (nativeDetachedEntry.Instance is not IHasMainUI nativeMainUi)
            {
                throw new InvalidOperationException(
                    "Quality native plugin does not expose a main UI.");
            }
            nativeMainUi.ShowMainUI();
            var nativeWindowOpened = await WaitUntilAsync(
                () => _application.Windows.Cast<Window>().Any(window =>
                    window.IsVisible
                    && string.Equals(
                        window.GetType().Assembly.GetName().Name,
                        "QuickLaunchPlugin",
                        StringComparison.OrdinalIgnoreCase)),
                10_000);
            var nativeSessionAbsent =
                ServicesInitializer.PluginSessions.GetByPluginId(
                    nativeDetachedPluginId) is null;
            var nativeModuleAbsent =
                !mainWindow.HasPluginRuntimeModuleForPluginForQuality(
                    nativeDetachedPluginId);
            var nativePreservedWorkspace =
                mainWindow.GetActiveWorkspaceModuleKeyForQuality()
                == fallbackModule;
            var nativeStopRequested = await registry.StopPluginAsync(
                nativeDetachedPluginId,
                persistAutoStart: false);
            var nativeStoppedCleanly = await WaitUntilAsync(
                () =>
                    nativeDetachedEntry.State == PluginState.Stopped
                    && !_application.Windows.Cast<Window>().Any(window =>
                        window.IsVisible
                        && string.Equals(
                            window.GetType().Assembly.GetName().Name,
                            "QuickLaunchPlugin",
                            StringComparison.OrdinalIgnoreCase))
                    && ServicesInitializer.PluginSessions.GetByPluginId(
                        nativeDetachedPluginId) is null
                    && !mainWindow.HasPluginRuntimeModuleForPluginForQuality(
                        nativeDetachedPluginId),
                10_000);

            var sameSessionAcrossMove =
                initial.SessionId is not null
                && initial.SessionId == detached.SessionId
                && initial.SessionId == returned.SessionId;
            var sameViewAcrossMove =
                initial.ContentIdentity != 0
                && initial.ContentIdentity == detached.ContentIdentity
                && initial.ContentIdentity == returned.ContentIdentity;
            var physicalInputPreserved =
                inputProbe?.InputReceived == true
                && detachedDom.Input == PluginRuntimeInputProbe.InputValue
                && returnedDom.Input == PluginRuntimeInputProbe.InputValue;
            var physicalScrollPreserved =
                inputProbe?.PageDownDispatched == true
                && inputProbe.ScrollBeforeDetach > 0
                && detachedDom.Scroll > 0
                && returnedDom.Scroll > 0
                && Math.Abs(detachedDom.Scroll - returnedDom.Scroll) < 1;
            var passed = embeddedReady
                && webViewReady
                && inputProbe?.ActiveElement == "input"
                && inputProbe.InputDispatched
                && physicalInputPreserved
                && physicalScrollPreserved
                && detachRequested
                && detachedReady
                && returnedReady
                && closeRequested
                && closeStopped
                && firstSessionEnded
                && reopenedReady
                && endRequested
                && endStopped
                && secondSessionEnded
                && sameSessionAcrossMove
                && sameViewAcrossMove
                && backgroundEmbeddedReady
                && backgroundCloseRequested
                && backgroundStateReady
                && backgroundSessionHidden
                && backgroundResumedReady
                && backgroundEndRequested
                && backgroundEndStopped
                && backgroundSessionEnded
                && settingsOpenError is null
                && upgradeEmbeddedReady
                && upgradeCleaned
                && upgradeRegistrationRestored
                && upgradeSessionEnded
                && upgradeModuleRemoved
                && upgradeMruRestored
                && upgradeReopenedReady
                && uninstallRequested
                && uninstallCleaned
                && uninstallRegistryRemoved
                && uninstallSessionEnded
                && uninstallModuleRemoved
                && uninstallMruRestored
                && nativeSurfaceOwnedByPlugin
                && nativeWindowOpened
                && nativeSessionAbsent
                && nativeModuleAbsent
                && nativePreservedWorkspace
                && nativeStopRequested
                && nativeStoppedCleanly;

            var fullPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(
                fullPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schema_version = 1,
                        captured_at = DateTimeOffset.UtcNow,
                        plugin_id = pluginId,
                        passed,
                        embedded_ready = embeddedReady,
                        detach_requested = detachRequested,
                        detached_ready = detachedReady,
                        returned_ready = returnedReady,
                        same_session_across_move = sameSessionAcrossMove,
                        same_view_across_move = sameViewAcrossMove,
                        foreground_requested =
                            inputProbe?.ForegroundRequested == true,
                        active_element_before_input =
                            inputProbe?.ActiveElement ?? string.Empty,
                        webview_input_dispatched =
                            inputProbe?.InputDispatched == true,
                        webview_input_received =
                            inputProbe?.InputReceived == true,
                        webview_input_observed =
                            inputProbe?.InputObserved ?? string.Empty,
                        webview_input_preserved =
                            physicalInputPreserved,
                        webview_page_down_dispatched =
                            inputProbe?.PageDownDispatched == true,
                        webview_scroll_preserved =
                            physicalScrollPreserved,
                        physical_ctrl_d_sent = detachRequested,
                        scroll_before_detach =
                            inputProbe?.ScrollBeforeDetach ?? 0,
                        scroll_after_detach = detachedDom.Scroll,
                        scroll_after_return = returnedDom.Scroll,
                        close_requested = closeRequested,
                        close_stopped = closeStopped,
                        first_session_ended = firstSessionEnded,
                        reopened_ready = reopenedReady,
                        new_session_after_close =
                            reopened.SessionId is not null
                            && reopened.SessionId != initial.SessionId,
                        end_requested = endRequested,
                        end_stopped = endStopped,
                        second_session_ended = secondSessionEnded,
                        first_session_id = initial.SessionId,
                        second_session_id = reopened.SessionId,
                        content_identity = initial.ContentIdentity,
                        background_plugin_id = backgroundPluginId,
                        background_workspace_runtime_ready =
                            backgroundEmbeddedReady,
                        background_close_requested =
                            backgroundCloseRequested,
                        background_state_ready = backgroundStateReady,
                        background_session_hidden =
                            backgroundSessionHidden,
                        background_resumed_ready =
                            backgroundResumedReady,
                        background_same_session_after_resume =
                            backgroundInitial.SessionId is not null
                            && backgroundInitial.SessionId
                                == backgroundResumed.SessionId,
                        background_same_view_after_resume =
                            backgroundInitial.ContentIdentity != 0
                            && backgroundInitial.ContentIdentity
                                == backgroundResumed.ContentIdentity,
                        background_end_requested =
                            backgroundEndRequested,
                        background_end_stopped = backgroundEndStopped,
                        background_session_ended =
                            backgroundSessionEnded,
                        fallback_module = fallbackModule.ToString(),
                        settings_open_error = settingsOpenError,
                        upgrade_workspace_runtime_ready = upgradeEmbeddedReady,
                        upgrade_cleaned = upgradeCleaned,
                        upgrade_registration_restored =
                            upgradeRegistrationRestored,
                        upgrade_session_ended = upgradeSessionEnded,
                        upgrade_module_removed = upgradeModuleRemoved,
                        upgrade_mru_restored = upgradeMruRestored,
                        upgrade_reopened_ready = upgradeReopenedReady,
                        upgrade_old_session_id = upgradeInitial.SessionId,
                        uninstall_requested = uninstallRequested,
                        uninstall_cleaned = uninstallCleaned,
                        uninstall_registry_removed =
                            uninstallRegistryRemoved,
                        uninstall_session_ended = uninstallSessionEnded,
                        uninstall_module_removed = uninstallModuleRemoved,
                        uninstall_mru_restored = uninstallMruRestored,
                        uninstall_session_id = uninstallInitial.SessionId,
                        native_detached_plugin_id =
                            nativeDetachedPluginId,
                        native_surface_owned_by_plugin =
                            nativeSurfaceOwnedByPlugin,
                        native_window_opened = nativeWindowOpened,
                        native_session_absent = nativeSessionAbsent,
                        native_module_absent = nativeModuleAbsent,
                        native_preserved_workspace =
                            nativePreservedWorkspace,
                        native_stop_requested = nativeStopRequested,
                        native_stopped_cleanly = nativeStoppedCleanly,
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
            _application.Shutdown(passed ? 0 : 3);
        }

        public async Task OpenPluginRuntimeAsync(
            MainWindow mainWindow,
            string pluginId)
        {
            ArgumentNullException.ThrowIfNull(mainWindow);
            var result = await PluginMainUiLauncher.OpenAsync(
                HostProvider.Instance.PluginStore,
                pluginId);
            if (result != PluginMainUiOpenStatus.Opened)
                throw new InvalidOperationException(
                    $"Quality runtime plugin could not open: {pluginId} ({result})");
        }

        public async Task RunUiServiceThemeProbeAsync(string reportPath)
        {
            var originalTheme = App.IsLightTheme;
            var highContrast = App.IsHighContrastEnabled;
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
                App.ApplyTheme(isLight: false);
                await _application.Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Render);
                dialog.UpdateLayout();
                var dark = CaptureDialogTheme(dialog, "dark");
                await CaptureWindowAsync(dialog, darkScreenshotPath);

                App.ApplyTheme(isLight: true);
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
                var themeBehaviorPassed = highContrast ? !changed : changed;
                var passed = themeBehaviorPassed && dark.Passed && light.Passed;
                await File.WriteAllTextAsync(
                    fullPath,
                    JsonSerializer.Serialize(
                        new
                        {
                            schema_version = 1,
                            captured_at = DateTimeOffset.UtcNow,
                            passed,
                            high_contrast = highContrast,
                            runtime_theme_changed = changed,
                            system_palette_preserved = highContrast && !changed,
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
                App.ApplyTheme(originalTheme);
            }
        }

        public async Task RunThemedMessageDialogProbeAsync(string reportPath)
        {
            var originalTheme = App.IsLightTheme;
            var highContrast = App.IsHighContrastEnabled;
            var dialog = ThemedMessageDialog.CreateForQuality();
            var fullPath = Path.GetFullPath(reportPath);
            var reportDirectory = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(reportDirectory);
            var darkScreenshotPath = Path.Combine(
                reportDirectory,
                "themed-message-dialog-dark.png");
            var lightScreenshotPath = Path.Combine(
                reportDirectory,
                "themed-message-dialog-light.png");
            try
            {
                dialog.Show();
                App.ApplyTheme(isLight: false);
                await _application.Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Render);
                dialog.UpdateLayout();
                var dark = CaptureThemedMessageDialogTheme(dialog, "dark");
                var compactLongTextLayoutPassed =
                    VerifyThemedMessageDialogLayout(dialog);
                await CaptureWindowAsync(dialog, darkScreenshotPath);

                App.ApplyTheme(isLight: true);
                await _application.Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Render);
                dialog.UpdateLayout();
                var light = CaptureThemedMessageDialogTheme(dialog, "light");
                await CaptureWindowAsync(dialog, lightScreenshotPath);

                var changed =
                    dark.Surface != light.Surface
                    && dark.SecondaryButtonBackground
                        != light.SecondaryButtonBackground;
                var themeBehaviorPassed = highContrast ? !changed : changed;
                var passed = themeBehaviorPassed
                    && compactLongTextLayoutPassed
                    && dark.Passed
                    && light.Passed;
                await File.WriteAllTextAsync(
                    fullPath,
                    JsonSerializer.Serialize(
                        new
                        {
                            schema_version = 1,
                            captured_at = DateTimeOffset.UtcNow,
                            passed,
                            high_contrast = highContrast,
                            runtime_theme_changed = changed,
                            system_palette_preserved = highContrast && !changed,
                            default_action = "cancel",
                            compact_long_text_layout_passed =
                                compactLongTextLayoutPassed,
                            window = new
                            {
                                width = Math.Round(dialog.ActualWidth, 1),
                                height = Math.Round(dialog.ActualHeight, 1),
                            },
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
                _application.Shutdown(passed ? 0 : 7);
            }
            finally
            {
                dialog.Close();
                App.ApplyTheme(originalTheme);
            }
        }

        public async Task RunPluginSettingsProbeAsync(string reportPath)
        {
            var highContrast = App.IsHighContrastEnabled;
            var expectedControls = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["com.long.color-picker"] = 1,
                ["com.long.folder-note"] = 1,
                ["com.long.macro"] = 3,
                ["com.long.screenshot"] = 2,
                ["com.long.window-manager"] = 1,
            };
            var originalTheme = App.IsLightTheme;
            var fullPath = Path.GetFullPath(reportPath);
            var reportDirectory = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(reportDirectory);
            var entries = HostProvider.Instance.PluginStore.GetAll()
                .ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
            var results = new List<PluginSettingsProbeResult>();
            try
            {
                foreach (var expected in expectedControls)
                {
                    if (!entries.TryGetValue(expected.Key, out var entry)
                        || entry.Instance is not IHasSettingsUI settingsUi)
                    {
                        results.Add(new PluginSettingsProbeResult(
                            expected.Key,
                            expected.Value,
                            0,
                            false,
                            false,
                            null,
                            null,
                            null,
                            null));
                        continue;
                    }

                    var window = PluginManagementControl.CreateSettingsWindow(
                        entry,
                        settingsUi,
                        owner: null);
                    var fileStem = expected.Key.Replace('.', '-');
                    var darkScreenshotPath = Path.Combine(
                        reportDirectory,
                        $"{fileStem}-dark.png");
                    var lightScreenshotPath = Path.Combine(
                        reportDirectory,
                        $"{fileStem}-light.png");
                    try
                    {
                        window.WindowStartupLocation =
                            WindowStartupLocation.CenterScreen;
                        window.Show();
                        App.ApplyTheme(isLight: false);
                        await _application.Dispatcher.InvokeAsync(
                            () => { },
                            DispatcherPriority.Render);
                        window.UpdateLayout();
                        var darkAccessible =
                            await VerifySettingsControlsAccessibleAsync(window);
                        var dark = CapturePluginSettingsTheme(
                            window,
                            "dark",
                            expected.Value,
                            darkAccessible);
                        await CaptureWindowAsync(window, darkScreenshotPath);

                        App.ApplyTheme(isLight: true);
                        await _application.Dispatcher.InvokeAsync(
                            () => { },
                            DispatcherPriority.Render);
                        window.UpdateLayout();
                        var lightAccessible =
                            await VerifySettingsControlsAccessibleAsync(window);
                        var light = CapturePluginSettingsTheme(
                            window,
                            "light",
                            expected.Value,
                            lightAccessible);
                        await CaptureWindowAsync(window, lightScreenshotPath);

                        var changed = dark.Surface != light.Surface
                            && dark.InputBackground != light.InputBackground;
                        results.Add(new PluginSettingsProbeResult(
                            expected.Key,
                            expected.Value,
                            dark.ControlCount,
                            (highContrast ? !changed : changed)
                                && dark.Passed
                                && light.Passed,
                            changed,
                            Path.GetFileName(darkScreenshotPath),
                            GetFileSha256(darkScreenshotPath),
                            Path.GetFileName(lightScreenshotPath),
                            GetFileSha256(lightScreenshotPath))
                        {
                            Themes = [dark, light],
                        });
                    }
                    finally
                    {
                        window.DetachContent();
                        window.Close();
                    }
                }

                var registeredHotkeys = ServicesInitializer.HotKey
                    .GetAllHotkeys();
                var samePluginPair = registeredHotkeys
                    .GroupBy(
                        pair => pair.Value,
                        StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(group => group.Count() >= 2)?
                    .Take(2)
                    .Select(pair => pair.Key)
                    .ToArray();
                var currentHotkey = samePluginPair?.ElementAtOrDefault(0);
                var requestedHotkey = samePluginPair?.ElementAtOrDefault(1);
                var samePluginConflict = currentHotkey is not null
                    && requestedHotkey is not null
                    ? await ServicesInitializer.HotKey.IsConflictAsync(
                        requestedHotkey,
                        currentHotkey)
                    : HostApiResponse<bool>.Failure(
                        ApiErrorCode.NotFound,
                        "No plugin registered two hotkeys for the quality probe.");
                var conflictPassed = samePluginConflict.IsSuccess
                    && samePluginConflict.Data;
                var passed = results.Count == expectedControls.Count
                    && results.All(result => result.Passed)
                    && conflictPassed;
                await File.WriteAllTextAsync(
                    fullPath,
                    JsonSerializer.Serialize(
                        new
                        {
                            schema_version = 1,
                            captured_at = DateTimeOffset.UtcNow,
                            passed,
                            high_contrast = highContrast,
                            system_palette_preserved = highContrast
                                && results.All(result =>
                                    !result.RuntimeThemeChanged),
                            same_plugin_conflict = new
                            {
                                current_hotkey = currentHotkey,
                                requested_hotkey = requestedHotkey,
                                detected = samePluginConflict.Data,
                                passed = conflictPassed,
                            },
                            plugins = results,
                        },
                        new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                        }));
                _application.Shutdown(passed ? 0 : 8);
            }
            finally
            {
                App.ApplyTheme(originalTheme);
            }
        }

        public async Task RunPluginSettingsPersistenceProbeAsync(
            string reportPath)
        {
            var settings = new Dictionary<string, SettingProbeDefinition[]>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["com.long.color-picker"] =
                    [new("hotkey", "Ctrl+Shift+P")],
                ["com.long.folder-note"] =
                    [new("hotkey", "Alt+M")],
                ["com.long.macro"] =
                [
                    new("record_hotkey", "F6"),
                    new("play_once_hotkey", "F7"),
                    new("play_loop_hotkey", "F8"),
                ],
                ["com.long.screenshot"] =
                [
                    new("full_hotkey", "Ctrl+Shift+S"),
                    new("region_hotkey", "Ctrl+Shift+A"),
                ],
                ["com.long.window-manager"] =
                    [new("topmost_hotkey", "Ctrl+Alt+T")],
            };
            var candidates = Enumerable.Range(1, 12)
                .Select(number => $"Ctrl+Alt+Shift+F{number}")
                .ToArray();
            var candidateIndex = 0;
            var entries = HostProvider.Instance.PluginStore.GetAll()
                .ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
            var results = new List<SettingPersistenceProbeResult>();

            foreach (var plugin in settings)
            {
                if (!entries.TryGetValue(plugin.Key, out var entry)
                    || entry.Instance is not IHasSettingsUI settingsUi)
                {
                    foreach (var definition in plugin.Value)
                    {
                        results.Add(new SettingPersistenceProbeResult(
                            plugin.Key,
                            definition.Key,
                            null,
                            null,
                            false,
                            false,
                            false,
                            false,
                            false,
                            "Plugin settings UI is unavailable."));
                    }
                    continue;
                }

                var window = PluginManagementControl.CreateSettingsWindow(
                    entry,
                    settingsUi,
                    owner: null);
                try
                {
                    window.WindowStartupLocation =
                        WindowStartupLocation.CenterScreen;
                    window.Show();
                    await _application.Dispatcher.InvokeAsync(
                        () => { },
                        DispatcherPriority.Render);
                    window.UpdateLayout();
                    var inputs = FindVisualChildren<System.Windows.Controls.TextBox>(
                            window)
                        .Where(input =>
                            System.Windows.Automation.AutomationProperties
                                .GetAutomationId(input)
                                == "Long.HotkeySettings.Input")
                        .ToArray();
                    var buttons = FindVisualChildren<System.Windows.Controls.Button>(
                            window)
                        .Where(button =>
                            System.Windows.Automation.AutomationProperties
                                .GetAutomationId(button)
                                == "Long.HotkeySettings.Apply")
                        .ToArray();
                    if (inputs.Length != plugin.Value.Length
                        || buttons.Length != plugin.Value.Length)
                    {
                        foreach (var definition in plugin.Value)
                        {
                            results.Add(new SettingPersistenceProbeResult(
                                plugin.Key,
                                definition.Key,
                                null,
                                null,
                                false,
                                false,
                                false,
                                false,
                                false,
                                "Settings control count does not match the contract."));
                        }
                        continue;
                    }

                    for (var index = 0; index < plugin.Value.Length; index++)
                    {
                        var definition = plugin.Value[index];
                        var originalConfigured =
                            entry.GetSetting(definition.Key)
                            ?? definition.DefaultValue;
                        var originalDisplayed = inputs[index].Text;
                        var originalWasRegistered = string.Equals(
                            ServicesInitializer.HotKey.GetOwner(originalDisplayed),
                            plugin.Key,
                            StringComparison.OrdinalIgnoreCase);
                        string? appliedCandidate = null;
                        for (var attempt = 0;
                             attempt < candidates.Length;
                             attempt++)
                        {
                            var candidate = candidates[
                                candidateIndex++ % candidates.Length];
                            var conflict = await ServicesInitializer.HotKey
                                .IsConflictAsync(candidate);
                            if (!conflict.IsSuccess || conflict.Data)
                                continue;

                            inputs[index].Text = candidate;
                            buttons[index].RaiseEvent(new RoutedEventArgs(
                                System.Windows.Controls.Button.ClickEvent));
                            if (await WaitUntilAsync(
                                    () => string.Equals(
                                        entry.GetSetting(definition.Key),
                                        candidate,
                                        StringComparison.OrdinalIgnoreCase),
                                    2_000))
                            {
                                appliedCandidate = candidate;
                                break;
                            }
                        }

                        var registered = appliedCandidate is not null
                            && string.Equals(
                                ServicesInitializer.HotKey.GetOwner(
                                    appliedCandidate),
                                plugin.Key,
                                StringComparison.OrdinalIgnoreCase);
                        var configPath = Path.Combine(
                            entry.Directory,
                            "config.json");
                        var persisted = appliedCandidate is not null
                            && ConfigSettingEquals(
                                configPath,
                                definition.Key,
                                appliedCandidate);
                        var reloaded = appliedCandidate is not null
                            && string.Equals(
                                new PluginEntry(
                                    entry.Manifest,
                                    new object(),
                                    entry.Directory,
                                    registrationRevision: 0)
                                .GetSetting(definition.Key),
                                appliedCandidate,
                                StringComparison.OrdinalIgnoreCase);

                        var restored = false;
                        if (appliedCandidate is not null)
                        {
                            if (originalWasRegistered)
                            {
                                inputs[index].Text = originalDisplayed;
                                buttons[index].RaiseEvent(new RoutedEventArgs(
                                    System.Windows.Controls.Button.ClickEvent));
                                var runtimeRestored = await WaitUntilAsync(
                                    () => string.Equals(
                                            entry.GetSetting(definition.Key),
                                            originalDisplayed,
                                            StringComparison.OrdinalIgnoreCase)
                                        && string.Equals(
                                            ServicesInitializer.HotKey.GetOwner(
                                                originalDisplayed),
                                            plugin.Key,
                                            StringComparison.OrdinalIgnoreCase),
                                    2_000);
                                var configRestored = entry.SetSetting(
                                    definition.Key,
                                    originalConfigured);
                                restored = runtimeRestored
                                    && configRestored.IsSuccess;
                            }
                            else
                            {
                                var unregistered =
                                    await ServicesInitializer.HotKey
                                        .UnregisterAsync(appliedCandidate);
                                var reset = entry.SetSetting(
                                    definition.Key,
                                    originalConfigured);
                                restored = unregistered.IsSuccess
                                    && reset.IsSuccess;
                            }
                        }

                        results.Add(new SettingPersistenceProbeResult(
                            plugin.Key,
                            definition.Key,
                            originalDisplayed,
                            appliedCandidate,
                            appliedCandidate is not null,
                            registered,
                            persisted,
                            reloaded,
                            restored,
                            appliedCandidate is null
                                ? "No candidate hotkey could be applied."
                                : null));
                    }
                }
                finally
                {
                    window.DetachContent();
                    window.Close();
                }
            }

            var unexpectedPersistedKeys = settings
                .SelectMany(plugin =>
                {
                    if (!entries.TryGetValue(plugin.Key, out var entry))
                        return [];
                    var configPath = Path.Combine(
                        entry.Directory,
                        "config.json");
                    if (!File.Exists(configPath))
                        return [];
                    using var document = JsonDocument.Parse(
                        File.ReadAllText(configPath));
                    var allowed = plugin.Value
                        .Select(definition => definition.Key)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    return document.RootElement
                        .EnumerateObject()
                        .Where(property => !allowed.Contains(property.Name))
                        .Select(property =>
                            $"{plugin.Key}:{property.Name}")
                        .ToArray();
                })
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var passed = results.Count
                    == settings.Values.Sum(definitions => definitions.Length)
                && results.All(result => result.Passed)
                && unexpectedPersistedKeys.Length == 0;
            var fullPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(
                fullPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schema_version = 1,
                        captured_at = DateTimeOffset.UtcNow,
                        isolated_plugins_directory_required = true,
                        passed,
                        setting_count = results.Count,
                        unexpected_persisted_keys = unexpectedPersistedKeys,
                        settings = results,
                    },
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    }));
            _application.Shutdown(passed ? 0 : 9);
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
            var webView = await WaitForWebViewReadyAsync(target, TimeSpan.FromSeconds(10));
            await Task.Delay(Math.Max(250, options.QualityCaptureDelayMilliseconds));
            await webView.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

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

        private static async Task<WebView2> WaitForWebViewReadyAsync(
            DependencyObject target,
            TimeSpan timeout)
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            WebView2? webView = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                webView = FindVisualChild<WebView2>(target);
                if (webView?.CoreWebView2 != null)
                {
                    try
                    {
                        var state = await webView.CoreWebView2.ExecuteScriptAsync(
                            "document.readyState");
                        if (state is "\"interactive\"" or "\"complete\"")
                            return webView;
                    }
                    catch (InvalidOperationException)
                    {
                        // WebView2 can briefly reject script execution while navigation starts.
                    }
                }

                await Task.Delay(100);
            }

            throw new InvalidDataException(
                webView == null
                    ? "Quality capture could not find an active WebView2."
                    : "WebView2 did not become ready for quality capture before timeout.");
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

        private static ThemedMessageDialogSnapshot CaptureThemedMessageDialogTheme(
            ThemedMessageDialog dialog,
            string theme)
        {
            var snapshot = dialog.GetThemeSnapshot();
            var titleContrast = ContrastRatio(
                snapshot.Title,
                snapshot.Surface);
            var messageContrast = ContrastRatio(
                snapshot.Message,
                snapshot.Surface);
            var primaryContrast = ContrastRatio(
                snapshot.PrimaryForeground,
                snapshot.PrimaryBackground);
            var secondaryContrast = ContrastRatio(
                snapshot.SecondaryForeground,
                snapshot.SecondaryBackground);

            return new ThemedMessageDialogSnapshot(
                theme,
                ToHex(snapshot.Surface),
                ToHex(snapshot.Title),
                ToHex(snapshot.Message),
                ToHex(snapshot.PrimaryBackground),
                ToHex(snapshot.PrimaryForeground),
                ToHex(snapshot.SecondaryBackground),
                ToHex(snapshot.SecondaryForeground),
                Math.Round(titleContrast, 2),
                Math.Round(messageContrast, 2),
                Math.Round(primaryContrast, 2),
                Math.Round(secondaryContrast, 2),
                titleContrast >= 4.5
                    && messageContrast >= 4.5
                    && primaryContrast >= 4.5
                    && secondaryContrast >= 4.5);
        }

        private static PluginSettingsThemeSnapshot CapturePluginSettingsTheme(
            PluginWindowHost window,
            string theme,
            int expectedControlCount,
            bool allControlsAccessible)
        {
            var controls = FindVisualChildren<HotkeySettingsControl>(window)
                .ToArray();
            var inputs = FindVisualChildren<System.Windows.Controls.TextBox>(window)
                .Where(input =>
                    System.Windows.Automation.AutomationProperties
                        .GetAutomationId(input)
                        == "Long.HotkeySettings.Input")
                .ToArray();
            var buttons = FindVisualChildren<System.Windows.Controls.Button>(window)
                .Where(button =>
                    System.Windows.Automation.AutomationProperties
                        .GetAutomationId(button)
                        == "Long.HotkeySettings.Apply")
                .ToArray();
            var surface = GetColor((Brush)window.FindResource(
                "Long.Brush.Background.Base"));
            var textForeground = GetColor((Brush)window.FindResource(
                "Long.Brush.Text.Primary"));
            var inputBackground = inputs.Length > 0
                ? GetColor(inputs[0].Background)
                : Colors.Transparent;
            var inputForeground = inputs.Length > 0
                ? GetColor(inputs[0].Foreground)
                : Colors.Transparent;
            var buttonBackground = buttons.Length > 0
                ? GetColor(buttons[0].Background)
                : Colors.Transparent;
            var buttonForeground = buttons.Length > 0
                ? GetColor(buttons[0].Foreground)
                : Colors.Transparent;
            var textContrast = ContrastRatio(textForeground, surface);
            var inputContrast = ContrastRatio(
                inputForeground,
                inputBackground);
            var buttonContrast = ContrastRatio(
                buttonForeground,
                buttonBackground);
            var settingsScroll = FindVisualChildren<ScrollViewer>(window)
                .Single(scroll =>
                    System.Windows.Automation.AutomationProperties
                        .GetAutomationId(scroll)
                        == "Long.Plugin.Settings.Scroll");
            var countsMatch = controls.Length == expectedControlCount
                && inputs.Length == expectedControlCount
                && buttons.Length == expectedControlCount;

            return new PluginSettingsThemeSnapshot(
                theme,
                controls.Length,
                inputs.Length,
                buttons.Length,
                Math.Round(window.ActualWidth, 1),
                Math.Round(window.ActualHeight, 1),
                settingsScroll.ScrollableHeight > 0,
                allControlsAccessible,
                ToHex(surface),
                ToHex(textForeground),
                ToHex(inputBackground),
                ToHex(inputForeground),
                ToHex(buttonBackground),
                ToHex(buttonForeground),
                Math.Round(textContrast, 2),
                Math.Round(inputContrast, 2),
                Math.Round(buttonContrast, 2),
                countsMatch
                    && allControlsAccessible
                    && textContrast >= 4.5
                    && inputContrast >= 4.5
                    && buttonContrast >= 4.5);
        }

        private static bool VerifyThemedMessageDialogLayout(
            ThemedMessageDialog dialog)
        {
            var actionButtons = FindVisualChildren<System.Windows.Controls.Button>(
                    dialog)
                .Where(button =>
                {
                    var automationId =
                        System.Windows.Automation.AutomationProperties
                            .GetAutomationId(button);
                    return automationId is "Long.MessageDialog.Cancel"
                        or "Long.MessageDialog.Primary";
                })
                .ToArray();
            var message = FindVisualChildren<TextBlock>(dialog)
                .FirstOrDefault(text =>
                    System.Windows.Automation.AutomationProperties
                        .GetLiveSetting(text)
                    == System.Windows.Automation.AutomationLiveSetting.Assertive);
            return dialog.ActualWidth <= 380.5
                && dialog.ActualHeight <= dialog.MaxHeight
                && actionButtons.Length == 2
                && actionButtons.All(button => IsFullyVisible(button, dialog))
                && message is not null
                && IsFullyVisible(message, dialog);
        }

        private async Task<bool> VerifySettingsControlsAccessibleAsync(
            PluginWindowHost window)
        {
            var scroll = FindVisualChildren<ScrollViewer>(window)
                .Single(candidate =>
                    System.Windows.Automation.AutomationProperties
                        .GetAutomationId(candidate)
                        == "Long.Plugin.Settings.Scroll");
            var controls = FindVisualChildren<HotkeySettingsControl>(window)
                .ToArray();
            var inputs = FindVisualChildren<System.Windows.Controls.TextBox>(window)
                .Where(input =>
                    System.Windows.Automation.AutomationProperties
                        .GetAutomationId(input)
                        == "Long.HotkeySettings.Input")
                .ToArray();
            var buttons = FindVisualChildren<System.Windows.Controls.Button>(window)
                .Where(button =>
                    System.Windows.Automation.AutomationProperties
                        .GetAutomationId(button)
                        == "Long.HotkeySettings.Apply")
                .ToArray();
            if (controls.Length == 0
                || inputs.Length != controls.Length
                || buttons.Length != controls.Length)
            {
                return false;
            }

            scroll.ScrollToHome();
            await _application.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.Render);
            window.UpdateLayout();
            var firstVisible = new FrameworkElement[]
            {
                controls[0],
                inputs[0],
                buttons[0],
            }.All(element => IsFullyVisible(element, window));
            var allVisible = controls.Cast<FrameworkElement>()
                .Concat(inputs)
                .Concat(buttons)
                .All(element => IsFullyVisible(element, window));
            if (allVisible)
                return true;
            if (scroll.ScrollableHeight <= 0 || !firstVisible)
                return false;

            scroll.ScrollToEnd();
            await _application.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.Render);
            window.UpdateLayout();
            var lastIndex = controls.Length - 1;
            var lastVisible = new FrameworkElement[]
            {
                controls[lastIndex],
                inputs[lastIndex],
                buttons[lastIndex],
            }.All(element => IsFullyVisible(element, window));
            scroll.ScrollToHome();
            await _application.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.Render);
            window.UpdateLayout();
            return lastVisible;
        }

        private static bool IsFullyVisible(
            FrameworkElement element,
            Window window)
        {
            if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
                return false;
            var origin = element.TransformToAncestor(window)
                .Transform(new Point(0, 0));
            const double tolerance = 0.5;
            return origin.X >= -tolerance
                && origin.Y >= -tolerance
                && origin.X + element.ActualWidth
                    <= window.ActualWidth + tolerance
                && origin.Y + element.ActualHeight
                    <= window.ActualHeight + tolerance;
        }

        private static async Task<bool> WaitUntilAsync(
            Func<bool> condition,
            int timeoutMilliseconds)
        {
            var deadline = Environment.TickCount64 + timeoutMilliseconds;
            while (Environment.TickCount64 < deadline)
            {
                if (condition())
                    return true;
                await Task.Delay(40);
            }
            return condition();
        }

        private static bool ConfigSettingEquals(
            string configPath,
            string key,
            string expected)
        {
            if (!File.Exists(configPath))
                return false;
            using var document = JsonDocument.Parse(
                File.ReadAllText(configPath));
            return document.RootElement.TryGetProperty(key, out var value)
                && value.ValueKind == JsonValueKind.String
                && string.Equals(
                    value.GetString(),
                    expected,
                    StringComparison.OrdinalIgnoreCase);
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

        private sealed record ThemedMessageDialogSnapshot(
            string Theme,
            string Surface,
            string Title,
            string Message,
            string PrimaryButtonBackground,
            string PrimaryButtonForeground,
            string SecondaryButtonBackground,
            string SecondaryButtonForeground,
            double TitleContrast,
            double MessageContrast,
            double PrimaryButtonContrast,
            double SecondaryButtonContrast,
            bool Passed);

        private sealed record PluginSettingsProbeResult(
            string PluginId,
            int ExpectedControlCount,
            int ActualControlCount,
            bool Passed,
            bool RuntimeThemeChanged,
            string? DarkScreenshot,
            string? DarkScreenshotSha256,
            string? LightScreenshot,
            string? LightScreenshotSha256)
        {
            public IReadOnlyList<PluginSettingsThemeSnapshot> Themes { get; init; } =
                [];
        }

        private sealed record PluginSettingsThemeSnapshot(
            string Theme,
            int ControlCount,
            int InputCount,
            int ButtonCount,
            double WindowWidth,
            double WindowHeight,
            bool VerticalScrollAvailable,
            bool AllControlsAccessible,
            string Surface,
            string TextForeground,
            string InputBackground,
            string InputForeground,
            string ButtonBackground,
            string ButtonForeground,
            double TextContrast,
            double InputContrast,
            double ButtonContrast,
            bool Passed);

        private sealed record SettingProbeDefinition(
            string Key,
            string DefaultValue);

        private sealed record SettingPersistenceProbeResult(
            string PluginId,
            string SettingKey,
            string? OriginalDisplayedHotkey,
            string? AppliedHotkey,
            bool Applied,
            bool Registered,
            bool Persisted,
            bool Reloaded,
            bool Restored,
            string? Error)
        {
            public bool Passed => Applied
                && Registered
                && Persisted
                && Reloaded
                && Restored;
        }
    }
}
