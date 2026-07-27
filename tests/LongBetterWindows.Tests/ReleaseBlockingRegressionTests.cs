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
        Assert.Contains("DynamicResource Long.Brush.Surface.Card", scriptDialog);
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

        Assert.Contains("NormalizeAbsoluteCoordinate", engine);
        Assert.Contains("MOUSEEVENTF_VIRTUALDESK", engine);
        Assert.Contains("GetSystemMetrics(SM_XVIRTUALSCREEN)", engine);
        Assert.Contains("Task.Delay(action.DelayMs, cancellationToken)", engine);
        Assert.Contains("_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero", engine);
        Assert.Contains("_engine?.StopPlay()", plugin);
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

        Assert.Contains("if (!GetCursorPos(out var point))", picker);
        Assert.Contains("if (pixel == uint.MaxValue)", picker);
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
