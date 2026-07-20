using System.IO;
using System.Xml.Linq;

namespace LongBetterWindows.Tests;

public class DesignSystemTests
{
    [Fact]
    public void Colors_ContainsRequiredSemanticTokens()
    {
        var document = LoadXaml("src", "LongBetterWindows.Host", "Themes", "Colors.xaml");
        var keys = GetResourceKeys(document);
        var required = new[]
        {
            "Long.Brush.Background.Base",
            "Long.Brush.Background.Raised",
            "Long.Brush.Surface.Card",
            "Long.Brush.Surface.Hover",
            "Long.Brush.Surface.Overlay",
            "Long.Brush.Stroke.Default",
            "Long.Brush.Stroke.Strong",
            "Long.Brush.Text.Primary",
            "Long.Brush.Text.Secondary",
            "Long.Brush.Text.Muted",
            "Long.Brush.Accent.Primary",
            "Long.Brush.Accent.Hover",
            "Long.Brush.Accent.Soft",
            "Long.Brush.State.Success",
            "Long.Brush.State.Warning",
            "Long.Brush.State.Danger",
        };

        Assert.All(required, key => Assert.Contains(key, keys));
    }

    [Fact]
    public void LongComponents_ContainsCoreComponentStyles()
    {
        var document = LoadXaml("src", "LongBetterWindows.Host", "Themes", "LongComponents.xaml");
        var keys = GetResourceKeys(document);
        var required = new[]
        {
            "LongButton",
            "LongIconButton",
            "LongTextBox",
            "LongSearchBox",
            "LongCard",
            "LongCommandItem",
            "LongPluginCard",
            "LongBadge",
            "LongHotkeyBadge",
            "LongToggle",
            "LongDialog",
            "LongToast",
            "LongEmptyState",
            "LongLoadingState",
            "LongComboBox",
            "LongNavigationItem",
            "LongWindowChrome",
        };

        Assert.All(required, key => Assert.Contains(key, keys));
    }

