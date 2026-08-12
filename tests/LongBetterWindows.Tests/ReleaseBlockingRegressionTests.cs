using System.IO;
using System.Text.RegularExpressions;
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
    public void PrimaryButtonStatesMeetNormalTextContrastInBothThemes()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "App.xaml.cs"));
        var css = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "WebAssets",
            "long-ui.css"));

        var darkPaletteStart = app.IndexOf("DarkPalette =", StringComparison.Ordinal);
        var lightPaletteStart = app.IndexOf("LightPalette =", darkPaletteStart, StringComparison.Ordinal);
        var lightPaletteEnd = app.IndexOf(
            "HighContrastPalette",
            lightPaletteStart,
            StringComparison.Ordinal);
        var darkPalette = app[darkPaletteStart..lightPaletteStart];
        var lightPalette = app[lightPaletteStart..lightPaletteEnd];
        var darkCssStart = css.IndexOf(":root {", StringComparison.Ordinal);
        var darkCssEnd = css.IndexOf('}', darkCssStart);
        var lightCssStart = css.IndexOf(
            ":root[data-long-theme=\"light\"]",
            StringComparison.Ordinal);
        var lightCssEnd = css.IndexOf('}', lightCssStart);
        var darkCss = css[darkCssStart..darkCssEnd];
        var lightCss = css[lightCssStart..lightCssEnd];

        AssertPrimaryButtonContrastAndParity(darkPalette, darkCss);
        AssertPrimaryButtonContrastAndParity(lightPalette, lightCss);
        Assert.Contains(
            "button.btn-primary:active, .long-button--primary:active",
            css);
    }

    [Fact]
    public void HighContrastActionsUseSystemForegroundBackgroundPair()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "App.xaml.cs"));
        var colors = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Themes",
            "Colors.xaml"));

        Assert.Contains(
            "[\"Long.Color.Text.OnAccent\"] = SystemColors.HighlightTextColor.ToString()",
            app);
        Assert.Contains(
            "[\"Long.Color.State.Danger\"] = SystemColors.HighlightColor.ToString()",
            app);
        Assert.Contains(
            "Long.Brush.Text.OnAccent\" Color=\"{DynamicResource Long.Color.Text.OnAccent}",
            colors);
    }

    private static void AssertPrimaryButtonContrastAndParity(
        string palette,
        string css)
    {
        var wpfColors = new[]
        {
            ExtractColor(palette, "Long.Color.Accent.Primary"),
            ExtractColor(palette, "Long.Color.Accent.Hover"),
            ExtractColor(palette, "Long.Color.Accent.Pressed"),
        };
        var webColors = new[]
        {
            ExtractCssColor(css, "--long-accent"),
            ExtractCssColor(css, "--long-accent-hover"),
            ExtractCssColor(css, "--long-accent-pressed"),
        };

        Assert.Equal(wpfColors[0], webColors[0], ignoreCase: true);
        Assert.Equal(wpfColors[1], webColors[1], ignoreCase: true);
        Assert.Equal(wpfColors[2], webColors[2], ignoreCase: true);
        Assert.All(
            wpfColors.Concat(webColors),
            color => Assert.True(
                ContrastRatio("#FFFFFF", color) >= 4.5,
                $"{color} does not provide 4.5:1 contrast for white button text."));
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
    public void MediumRiskWebPluginsAvoidDuplicatePollingAndStaleNetworkResults()
    {
        var root = FindRepositoryRoot();
        var clipboardHistory = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ClipboardHistory",
            "index.html"));
        var translate = File.ReadAllText(Path.Combine(
            root,
            "src",
            "TranslatePlugin",
            "index.html"));

        Assert.Contains("function startFallbackPolling()", clipboardHistory);
        Assert.Contains("if (monitorStarted) {", clipboardHistory);
        Assert.Contains("stopFallbackPolling();", clipboardHistory);
        Assert.Contains("startFallbackPolling();", clipboardHistory);
        Assert.DoesNotContain(
            "if (!fallbackTimer) {\n        fallbackTimer = window.setInterval",
            clipboardHistory);

        Assert.Contains("let requestGeneration = 0;", translate);
        Assert.Contains("const generation = ++requestGeneration;", translate);
        Assert.Contains(
            "if (generation !== requestGeneration) return false;",
            translate);
        Assert.Contains("function invalidateTranslation(showWarning)", translate);
        Assert.Contains(
            "input.addEventListener('input', function () { invalidateTranslation(true); });",
            translate);
        Assert.Contains("'status.sourceChanged'", translate);
    }

    private static string ExtractColor(string source, string key)
    {
        var match = Regex.Match(
            source,
            $@"\[""{Regex.Escape(key)}""\]\s*=\s*""(?<color>#[0-9A-Fa-f]{{6}})""");
        Assert.True(match.Success, $"Missing color {key}.");
        return match.Groups["color"].Value;
    }

    private static string ExtractCssColor(string source, string variable)
    {
        var match = Regex.Match(
            source,
            $@"{Regex.Escape(variable)}\s*:\s*(?<color>#[0-9A-Fa-f]{{6}})");
        Assert.True(match.Success, $"Missing CSS color {variable}.");
        return match.Groups["color"].Value;
    }

    private static double ContrastRatio(string first, string second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05)
            / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var channels = new[]
        {
            Convert.ToByte(hex.Substring(1, 2), 16),
            Convert.ToByte(hex.Substring(3, 2), 16),
            Convert.ToByte(hex.Substring(5, 2), 16),
        };
        var linear = channels.Select(channel =>
        {
            var value = channel / 255d;
            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }).ToArray();
        return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2];
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
        var diskSearch = File.ReadAllText(Path.Combine(
            root,
            "src",
            "QuickLaunchPlugin",
            "QuickLaunchDiskSearchEngine.cs"));

        Assert.Contains("private CancellationTokenSource? _searchCancellation", source);
        Assert.Contains("await Task.Delay(180, cancellationToken)", source);
        Assert.Contains("await Task.Run(", source);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested()", source);
        Assert.Contains("QuickLaunchDiskSearchEngine", source);
        Assert.Contains("_queryGeneration.IsCurrent(generation)", source);
        Assert.Contains("FairFileEnumerator", diskSearch);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested()", diskSearch);
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

        var qualityRuntime = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Services",
            "QualityRuntimeService.cs"));
        Assert.Contains(
            "HostProvider.Instance.PluginStore.GetAll()",
            qualityRuntime);
        Assert.Contains(
            "distinct_actual_identity_count",
            qualityRuntime);
        Assert.Contains("distinct_icon_count", qualityRuntime);
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
        var views = Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views");
        var inputDialog = File.ReadAllText(Path.Combine(
            views,
            "CapabilityInputDialog.xaml"));
        var selectDialog = File.ReadAllText(Path.Combine(
            views,
            "CapabilitySelectDialog.xaml"));
        var contentWindow = File.ReadAllText(Path.Combine(
            views,
            "PluginContentWindow.xaml"));
        var inputDialogCode = File.ReadAllText(Path.Combine(
            views,
            "CapabilityInputDialog.xaml.cs"));
        var selectDialogCode = File.ReadAllText(Path.Combine(
            views,
            "CapabilitySelectDialog.xaml.cs"));
        var contentWindowCode = File.ReadAllText(Path.Combine(
            views,
            "PluginContentWindow.xaml.cs"));

        Assert.Contains("Long.Brush.Background.Base", contentWindow);
        Assert.Contains("LongButton.Primary", inputDialog);
        Assert.Contains("LongTextBox", inputDialog);
        Assert.Contains("LongCommandItem", selectDialog);
        Assert.All(
            new[] { inputDialog, selectDialog, contentWindow },
            xaml =>
            {
                Assert.Contains("LongWindowChrome", xaml);
                Assert.Contains("ShowInTaskbar=\"False\"", xaml);
                Assert.Contains("WindowStartupLocation=\"CenterOwner\"", xaml);
                Assert.Contains("PreviewKeyDown=\"Window_PreviewKeyDown\"", xaml);
            });
        Assert.Contains("new PluginContentWindow(", service);
        Assert.Contains("ResolveOwner()", service);
        Assert.DoesNotContain("new Window", service);
        Assert.All(
            new[] { inputDialogCode, selectDialogCode, contentWindowCode },
            code => Assert.Contains("e.Key != Key.Escape", code));
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
        Assert.Contains("long.process.killPortOwnerVerified", portManager);
        Assert.DoesNotContain("long.process.kill(target.id)", portManager);
        Assert.DoesNotContain("long.process.killVerified(target.id", portManager);
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
        var guideCode = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WindowManagerPlugin",
                "WindowManagerGuide.xaml.cs"));
        var implementation = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WindowManagerPlugin",
                "WindowManagerPluginImpl.cs"));

        Assert.DoesNotContain("StaticResource LongDialog", guide);
        Assert.Contains("WindowChrome.WindowChrome", guide);
        Assert.Contains("DynamicResource Long.Brush.Text.Primary", guide);
        Assert.Contains("ShowInTaskbar=\"False\"", guide);
        Assert.Contains("WindowStartupLocation=\"CenterOwner\"", guide);
        Assert.Contains("PreviewKeyDown=\"Window_PreviewKeyDown\"", guide);
        Assert.Contains("e.Key != Key.Escape", guideCode);
        Assert.Contains("_guide.Owner = owner", implementation);
    }

    [Fact]
    public void ThemeSensitiveViews_UseSemanticColorResources()
    {
        var root = FindRepositoryRoot();
        var developerToolkit = File.ReadAllText(
            Path.Combine(root, "src", "DevToolkit", "index.html"));

        Assert.DoesNotMatch("#[0-9a-fA-F]{3,8}\\b", developerToolkit);
        Assert.Contains("var(--long-bg-base)", developerToolkit);
        Assert.Contains("class=\"btn-primary\"", developerToolkit);
        Assert.Contains("class=\"btn-secondary\"", developerToolkit);
        Assert.Contains("background: transparent !important", developerToolkit);
        Assert.Contains("prefers-reduced-motion: reduce", developerToolkit);
    }

    [Fact]
    public void SystemPickers_RequireResolvedOwnerAndLegacyScriptDialogIsRemoved()
    {
        var root = FindRepositoryRoot();
        var hostRoot = Path.Combine(root, "src", "LongBetterWindows.Host");
        var views = Path.Combine(hostRoot, "Views");
        var pickerSources = new[]
        {
            "MarketplaceControl.xaml.cs",
            "SystemIntegrationPageControl.xaml.cs",
            "WorkflowEditorControl.xaml.cs",
            "WorkflowInvocationEditorControl.xaml.cs",
        }.Select(file => File.ReadAllText(Path.Combine(views, file))).ToArray();
        var combined = string.Join(Environment.NewLine, pickerSources);
        var ownerResolver = File.ReadAllText(Path.Combine(
            hostRoot,
            "Helpers",
            "DialogOwnerResolver.cs"));

        Assert.DoesNotContain("dialog.ShowDialog()", combined);
        Assert.Equal(
            8,
            pickerSources.Sum(source =>
                source.Split("ShowDialog(DialogOwnerResolver.Resolve(this))")
                    .Length - 1));
        Assert.Contains("Window.GetWindow(origin)", ownerResolver);
        Assert.Contains("window.IsActive && window.IsVisible", ownerResolver);
        Assert.False(File.Exists(Path.Combine(views, "ScriptCreationDialog.xaml")));
        Assert.False(File.Exists(Path.Combine(views, "ScriptCreationDialog.xaml.cs")));

        foreach (var locale in new[] { "zh-CN.json", "en-US.json" })
        {
            var resources = File.ReadAllText(Path.Combine(hostRoot, "i18n", locale));
            Assert.DoesNotContain("developer.script.", resources);
        }
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
            "LongBetterWindows.PluginSdk.Wpf",
            "HotkeySettingsControl.cs"));

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
        Assert.Contains(
            "return await CaptureFullScreenAsync(cancellationToken)",
            screenshot);
        Assert.Contains(
            "Task<PluginCommandResult> CaptureFullScreenAsync(",
            screenshot);
        Assert.Contains(
            "return await ShowNoteHudAsync(folderPath, cancellationToken)",
            folderNote);
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
        var screenshotOperations = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ScreenshotPlugin",
            "ScreenshotOperationCoordinator.cs"));
        var screenshotGeometry = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ScreenshotPlugin",
            "ScreenshotRegionGeometry.cs"));
        var screenshotClipboard = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ScreenshotPlugin",
            "ScreenshotClipboardWriter.cs"));
        var colorPicker = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ColorPickerPlugin",
            "ColorPickerWindow.xaml.cs"));
        var colorPickerNative = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ColorPickerPlugin",
            "ColorPickerNativeInteraction.cs"));
        var colorPickerPlugin = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ColorPickerPlugin",
            "ColorPickerPluginImpl.cs"));
        var captureService = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Services",
            "ScreenCaptureService.cs"));

        Assert.Contains("_host.ScreenCapture.CaptureToBitmapAsync()", screenshot);
        Assert.Contains("_host.ScreenCapture.CaptureRegionAsync(", screenshot);
        Assert.Contains("AsyncDeliveryBoundary.RunAsync(", screenshot);
        Assert.Contains("_operationLifetime.Cancel()", screenshot);
        Assert.DoesNotContain("internal static class ScreenCapture", screenshot);
        Assert.Contains("TryGetCursorPosition(out _screenStart)", selector);
        Assert.Contains("PointFromScreen", selector);
        Assert.Contains("ScreenshotRegionGeometry.TryCreate", selector);
        Assert.Contains("CloseSelector();", selector);
        Assert.Contains(
            "await Dispatcher.Yield(DispatcherPriority.ApplicationIdle)",
            selector);
        Assert.Contains(
            "VirtualScreenHelper.PlaceWindowOverPhysicalBounds(this)",
            selector);
        Assert.Contains("Keyboard.Focus(SelectionCanvas)", selector);
        Assert.DoesNotContain("SystemParameters.VirtualScreen", selector);
        Assert.Contains("ScreenshotOperationCoordinator", screenshot);
        Assert.Contains("_operations.TryBegin()", screenshot);
        Assert.Contains("operation.Dispose()", screenshot);
        Assert.Contains("Interlocked.CompareExchange", screenshotOperations);
        Assert.Contains("minimumExtent", screenshotGeometry);
        Assert.Contains("DefaultRetryDelays", screenshotClipboard);
        Assert.Contains("ScreenshotClipboardDeliveryException", screenshotClipboard);
        Assert.Contains("ColorPickerNativeWindow.PositionNearCursor(this, point)", colorPicker);
        Assert.Contains("_screenColorSampler.Sample(", colorPicker);
        Assert.Contains("WaitForLeftButtonReleaseAsync", colorPicker);
        Assert.Contains("SetWindowsHookEx", colorPickerNative);
        Assert.Contains("UnhookWindowsHookEx", colorPickerNative);
        Assert.Contains("ColorPickerDeliveryCoordinator", colorPickerPlugin);
        Assert.Contains("finally", colorPicker);
        Assert.Contains(
            "VirtualScreenHelper.GetPhysicalBounds()",
            captureService);
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
            "LongBetterWindows.PluginSdk.Wpf",
            "AnchoredTextEditorWindow.xaml.cs"));
        var editorXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.PluginSdk.Wpf",
            "AnchoredTextEditorWindow.xaml"));
        var folderNote = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FolderNotePlugin",
            "FolderNotePluginImpl.cs"));
        var floatingHud = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "FloatingHudWindow.xaml.cs"));
        var adsService = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Services",
            "ADSService.cs"));
        var rollback = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Services",
            "RollbackEngine.cs"));
        var qualityRuntime = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Services",
            "QualityRuntimeService.cs"));
        var app = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "App.xaml.cs"));

        Assert.Contains("Func<string, Task> _onSave", hud);
        Assert.Contains("await _onSave(Editor.Text)", hud);
        Assert.Contains("PreviewKeyDown=\"Window_PreviewKeyDown\"", editorXaml);
        Assert.DoesNotContain(" KeyDown=\"Window_KeyDown\"", editorXaml);
        Assert.Contains("Long.Sdk.AnchoredTextEditor.Window", editorXaml);
        Assert.Contains("Long.Sdk.AnchoredTextEditor.Input", editorXaml);
        var floatingHudXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "FloatingHudWindow.xaml"));
        Assert.Contains("PreviewKeyDown=\"Window_PreviewKeyDown\"", floatingHudXaml);
        Assert.DoesNotContain(" KeyDown=\"Window_KeyDown\"", floatingHudXaml);
        Assert.Contains("Long.FolderNote.Hud.Window", floatingHudXaml);
        Assert.Contains("Long.FolderNote.Hud.Input", floatingHudXaml);
        Assert.DoesNotContain("Editor.Text.Trim()", hud);
        Assert.Contains("await _onSave(NoteTextBox.Text)", floatingHud);
        Assert.Contains("e.Handled = true", floatingHud);
        Assert.DoesNotContain("NoteTextBox.Text.Trim()", floatingHud);
        Assert.Contains("catch (Exception exception)", hud);
        Assert.Contains("if (!result.IsSuccess)", folderNote);
        Assert.Contains("SemaphoreSlim _openGate", folderNote);
        Assert.Contains("State != PluginState.Running", folderNote);
        Assert.Contains("await _openGate.WaitAsync()", folderNote);
        Assert.Contains("error.editorOpen", folderNote);
        Assert.Contains("ApiErrorCode.NotNTFSVolume", folderNote);
        Assert.Contains(
            "noteResult.ErrorCode != ApiErrorCode.StreamNotFound",
            folderNote);
        Assert.Contains("\"error.loadFailed\"", folderNote);
        Assert.Contains("\"error.saveFailed\"", folderNote);
        Assert.Contains("EnsureNoReparsePoint", adsService);
        Assert.DoesNotContain("GetFallbackPath", adsService);
        Assert.DoesNotContain("FolderNoteFallbackFileName", adsService);
        Assert.Contains("ResolveAdsRollbackTarget", rollback);
        Assert.Contains("ValidateRollbackTarget", rollback);
        Assert.Contains("ValidateRollbackTarget", adsService);
        Assert.Contains("rollback.AdsMutationGate", adsService);
        Assert.Contains("lock (AdsMutationGate)", rollback);
        Assert.Contains("\"folder-note\"", qualityRuntime);
        Assert.Contains("OfType<AnchoredTextEditorWindow>()", qualityRuntime);
        Assert.Contains("TaskCompletionSource", app);
        Assert.Contains("await closed.Task", app);
    }

    [Fact]
    public void TransientStatusSurfaces_DoNotActivateOrEscapeTheWorkArea()
    {
        var root = FindRepositoryRoot();
        var toastXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "ToastWindow.xaml"));
        var toastCode = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "ToastWindow.xaml.cs"));
        var macroXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "MacroPlugin",
            "MacroOverlay.xaml"));
        var macroCode = File.ReadAllText(Path.Combine(
            root,
            "src",
            "MacroPlugin",
            "MacroOverlay.xaml.cs"));
        var behavior = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.PluginSdk.Wpf",
            "TransientWindowBehavior.cs"));

        Assert.Contains("ShowActivated=\"False\"", toastXaml);
        Assert.Contains("Focusable=\"False\"", toastXaml);
        Assert.Contains("IsHitTestVisible=\"False\"", toastXaml);
        Assert.Contains("Long.Toast.Window", toastXaml);
        Assert.Contains("Long.Toast.Message", toastXaml);
        Assert.Contains("ActiveWindows", toastCode);
        Assert.Contains("GetCursorPlacement(window).WorkArea", toastCode);
        Assert.Contains("Reposition(window._workArea)", toastCode);
        Assert.Contains("MakeNonActivating(this, clickThrough: true)", toastCode);

        Assert.Contains("ShowActivated=\"False\"", macroXaml);
        Assert.Contains("Focusable=\"False\"", macroXaml);
        Assert.Contains("IsHitTestVisible=\"False\"", macroXaml);
        Assert.Contains("Long.Macro.Overlay.Window", macroXaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", macroXaml);
        Assert.Contains("GetCursorPlacement(window).WorkArea", macroCode);
        Assert.Contains("SizeChanged +=", macroCode);
        Assert.Contains("PositionInsideWorkArea", macroCode);
        Assert.Contains("MakeNonActivating(this, clickThrough: true)", macroCode);
        Assert.Contains("WsExNoActivate", behavior);
        Assert.Contains("WsExTransparent", behavior);
        Assert.Contains("WsExToolWindow", behavior);
    }

    [Fact]
    public void NativePluginWindows_ExposeStableAutomationAndDpiPlacement()
    {
        var root = FindRepositoryRoot();
        var quickLaunch = File.ReadAllText(Path.Combine(
            root, "src", "QuickLaunchPlugin", "LaunchWindow.xaml"));
        var quickLaunchCode = File.ReadAllText(Path.Combine(
            root, "src", "QuickLaunchPlugin", "LaunchWindow.xaml.cs"));
        var colorPicker = File.ReadAllText(Path.Combine(
            root, "src", "ColorPickerPlugin", "ColorPickerWindow.xaml"));
        var screenshot = File.ReadAllText(Path.Combine(
            root, "src", "ScreenshotPlugin", "RegionSelectorWindow.xaml"));

        Assert.Contains("Long.QuickLaunch.Window", quickLaunch);
        Assert.Contains("Long.QuickLaunch.Search", quickLaunch);
        Assert.Contains("Long.QuickLaunch.Results", quickLaunch);
        Assert.Contains("PreviewKeyDown=\"Window_PreviewKeyDown\"", quickLaunch);
        Assert.DoesNotContain(" KeyDown=\"Window_KeyDown\"", quickLaunch);
        Assert.Contains("GetCursorPlacement(this)", quickLaunchCode);
        Assert.DoesNotContain("GetCursorWorkArea()", quickLaunchCode);

        Assert.Contains("Long.ColorPicker.Window", colorPicker);
        Assert.Contains("Long.ColorPicker.Hex", colorPicker);
        Assert.Contains("Long.ColorPicker.Rgb", colorPicker);
        Assert.Contains("Long.Screenshot.RegionSelector.Window", screenshot);
        Assert.Contains("Long.Screenshot.RegionSelector.Canvas", screenshot);
        Assert.Contains("Long.Screenshot.RegionSelector.Selection", screenshot);
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
        var clipboardWriter = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ColorPickerPlugin",
            "ColorPickerClipboardWriter.cs"));
        var plugin = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ColorPickerPlugin",
            "ColorPickerPluginImpl.cs"));

        Assert.Contains(
            "if (!ColorPickerNativeWindow.TryGetCursorPosition(out var point))",
            picker);
        Assert.Contains("if (!UpdateSample(point))", picker);
        Assert.Contains("outside the physical virtual screen", sampler);
        Assert.Contains("if (pixel == uint.MaxValue)", sampler);
        Assert.Contains("ReleaseDC(IntPtr.Zero, screenDc)", sampler);
        Assert.Contains("DefaultRetryDelays", clipboardWriter);
        Assert.Contains("await Task.Delay", clipboardWriter);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested()", clipboardWriter);
        Assert.Contains("HasCommittedSelection", picker);
        Assert.Contains("delivery.Cancel()", plugin);
        Assert.Contains("ReferenceEquals(_window, window)", plugin);
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
