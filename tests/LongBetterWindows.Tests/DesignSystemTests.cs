using System.IO;
using System.Xml.Linq;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

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
    public void WebUiKit_ExposesAccessibleContentStates()
    {
        var root = FindRepositoryRoot();
        var css = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "WebAssets", "long-ui.css"));
        var script = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "WebAssets", "long-ui.js"));

        Assert.Contains(".long-state--empty", css);
        Assert.Contains(".long-state--loading", css);
        Assert.Contains(".long-state--error", css);
        Assert.Contains("data-long-reduced-motion", css);
        Assert.Contains("LongUI.renderState", script);
        Assert.Contains("LongUI.clearState", script);
        Assert.Contains("kind === 'error' ? 'alert' : 'status'", script);
        Assert.Contains("container.toggleAttribute('aria-busy', kind === 'loading')", script);
        Assert.Contains("container.replaceChildren(state)", script);
        Assert.DoesNotContain("innerHTML", script);
    }

    [Fact]
    public void RepresentativeWebPlugins_UseUnifiedContentStates()
    {
        var root = FindRepositoryRoot();
        var portManager = File.ReadAllText(Path.Combine(
            root, "src", "PortManager", "index.html"));
        var clipboardHistory = File.ReadAllText(Path.Combine(
            root, "src", "ClipboardHistory", "index.html"));

        Assert.Contains("LongUI?.renderState", portManager);
        Assert.Contains("kind: 'loading'", portManager);
        Assert.Contains("kind: 'empty'", portManager);
        Assert.Contains("kind: 'error'", portManager);
        Assert.DoesNotContain(".empty-state", portManager);
        Assert.DoesNotContain(".loading", portManager);

        Assert.Contains("LongUI?.renderState", clipboardHistory);
        Assert.Contains("kind: 'loading'", clipboardHistory);
        Assert.Contains("kind: 'empty'", clipboardHistory);
        Assert.Contains("kind: 'error'", clipboardHistory);
        Assert.Contains("long.clipboard.startMonitoring(handleClipboardChanged)", clipboardHistory);
        Assert.Contains("mutationQueue", clipboardHistory);
        Assert.Contains("history = previous", clipboardHistory);
        Assert.Contains("long.hotkey.unregister(hotkey)", clipboardHistory);
        Assert.DoesNotContain("<style", clipboardHistory);
        Assert.DoesNotContain("innerHTML", clipboardHistory);
        Assert.DoesNotContain("onclick=", clipboardHistory);
        Assert.DoesNotContain(".empty-state", clipboardHistory);
        Assert.DoesNotContain(".empty-icon", clipboardHistory);
    }

    [Fact]
    public void SecondBatchWebPlugins_UseSafeUnifiedContentStates()
    {
        var root = FindRepositoryRoot();
        var hardwareMonitor = File.ReadAllText(Path.Combine(
            root, "src", "HardwareMonitor", "index.html"));
        var clipboardTool = File.ReadAllText(Path.Combine(
            root, "src", "ClipboardTool", "index.html"));
        var regexTester = File.ReadAllText(Path.Combine(
            root, "src", "RegexTester", "index.html"));

        Assert.Contains("kind: 'loading'", hardwareMonitor);
        Assert.Contains("kind: 'empty'", hardwareMonitor);
        Assert.Contains("kind: 'error'", hardwareMonitor);
        Assert.Contains("appendText", hardwareMonitor);
        Assert.DoesNotContain("innerHTML", hardwareMonitor);

        Assert.Contains("kind: 'loading'", clipboardTool);
        Assert.Contains("kind: 'empty'", clipboardTool);
        Assert.Contains("kind: 'error'", clipboardTool);
        Assert.Contains("if (!historyResult.success) throw", clipboardTool);
        Assert.Contains("mutationQueue", clipboardTool);
        Assert.Contains("persistCollection", clipboardTool);
        Assert.Contains("history = previous", clipboardTool);
        Assert.Contains("responseData(response", clipboardTool);
        Assert.DoesNotContain("async function persist()", clipboardTool);
        Assert.DoesNotContain("id=\"emptyState\"", clipboardTool);
        Assert.DoesNotContain("class=\"long-empty\"", clipboardTool);

        Assert.Contains("kind: 'empty'", regexTester);
        Assert.Contains("kind: 'error'", regexTester);
        Assert.DoesNotContain("innerHTML", regexTester);
        Assert.DoesNotContain("class=\"long-empty\"", regexTester);
    }

    [Fact]
    public void BuiltInWebPlugins_DoNotReintroducePrivateContentStates()
    {
        var root = FindRepositoryRoot();
        var source = Path.Combine(root, "src");
        var pages = Directory.GetFiles(source, "index.html", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(pages);
        Assert.All(pages, path =>
        {
            var html = File.ReadAllText(path);
            Assert.DoesNotContain("class=\"long-empty", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("empty-state", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("class=\"loading", html, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var plugin in new[] { "FileRenamerPlugin", "QuickNotePlugin", "MarkdownPreview" })
        {
            var html = File.ReadAllText(Path.Combine(root, "src", plugin, "index.html"));
            Assert.Contains("LongUI?.renderState", html);
            Assert.Contains("kind: 'empty'", html);
        }

        var fileRenamer = File.ReadAllText(Path.Combine(
            root, "src", "FileRenamerPlugin", "index.html"));
        var quickNote = File.ReadAllText(Path.Combine(
            root, "src", "QuickNotePlugin", "index.html"));
        Assert.Contains("if (!response.success) throw", fileRenamer);
        Assert.Contains("mutationQueue", quickNote);
        Assert.Contains("persistSnapshot", quickNote);
        Assert.Contains("notes = previous", quickNote);
        Assert.Contains("if (!result || !result.success)", quickNote);
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
            "PanelWorkflows",
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
        var zhResources = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "i18n", "zh-CN.json"));
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
        Assert.Contains("i18n.plugins.openCapabilities", pluginManagementView);
        Assert.Contains("查看插件权限", zhResources);
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
            "FileOrganizer",
            "UrlToolkit",
            "TimestampConverter",
            "RegexTester",
            "UuidGenerator",
            "HardwareMonitor",
            "PortManager",
        };

        Assert.All(plugins, plugin =>
        {
            var html = File.ReadAllText(Path.Combine(root, "src", plugin, "index.html"));
            Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", html);
            Assert.Contains("class=\"long-page", html);
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
            "FileOrganizer",
            "UrlToolkit",
            "TimestampConverter",
            "RegexTester",
            "UuidGenerator",
            "HardwareMonitor",
            "PortManager",
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
        Assert.Contains("long.command-result", script);
        Assert.Contains("request_id", script);
    }

    [Fact]
    public void HardwareMonitor_UsesRealPerformanceApisAndUiKit()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "src", "HardwareMonitor", "index.html"));
        var uiKit = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "WebAssets", "long-ui.css"));
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "src", "HardwareMonitor", "manifest.json")));

        Assert.DoesNotContain("<style", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", page);
        Assert.Contains("long.performance.getCpuUsage()", page);
        Assert.Contains("long.performance.getMemoryInfo()", page);
        Assert.Contains("long.performance.getDiskInfo()", page);
        Assert.Contains("long.performance.getSystemInfo()", page);
        Assert.Contains("long.performance.getTopByMemory(10)", page);
        Assert.DoesNotContain("Math.random", page);
        Assert.Contains("visibilitychange", page);
        Assert.Contains("LongUI?.onCommand", page);
        Assert.Contains("event.key === 'F5'", page);
        var capabilities = manifest.RootElement.GetProperty("capabilities")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();
        Assert.Equal(["system.performance"], capabilities);

        Assert.Contains(".long-metric-grid", uiKit);
        Assert.Contains(".long-progress__fill", uiKit);
        Assert.Contains(".long-key-value__row", uiKit);
        Assert.Contains(".long-data-grid__row", uiKit);
    }

    [Fact]
    public void FileOrganizer_UsesReviewedHostOwnedOrganizationPlan()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "src", "FileOrganizer", "index.html"));
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "src", "FileOrganizer", "manifest.json")));

        Assert.DoesNotContain("<style", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", page);
        Assert.DoesNotContain("innerHTML", page);
        Assert.DoesNotContain("confirm(", page);
        Assert.Contains("long.fileSystem.planOrganization(", page);
        Assert.Contains("long.fileSystem.executeOrganization(", page);
        Assert.Contains("executeDialog.showModal()", page);
        Assert.Contains("LongUI?.onCommand", page);

        var capabilities = manifest.RootElement.GetProperty("capabilities")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();
        Assert.Contains("filesystem.advanced", capabilities);
        Assert.Contains("shell.selection", capabilities);
        Assert.DoesNotContain("file.ops", capabilities);
    }

    [Fact]
    public void PortManager_UsesRealHostApisAndControlledProcessTermination()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "src", "PortManager", "index.html"));
        var uiKit = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "WebAssets", "long-ui.css"));
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "src", "PortManager", "manifest.json")));

        Assert.DoesNotContain("<style", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", page);
        Assert.DoesNotContain("innerHTML", page);
        Assert.DoesNotContain("onclick=", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Math.random", page);
        Assert.DoesNotContain("模拟数据", page);
        Assert.DoesNotContain("confirm(", page);
        Assert.DoesNotContain("alert(", page);
        Assert.Contains("long.networkPort.getTcpListeners()", page);
        Assert.Contains("long.networkPort.getTcpConnections()", page);
        Assert.Contains("long.networkPort.getUdpEndpoints()", page);
        Assert.Contains("long.networkPort.findOwner(port, protocol)", page);
        Assert.Contains("long.clipboard.setText(text)", page);
        Assert.Contains("long.process.kill(target.id)", page);
        Assert.Contains("killDialog.showModal()", page);
        Assert.Contains("LongUI?.onCommand", page);
        Assert.Contains("visibilitychange", page);

        var capabilities = manifest.RootElement.GetProperty("capabilities")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();
        Assert.Equal(
            ["network.ports", "system.process", "system.clipboard"],
            capabilities);

        Assert.Contains(".long-dialog", uiKit);
        Assert.Contains(".long-badge--success", uiKit);
        Assert.Contains(".long-code", uiKit);
    }

    [Fact]
    public async Task Base64Commands_DeclareAndReturnTextResult()
    {
        var root = FindRepositoryRoot();
        var pluginDirectory = Path.Combine(root, "src", "Base64Tool");
        var result = await ManifestReader.ReadAsync(pluginDirectory);
        var page = File.ReadAllText(Path.Combine(pluginDirectory, "index.html"));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Manifest!.Commands.Count);
        Assert.All(result.Manifest.Commands, command =>
        {
            var output = Assert.Single(command.Outputs);
            Assert.Equal("result", output.Key);
            Assert.Equal(PluginCommandOutputType.Text, output.Type);
        });
        Assert.Contains("outputs: { result: { type: 'text', value: output.value } }", page);
        Assert.Contains("return command.command_id === 'base64.decode' ? decode() : encode();", page);
    }

    [Fact]
    public async Task UrlCommands_DeclareAndReturnBoundedTextResult()
    {
        var root = FindRepositoryRoot();
        var pluginDirectory = Path.Combine(root, "src", "UrlToolkit");
        var result = await ManifestReader.ReadAsync(pluginDirectory);
        var page = File.ReadAllText(Path.Combine(pluginDirectory, "index.html"));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Manifest!.Commands.Count);
        Assert.All(result.Manifest.Commands, command =>
        {
            var output = Assert.Single(command.Outputs);
            Assert.Equal("result", output.Key);
            Assert.Equal(PluginCommandOutputType.Text, output.Type);
        });
        Assert.Contains("outputs: { result: { type: 'text', value: output.value } }", page);
        Assert.Contains("output.value.length > 65536", page);
        Assert.Contains("return transform(command.command_id === 'url.decode'", page);
    }

    [Fact]
    public async Task JsonCommands_DeclareAndReturnBoundedTextResult()
    {
        var root = FindRepositoryRoot();
        var pluginDirectory = Path.Combine(root, "src", "JsonFormatterPlugin");
        var result = await ManifestReader.ReadAsync(pluginDirectory);
        var page = File.ReadAllText(Path.Combine(pluginDirectory, "index.html"));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Manifest!.Commands.Count);
        Assert.All(result.Manifest.Commands, command =>
        {
            var output = Assert.Single(command.Outputs);
            Assert.Equal("result", output.Key);
            Assert.Equal(PluginCommandOutputType.Text, output.Type);
        });
        Assert.Contains("outputs: { result: { type: 'text', value: result } }", page);
        Assert.Contains("result.length > 65536", page);
        Assert.Contains("return transform(command.command_id === 'json.minify'", page);
    }

    [Fact]
    public async Task TimestampCommand_DeclaresAndReturnsTextResult()
    {
        var root = FindRepositoryRoot();
        var pluginDirectory = Path.Combine(root, "src", "TimestampConverter");
        var result = await ManifestReader.ReadAsync(pluginDirectory);
        var page = File.ReadAllText(Path.Combine(pluginDirectory, "index.html"));

        Assert.True(result.IsSuccess, result.Error);
        var command = Assert.Single(result.Manifest!.Commands);
        var output = Assert.Single(command.Outputs);
        Assert.Equal("result", output.Key);
        Assert.Equal(PluginCommandOutputType.Text, output.Type);
        Assert.Contains("outputs: { result: { type: 'text', value: summary } }", page);
        Assert.Contains(
            "setStatus('status.invalid', 'danger', '无法识别当前时间')",
            page);
        Assert.Contains("return { success: false, message, outputs: {} }", page);
        Assert.Contains("return useNow();", page);
    }

    [Fact]
    public async Task UuidCommand_DeclaresAndReturnsParameterizedTextResult()
    {
        var root = FindRepositoryRoot();
        var pluginDirectory = Path.Combine(root, "src", "UuidGenerator");
        var result = await ManifestReader.ReadAsync(pluginDirectory);
        var page = File.ReadAllText(Path.Combine(pluginDirectory, "index.html"));

        Assert.True(result.IsSuccess, result.Error);
        var command = Assert.Single(result.Manifest!.Commands);
        var output = Assert.Single(command.Outputs);
        Assert.Equal("result", output.Key);
        Assert.Equal(PluginCommandOutputType.Text, output.Type);
        Assert.Contains("outputs: { result: { type: 'text', value: output.value } }", page);
        Assert.Contains("Number.parseInt(args.amount, 10)", page);
        Assert.Contains("args.uppercase === 'true'", page);
        Assert.Contains("args.compact === 'true'", page);
        Assert.Contains("applyCommandOptions(command); return generate();", page);
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
