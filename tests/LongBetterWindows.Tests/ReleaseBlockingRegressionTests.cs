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
            "LongAssistant.Plugin.com.long.json.formatter",
            first);
        Assert.NotEqual(first, second);

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
