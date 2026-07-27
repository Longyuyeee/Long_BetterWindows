using System.IO;
using LongBetterWindows.Host.Views;

namespace LongBetterWindows.Tests;

public sealed class ReleaseBlockingRegressionTests
{
    [Fact]
    public void RuntimeThemeSwitch_ReplacesBrushesAndPreservesAccentContrast()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "App.xaml.cs"));
        var components = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Themes",
            "LongComponents.xaml"));

        Assert.Contains("UpdateThemeBrushResources(r, palette)", app);
        Assert.Contains("[\"Long.Brush.Background.Raised\"]", app);
        Assert.Contains("[\"Long.Brush.Text.Primary\"]", app);
        Assert.Contains("[\"Long.Brush.Accent.Primary\"]", app);
        var buttonStyleStart = components.IndexOf(
            "<Style x:Key=\"LongButton\"",
            StringComparison.Ordinal);
        var buttonStyleEnd = components.IndexOf(
            "</Style>",
            buttonStyleStart,
            StringComparison.Ordinal);
        var buttonStyle = components[buttonStyleStart..buttonStyleEnd];
        Assert.DoesNotContain(
            "TargetName=\"Chrome\" Property=\"Background\" Value=\"{DynamicResource Long.Brush.Surface.Hover}\"",
            buttonStyle);
        Assert.Contains(
            "Property=\"Background\" Value=\"{DynamicResource Long.Brush.Accent.Hover}\"",
            components);
        Assert.Contains(
            "Property=\"Background\" Value=\"{DynamicResource Long.Brush.Accent.Pressed}\"",
            components);
    }

    [Fact]
    public void WebPluginInitialization_WaitsForBridgeAndInitialNavigation()
    {
        var root = FindRepositoryRoot();
        var lifecycle = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Engine",
            "WebPluginViewLifecycle.cs"));

        var bridgeAwait = lifecycle.IndexOf(
            "await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(",
            StringComparison.Ordinal);
        var navigate = lifecycle.IndexOf(
            "webView.CoreWebView2.Navigate(",
            StringComparison.Ordinal);

        Assert.True(bridgeAwait >= 0 && bridgeAwait < navigate);
        Assert.Contains("_navigationCompletion", lifecycle);
        Assert.Contains("navigationCompletion.Task.WaitAsync", lifecycle);
        Assert.Contains(
            "_navigationCompletion?.TrySetResult(args.IsSuccess)",
            lifecycle);
    }

    [Fact]
    public void QuickLaunch_DebouncesAndCancelsRecursiveDiskSearch()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "QuickLaunchPlugin",
            "LaunchWindow.xaml.cs"));

        Assert.Contains("private CancellationTokenSource? _searchCancellation", source);
        Assert.Contains("await Task.Delay(180, cancellationToken)", source);
        Assert.Contains("await Task.Run(", source);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested()", source);
        Assert.Contains("QuickLaunchDiskSearchEngine", source);
        Assert.Contains("_queryGeneration.IsCurrent(generation)", source);
    }

    [Fact]
    public void DetachedPluginWindows_UseStablePerPluginTaskbarIdentity()
    {
        var first = PluginTaskbarIdentity.CreateAppUserModelId(
            "com.long.json-formatter");
        var second = PluginTaskbarIdentity.CreateAppUserModelId(
            "com.long.password-gen");

        Assert.Equal(
            "LongAssistant.Plugin.com.long.json.formatter.6f6cf7597d46",
            first);
        Assert.NotEqual(first, second);
        Assert.Equal(
            PluginTaskbarIdentity.CreateAppUserModelId("PLUGIN-A"),
            PluginTaskbarIdentity.CreateAppUserModelId("plugin-a"));
        Assert.NotEqual(
            PluginTaskbarIdentity.CreateAppUserModelId("plugin-a"),
            PluginTaskbarIdentity.CreateAppUserModelId("plugin_a"));
        Assert.True(
            PluginTaskbarIdentity.CreateAppUserModelId(new string('a', 300))
                .Length <= 128);

        var root = FindRepositoryRoot();
        var presentation = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Engine",
            "WebPluginPresentationCoordinator.cs"));
        Assert.Contains("SetReturnTarget", presentation);
        Assert.DoesNotContain(
            "Owner = System.Windows.Application.Current.MainWindow",
            presentation);

        var identity = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "PluginTaskbarIdentity.cs"));
        Assert.Contains("StableHash(pluginId)", identity);
        Assert.DoesNotContain("GetHashCode(pluginId)", identity);
    }

    [Fact]
    public void PluginUiCapability_UsesRuntimeSemanticThemeResources()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Services",
            "UIService.cs"));

        Assert.Contains("Long.Brush.Background.Base", service);
        Assert.Contains("LongButton.Primary", service);
        Assert.Contains("LongTextBox", service);
        Assert.Contains("App.ThemeChanged += themeChanged", service);
        Assert.Contains("NavigationCompleted", service);
        Assert.Contains("data-long-theme", service);
        Assert.DoesNotContain("Color.FromRgb", service);
        Assert.DoesNotContain("MessageBox.Show", service);
        Assert.False(File.Exists(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Controls",
            "SkeletonCard.cs")));
        Assert.False(File.Exists(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Controls",
            "ToastNotification.cs")));
    }

    [Fact]
    public void WebPluginCommands_ProvideReliableOpenHandlers()
    {
        var root = FindRepositoryRoot();
        var developerToolkit = File.ReadAllText(
            Path.Combine(root, "src", "DevToolkit", "index.html"));
        var portManager = File.ReadAllText(
            Path.Combine(root, "src", "PortManager", "index.html"));

        Assert.Contains("LongUI?.onCommand", developerToolkit);
        Assert.Contains("success: true", developerToolkit);
        Assert.Contains("long.networkPort.findPortOwner", portManager);
        Assert.DoesNotContain("long.networkPort.findOwner", portManager);
        Assert.Contains("return true;", portManager);
        Assert.Contains("find.available", portManager);
    }

    [Fact]
    public void WindowManagerGuide_DoesNotDependOnHostWindowStyleLookup()
    {
        var root = FindRepositoryRoot();
        var guide = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WindowManagerPlugin",
                "WindowManagerGuide.xaml"));

        Assert.DoesNotContain("StaticResource LongDialog", guide);
        Assert.Contains("WindowChrome.WindowChrome", guide);
        Assert.Contains("DynamicResource Long.Brush.Text.Primary", guide);
    }

    [Fact]
    public void ThemeSensitiveViews_UseSemanticColorResources()
    {
        var root = FindRepositoryRoot();
        var developerToolkit = File.ReadAllText(
            Path.Combine(root, "src", "DevToolkit", "index.html"));
        var scriptDialog = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "LongBetterWindows.Host",
                "Views",
                "ScriptCreationDialog.xaml"));

        Assert.DoesNotMatch("#[0-9a-fA-F]{3,8}\\b", developerToolkit);
        Assert.DoesNotMatch("#[0-9a-fA-F]{3,8}\\b", scriptDialog);
        Assert.Contains("var(--long-bg-base)", developerToolkit);
        Assert.Contains("class=\"btn-primary\"", developerToolkit);
        Assert.Contains("class=\"btn-secondary\"", developerToolkit);
        Assert.Contains("background: transparent !important", developerToolkit);
        Assert.Contains("prefers-reduced-motion: reduce", developerToolkit);
        Assert.Contains("DynamicResource Long.Brush.Surface.Card", scriptDialog);
    }

    [Fact]
    public void HostMessages_UseThemedDialogAndDefaultSafeConfirmations()
    {
        var root = FindRepositoryRoot();
        var hostRoot = Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host");
        var productionSources = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    hostRoot,
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)
                    && !path.Contains(
                        $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
        var dialogXaml = File.ReadAllText(Path.Combine(
            hostRoot,
            "Views",
            "ThemedMessageDialog.xaml"));
        var dialogCode = File.ReadAllText(Path.Combine(
            hostRoot,
            "Views",
            "ThemedMessageDialog.xaml.cs"));

        Assert.DoesNotContain("MessageBox.Show", productionSources);
        Assert.Contains("DynamicResource Long.Brush.Surface.Card", dialogXaml);
        Assert.Contains("DynamicResource Long.Brush.Text.Primary", dialogXaml);
        Assert.Contains("Style=\"{StaticResource LongButton.Primary}\"", dialogXaml);
        Assert.Contains("AutomationProperties.AutomationId=\"Long.MessageDialog.Cancel\"", dialogXaml);
        Assert.Contains("CancelButton.Focus()", dialogCode);
        Assert.Contains("ThemedMessageDialogTone.Danger", productionSources);
    }

    [Fact]
    public void NativePluginHotkeySettings_ArePersistedAndRollbackOnFailure()
    {
        var root = FindRepositoryRoot();
        var pluginSources = new[]
        {
            "ColorPickerPlugin",
            "FolderNotePlugin",
            "MacroPlugin",
            "ScreenshotPlugin",
            "WindowManagerPlugin",
        }.Select(directory => File.ReadAllText(Directory
            .EnumerateFiles(
                Path.Combine(root, "src", directory),
                "*PluginImpl.cs")
            .Single()))
            .ToArray();
        var hotkeyControl = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "HotkeySettingsControl.xaml.cs"));

        Assert.All(pluginSources, source =>
        {
            Assert.Contains("host.Settings", source);
            Assert.Contains(".GetAsync(", source);
            Assert.Contains(".SetAsync(", source);
        });
        Assert.Contains("\"record_hotkey\"", pluginSources[2]);
        Assert.Contains("\"play_once_hotkey\"", pluginSources[2]);
        Assert.Contains("\"play_loop_hotkey\"", pluginSources[2]);
        Assert.Contains("previousWasRegistered", hotkeyControl);
        Assert.Contains("_hotKey.UnregisterAsync(newHotkey)", hotkeyControl);
        Assert.Contains("Rollback failed:", hotkeyControl);
    }

    [Fact]
    public void SideEffectingPluginCommands_ReportActualCompletion()
    {
        var root = FindRepositoryRoot();
        var translate = File.ReadAllText(
            Path.Combine(root, "src", "TranslatePlugin", "index.html"));
        var screenshot = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ScreenshotPlugin",
            "ScreenshotPluginImpl.cs"));
        var folderNote = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FolderNotePlugin",
            "FolderNotePluginImpl.cs"));

        Assert.Contains("onCommand(async function", translate);
        Assert.Contains("const success = await translate()", translate);
        Assert.Contains("success: success", translate);
        Assert.Contains("return await CaptureFullScreenAsync()", screenshot);
        Assert.Contains("Task<PluginCommandResult> CaptureFullScreenAsync()", screenshot);
        Assert.Contains("return await ShowNoteHudAsync(folderPath)", folderNote);
        Assert.Contains("Application.Current.Dispatcher.Invoke(() => _activeHud?.Close())", folderNote);
    }

    [Fact]
    public void ScreenshotAndColorPicker_UsePhysicalCoordinatesAndDeterministicCleanup()
    {
        var root = FindRepositoryRoot();
        var screenshot = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ScreenshotPlugin",
            "ScreenshotPluginImpl.cs"));
        var selector = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ScreenshotPlugin",
            "RegionSelectorWindow.xaml.cs"));
        var colorPicker = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ColorPickerPlugin",
            "ColorPickerWindow.xaml.cs"));
        var captureService = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Services",
            "ScreenCaptureService.cs"));

        Assert.Contains("_host.ScreenCapture.CaptureToBitmapAsync()", screenshot);
        Assert.Contains("_host.ScreenCapture.CaptureRegionAsync(", screenshot);
        Assert.DoesNotContain("internal static class ScreenCapture", screenshot);
        Assert.Contains("GetCursorPos(out _screenStart)", selector);
        Assert.Contains("Close();", selector);
        Assert.Contains(
            "await Dispatcher.Yield(DispatcherPriority.ApplicationIdle)",
            selector);
        Assert.Contains("MonitorHelper.GetCursorPlacement(this)", colorPicker);
        Assert.Contains("ScreenColorSampler.Sample(", colorPicker);
        Assert.Contains("finally", colorPicker);
        Assert.Contains("GetSystemMetrics(SmXvirtualscreen)", captureService);
        Assert.Contains("if (!BitBlt(", captureService);
        Assert.Contains("result.Freeze()", captureService);
    }

    [Fact]
    public void FolderNoteSave_AwaitsStorageBeforeClosing()
    {
        var root = FindRepositoryRoot();
        var hud = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "FloatingHudWindow.xaml.cs"));
        var folderNote = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FolderNotePlugin",
            "FolderNotePluginImpl.cs"));

        Assert.Contains("Func<string, Task>? _onSave", hud);
        Assert.Contains("await _onSave(text)", hud);
        Assert.Contains("catch (Exception exception)", hud);
        Assert.Contains("if (!result.IsSuccess)", folderNote);
        Assert.Contains(
            "noteResult.ErrorCode != ApiErrorCode.StreamNotFound",
            folderNote);
        Assert.Contains("\"error.loadFailed\"", folderNote);
        Assert.Contains("\"error.saveFailed\"", folderNote);
    }

    [Fact]
    public void MacroPlayback_UsesVirtualDesktopCoordinatesAndCancelableLifecycle()
    {
        var root = FindRepositoryRoot();
        var engine = File.ReadAllText(Path.Combine(
            root,
            "src",
            "MacroPlugin",
            "MacroEngine.cs"));
        var plugin = File.ReadAllText(Path.Combine(
            root,
            "src",
            "MacroPlugin",
            "MacroPluginImpl.cs"));
        var native = File.ReadAllText(Path.Combine(
            root,
            "src",
            "MacroPlugin",
            "MacroNativeApi.cs"));

        Assert.Contains("NormalizeAbsoluteCoordinate", native);
        Assert.Contains("MouseEventVirtualDesk", native);
        Assert.Contains("GetSystemMetrics(SmXVirtualScreen)", native);
        Assert.Contains("action.DelayMs", engine);
        Assert.Contains("cancellationToken", engine);
        Assert.Contains("ReleasePressedInputs", engine);
        Assert.Contains("StopPlayAsync", engine);
        Assert.Contains("await _engine.StopAsync()", plugin);
        Assert.Contains("PlaybackFailed", plugin);
        Assert.Contains("PluginCommandResult.Failure", plugin);
    }

    [Fact]
    public void ColorPicker_ClosesWhenDesktopSamplingIsUnavailable()
    {
        var root = FindRepositoryRoot();
        var picker = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ColorPickerPlugin",
            "ColorPickerWindow.xaml.cs"));
        var sampler = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Services",
            "ScreenColorSampler.cs"));

        Assert.Contains("if (!GetCursorPos(out var point))", picker);
        Assert.Contains("if (!UpdateSample(point))", picker);
        Assert.Contains("if (pixel == uint.MaxValue)", sampler);
        Assert.Contains("ReleaseDC(IntPtr.Zero, screenDc)", sampler);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