    [Fact]
    public void WebUiKit_ExposesThemeMotionAndControlTokens()
    {
        var root = FindRepositoryRoot();
        var css = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "WebAssets", "long-ui.css"));

        Assert.Contains(":root[data-long-theme=\"light\"]", css);
        Assert.Contains("prefers-reduced-motion", css);
        Assert.Contains("--long-accent", css);
        Assert.Contains(".long-card", css);
        Assert.Contains(".long-button", css);
        Assert.Contains(".long-input", css);
    }

    [Fact]
    public void WebUiKit_IsCopiedToHostOutput()
    {
        var css = Path.Combine(AppContext.BaseDirectory, "WebAssets", "long-ui.css");
        var js = Path.Combine(AppContext.BaseDirectory, "WebAssets", "long-ui.js");
        Assert.True(File.Exists(css), $"Missing published UI kit: {css}");
        Assert.True(File.Exists(js), $"Missing published UI helpers: {js}");
    }

    [Fact]
    public void CoreStageThreeViews_UseSemanticColorsOnly()
    {
        var root = FindRepositoryRoot();
        var views = new[]
        {
            Path.Combine("src", "LongBetterWindows.Host", "MainWindow.xaml"),
            Path.Combine("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml"),
            Path.Combine("src", "LongBetterWindows.Host", "Views", "PluginManagementControl.xaml"),
            Path.Combine("src", "LongBetterWindows.Host", "Views", "MarketplaceControl.xaml"),
            Path.Combine("src", "LongBetterWindows.Host", "Views", "CommandPaletteWindow.xaml"),
            Path.Combine("src", "LongBetterWindows.Host", "Views", "PluginWindowHost.xaml"),
            Path.Combine("src", "LongBetterWindows.Host", "Views", "PerformancePanel.xaml"),
            Path.Combine("src", "LongBetterWindows.Host", "Views", "CapabilityDetailPanel.xaml"),
            Path.Combine("src", "LongBetterWindows.Host", "Views", "FloatingHudWindow.xaml"),
            Path.Combine("src", "LongBetterWindows.Host", "Views", "ToastWindow.xaml"),
        };

        Assert.All(views, relativePath =>
        {
            var content = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.DoesNotMatch("#[0-9A-Fa-f]{6,8}", content);
        });
    }

    [Fact]
    public void ToolCenter_DefinesAllManagementWorkspaces()
    {
        var root = FindRepositoryRoot();
        var content = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml"));

        var requiredPages = new[]
        {
            "PanelOverview",
            "PanelPlugins",
            "PanelMarket",
            "PanelSystem",
            "PanelDiagnostics",
            "PanelDev",
            "PanelSettings",
        };

        Assert.All(requiredPages, page => Assert.Contains($"x:Name=\"{page}\"", content));
    }

    [Fact]
    public void DiagnosticsAndCapabilityViews_UseStructuredDesignSystemEntryPoints()
    {
        var root = FindRepositoryRoot();
        var toolCenter = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml.cs"));
        var pluginManagement = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "Views", "PluginManagementControl.xaml.cs"));
        var pluginManagementView = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "Views", "PluginManagementControl.xaml"));
        var performance = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "Views", "PerformancePanel.xaml.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "Interaction", "PerformanceRefreshCoordinator.cs"));
        var capabilityView = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "Views", "CapabilityDetailPanel.xaml"));

        Assert.Contains("DiagnosticsHost.Content == null", toolCenter);
        Assert.Contains("DiagnosticsHost.Content = new PerformancePanel()", toolCenter);
        Assert.Contains("OpenDiagnosticsForQuality", toolCenter);
        Assert.Contains("CapabilityDetails_Click", pluginManagement);
        Assert.Contains("Long.Icon.Shield", pluginManagementView);
        Assert.Contains("查看插件权限", pluginManagementView);
        Assert.DoesNotContain("CapabilityDetails_Click", toolCenter);
        Assert.DoesNotContain("DispatcherTimer", performance);
        Assert.DoesNotContain("new SolidColorBrush", performance);
        Assert.Contains("DispatcherTimer", coordinator);
        Assert.Contains("PerformanceSnapshot", coordinator);
        Assert.Contains("ItemsControl", capabilityView);
        Assert.Contains("LongPluginCard", capabilityView);
    }

    [Fact]
    public void MigratedWebPlugins_UseUiKitCommandBridgeAndKeyboardFlow()
    {
        var root = FindRepositoryRoot();
        var plugins = new[]
        {
            "Base64Tool",
            "JsonFormatterPlugin",
            "PasswordGenerator",
            "QuickNotePlugin",
            "TranslatePlugin",
            "TextDiffPlugin",
            "ClipboardTool",
            "MarkdownPreview",
            "FileRenamerPlugin",
            "UrlToolkit",
            "TimestampConverter",
            "RegexTester",
            "UuidGenerator",
        };

        Assert.All(plugins, plugin =>
        {
            var html = File.ReadAllText(Path.Combine(root, "src", plugin, "index.html"));
            Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", html);
            Assert.Contains("class=\"long-page\"", html);
            Assert.Contains("LongUI?.onCommand", html);
            Assert.Contains("addEventListener('keydown'", html);
            Assert.Contains("aria-live=\"polite\"", html);
        });
    }

    [Fact]
    public void MigratedWebPlugins_DeclareCommandsAndWindowPreferences()
    {
        var root = FindRepositoryRoot();
        var plugins = new[]
        {
            "Base64Tool",
            "JsonFormatterPlugin",
            "PasswordGenerator",
            "QuickNotePlugin",
            "TranslatePlugin",
            "TextDiffPlugin",
            "ClipboardTool",
            "MarkdownPreview",
            "FileRenamerPlugin",
            "UrlToolkit",
            "TimestampConverter",
            "RegexTester",
            "UuidGenerator",
        };

        Assert.All(plugins, plugin =>
        {
            var json = File.ReadAllText(Path.Combine(root, "src", plugin, "manifest.json"));
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var manifest = document.RootElement;
            Assert.True(manifest.GetProperty("commands").GetArrayLength() > 0);
            Assert.True(manifest.GetProperty("window").TryGetProperty("mode", out _));
        });
    }

    [Fact]
    public void EveryBuiltInPlugin_DeclaresAtLeastOneUnifiedCommand()
    {
        var root = FindRepositoryRoot();
        var source = Path.Combine(root, "src");
        var manifests = Directory.GetDirectories(source)
            .Select(folder => Path.Combine(folder, "manifest.json"))
            .Where(File.Exists)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(25, manifests.Length);

        var pluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in manifests)
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var manifest = document.RootElement;
            var id = manifest.GetProperty("id").GetString();

            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.True(pluginIds.Add(id!));
            Assert.True(manifest.GetProperty("commands").GetArrayLength() > 0, path);
        }
    }

    [Fact]
    public void MainlinePreservedPlugins_DeclareWindowAndLifecycleContracts()
    {
        var root = FindRepositoryRoot();
        var plugins = new[]
        {
            "ClipboardHistory",
            "DevToolkit",
            "FileOrganizer",
            "HardwareMonitor",
            "PortManager",
        };

        Assert.All(plugins, plugin =>
        {
            var path = Path.Combine(root, "src", plugin, "manifest.json");
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var manifest = document.RootElement;

            Assert.True(manifest.GetProperty("window").TryGetProperty("mode", out _), path);
            Assert.True(manifest.GetProperty("lifecycle").TryGetProperty("close_behavior", out _), path);
            Assert.True(manifest.GetProperty("lifecycle").TryGetProperty("default_presentation", out _), path);
        });
    }

    [Fact]
    public void WebUiKit_ExposesUnifiedPluginCommandBridge()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "WebAssets", "long-ui.js"));

        Assert.Contains("LongUI.onCommand", script);
        Assert.Contains("long.command", script);
        Assert.Contains("long:command", script);
    }

    [Fact]
    public void WebRuntime_UsesPluginPermissionContextAndLowercaseResponseContract()
    {
        var root = FindRepositoryRoot();
        var runtime = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "Engine", "WebPluginRuntime.cs"));
        var dispatcher = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "Engine", "WebPluginHostDispatcher.cs"));

        Assert.Contains("PluginAccessContext.Enter(_manifest.Id)", runtime);
        Assert.Contains("success = r.IsSuccess", dispatcher);
        Assert.DoesNotContain("return new { r.IsSuccess", dispatcher);
    }

    [Fact]
    public void TranslatePlugin_DeclaresControlledHttpCapability()
    {
        var root = FindRepositoryRoot();
        var manifest = File.ReadAllText(Path.Combine(
            root, "src", "TranslatePlugin", "manifest.json"));
        var html = File.ReadAllText(Path.Combine(
            root, "src", "TranslatePlugin", "index.html"));

        Assert.Contains("\"network.http\"", manifest);
        Assert.Contains("long.http.get", html);
        Assert.DoesNotContain("fetch(", html);
    }

    [Fact]
    public void MarkdownPreview_UsesSafeDomRenderingAndRestrictedLinks()
    {
        var root = FindRepositoryRoot();
        var html = File.ReadAllText(Path.Combine(
            root, "src", "MarkdownPreview", "index.html"));

        Assert.Contains("preview.replaceChildren()", html);
        Assert.Contains("isSafeUrl", html);
        Assert.Contains("noopener noreferrer", html);
        Assert.DoesNotContain("preview.innerHTML =", html);
    }

    [Fact]
    public void FileRenamer_UsesDeclaredFileOperationsCapability()
    {
        var root = FindRepositoryRoot();
        var manifest = File.ReadAllText(Path.Combine(
            root, "src", "FileRenamerPlugin", "manifest.json"));
        var dispatcher = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "Engine", "WebPluginHostDispatcher.cs"));

        Assert.Contains("\"file.ops\"", manifest);
        Assert.Contains("FileOps.MoveAsync", dispatcher);
        Assert.DoesNotContain("File.Move(oldPath", dispatcher);
    }

    [Fact]
    public void ClipboardTool_DoesNotClaimUnusedGlobalHotkey()
    {
        var root = FindRepositoryRoot();
        var manifest = File.ReadAllText(Path.Combine(
            root, "src", "ClipboardTool", "manifest.json"));
        var html = File.ReadAllText(Path.Combine(
            root, "src", "ClipboardTool", "index.html"));

        Assert.DoesNotContain("system.hotkey", manifest);
        Assert.Contains("visibilitychange", html);
        Assert.Contains("replaceChildren", html);
    }

    [Fact]
    public void NativeUiBatch_UsesSemanticStylesAndDeclaresCommandEntries()
    {
        var root = FindRepositoryRoot();
        var plugins = new[]
        {
            (Folder: "QuickLaunchPlugin", View: "LaunchWindow.xaml"),
            (Folder: "MacroPlugin", View: "MacroOverlay.xaml"),
            (Folder: "ColorPickerPlugin", View: "ColorPickerWindow.xaml"),
        };

        Assert.All(plugins, plugin =>
        {
            var folder = Path.Combine(root, "src", plugin.Folder);
            var xaml = File.ReadAllText(Path.Combine(folder, plugin.View));
            var manifest = File.ReadAllText(Path.Combine(folder, "manifest.json"));
            using var document = System.Text.Json.JsonDocument.Parse(manifest);

            Assert.Contains("Long.Brush.", xaml);
            Assert.DoesNotMatch("(Background|Foreground|BorderBrush|Fill|Stroke)=\"#[0-9A-Fa-f]{3,8}\"", xaml);
            Assert.True(document.RootElement.GetProperty("commands").GetArrayLength() > 0);
            Assert.True(document.RootElement.GetProperty("window").TryGetProperty("mode", out _));
        });
    }

    [Fact]
    public void QuickLaunch_UsesCommandPaletteInsteadOfASecondGlobalHotkey()
    {
        var root = FindRepositoryRoot();
        var folder = Path.Combine(root, "src", "QuickLaunchPlugin");
        var implementation = File.ReadAllText(Path.Combine(folder, "QuickLaunchPluginImpl.cs"));
        var manifest = File.ReadAllText(Path.Combine(folder, "manifest.json"));

        Assert.Contains("IPluginCommandHandler", implementation);
        Assert.Contains("launcher.open", manifest);
        Assert.DoesNotContain("system.hotkey", manifest);
        Assert.DoesNotContain("RegisterAsync", implementation);
    }

    [Fact]
    public void NativeHudPlugins_SurviveHotkeyConflictsAndUseHostClipboard()
    {
        var root = FindRepositoryRoot();
        var macro = File.ReadAllText(Path.Combine(
            root, "src", "MacroPlugin", "MacroPluginImpl.cs"));
        var picker = File.ReadAllText(Path.Combine(
            root, "src", "ColorPickerPlugin", "ColorPickerPluginImpl.cs"));
        var pickerWindow = File.ReadAllText(Path.Combine(
            root, "src", "ColorPickerPlugin", "ColorPickerWindow.xaml.cs"));

        Assert.Contains("Ctrl+Alt+F6", macro);
        Assert.Contains("State = PluginState.Running", macro);
        Assert.Contains("return true;", macro);
        Assert.Contains("Ctrl+Alt+P", picker);
        Assert.Contains("Clipboard.SetTextAsync", picker);
        Assert.DoesNotContain("System.Windows.Clipboard", picker);
        Assert.Contains("GetAsyncKeyState", pickerWindow);
    }

    [Fact]
    public void FinalNativeBatch_DeclaresCommandsAndWindowPreferences()
    {
        var root = FindRepositoryRoot();
        var plugins = new[] { "ScreenshotPlugin", "FolderNotePlugin", "WindowManagerPlugin", "SamplePlugin" };

        Assert.All(plugins, plugin =>
        {
            var json = File.ReadAllText(Path.Combine(root, "src", plugin, "manifest.json"));
            using var document = System.Text.Json.JsonDocument.Parse(json);
            Assert.True(document.RootElement.GetProperty("commands").GetArrayLength() > 0);
            Assert.True(document.RootElement.GetProperty("window").TryGetProperty("mode", out _));
        });
    }

    [Fact]
    public void Screenshot_UsesSemanticOverlayHostClipboardAndHotkeyFallbacks()
    {
        var root = FindRepositoryRoot();
        var implementation = File.ReadAllText(Path.Combine(
            root, "src", "ScreenshotPlugin", "ScreenshotPluginImpl.cs"));
        var overlay = File.ReadAllText(Path.Combine(
            root, "src", "ScreenshotPlugin", "RegionSelectorWindow.xaml"));

        Assert.Contains("Clipboard.SetImageAsync", implementation);
        Assert.DoesNotContain("Clipboard.SetImage(", implementation);
        Assert.Contains("Ctrl+Alt+Shift+A", implementation);
        Assert.Contains("State = PluginState.Running", implementation);
        Assert.Contains("Long.Brush.", overlay);
        Assert.DoesNotMatch("(Background|Foreground|BorderBrush|Fill|Stroke)=\"#[0-9A-Fa-f]{3,8}\"", overlay);
    }

    [Fact]
    public void FolderNote_ConsumesExplorerSelectionAndWindowManagerExposesCommands()
    {
        var root = FindRepositoryRoot();
        var note = File.ReadAllText(Path.Combine(
            root, "src", "FolderNotePlugin", "FolderNotePluginImpl.cs"));
        var manager = File.ReadAllText(Path.Combine(
            root, "src", "WindowManagerPlugin", "WindowManagerPluginImpl.cs"));
        var guide = File.ReadAllText(Path.Combine(
            root, "src", "WindowManagerPlugin", "WindowManagerGuide.xaml"));

        Assert.Contains("AcceptedInputType.ExplorerSelection", note);
        Assert.Contains("FirstOrDefault(Directory.Exists)", note);
        Assert.Contains("Ctrl+Alt+M", note);
        Assert.Contains("IPluginCommandHandler", manager);
        Assert.Contains("window.topmost", manager);
        Assert.Contains("State = PluginState.Running", manager);
        Assert.Contains("Long.Brush.", guide);
    }

    [Fact]
    public void SamplePlugin_IsAWorkingUnifiedCommandTemplate()
    {
        var root = FindRepositoryRoot();
        var sample = File.ReadAllText(Path.Combine(root, "src", "SamplePlugin", "HelloPlugin.cs"));
        var manifest = File.ReadAllText(Path.Combine(root, "src", "SamplePlugin", "manifest.json"));

        Assert.Contains("IPluginCommandHandler", sample);
        Assert.Contains("PluginCommandInvocation", sample);
        Assert.Contains("PluginCommandResult.Success", sample);
        Assert.Contains("sample.hello", manifest);
    }

    [Fact]
    public void CommandPalette_ReleasesForegroundBeforeContextSensitiveCommands()
    {
        var root = FindRepositoryRoot();
        var palette = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "Views", "CommandPaletteWindow.xaml.cs"));
        var hideIndex = palette.IndexOf("Hide();", StringComparison.Ordinal);
        var delayIndex = palette.IndexOf("await Task.Delay(40)", StringComparison.Ordinal);
        var executeIndex = palette.IndexOf("_executor.ExecuteAsync", StringComparison.Ordinal);

        Assert.True(hideIndex >= 0, "Command Palette must hide before command execution.");
        Assert.True(delayIndex > hideIndex);
        Assert.True(executeIndex > delayIndex);
        Assert.Contains("Show();", palette);
        Assert.Contains("Activate();", palette);
    }

    private static XDocument LoadXaml(params string[] pathParts)
        => XDocument.Load(Path.Combine(new[] { FindRepositoryRoot() }.Concat(pathParts).ToArray()));

    private static HashSet<string> GetResourceKeys(XDocument document)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Descendants()
            .Select(element => (string?)element.Attribute(x + "Key"))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
