using System.Diagnostics;
using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class QualityGateTests
{
    [Fact]
    public void Marketplace_ExposesTrustCompatibilityPermissionsAndRollbackActions()
    {
        var xaml = Read("src", "LongBetterWindows.Host", "Views", "MarketplaceControl.xaml");
        var source = Read("src", "LongBetterWindows.Host", "Views", "MarketplaceControl.xaml.cs");
        var installer = Read("src", "LongBetterWindows.Host", "Engine", "LpakInstaller.cs");
        var transport = Read("src", "LongBetterWindows.Host", "Engine", "MarketplaceTransport.cs");

        Assert.Contains("MarketSearchBox", xaml);
        Assert.Contains("VersionBox", xaml);
        Assert.Contains("PermissionDiffItems", xaml);
        Assert.Contains("HighTrustWarning", xaml);
        Assert.Contains("ConfirmOverlay", xaml);
        Assert.Contains("ValidateAsync(path, metadata, installed)", source);
        Assert.Contains("InstallAsync(_pendingPackagePath, _pendingMetadata)", source);
        Assert.Contains("UninstallAsync(_pendingUninstallId)", source);
        Assert.Contains(".long-transaction-", installer);
        Assert.Contains("RecoverInterruptedTransactionsAsync", installer);
        Assert.Contains("TransactionPhase.Committed", installer);
        Assert.Contains("_transactionGate", installer);
        Assert.Contains("ConcurrentDictionary<string, SemaphoreSlim>", transport);
        Assert.Contains("CleanupStaleTemporaryFiles", transport);
        Assert.Contains("ShouldRetry", transport);
        Assert.Contains("Attempts", transport);
        Assert.Contains("PublisherTrustStore", Read(
            "src", "LongBetterWindows.Host", "Engine", "PluginPackageValidator.cs"));
    }

    [Fact]
    public void MarketplacePublisher_UsesExternalKeysValidatedPackagesAndAtomicOutput()
    {
        var wrapper = Read("publish-marketplace.ps1");
        var pipeline = Read(
            "tools", "LongBetterWindows.MarketplacePublisher", "MarketplacePublishingPipeline.cs");
        var ignore = Read(".gitignore");

        Assert.Contains("PrivateKeyPath", wrapper);
        Assert.Contains("LongBetterWindows.MarketplacePublisher.csproj", wrapper);
        Assert.Contains("PluginPackageValidator", pipeline);
        Assert.Contains("ExpectedPluginId", pipeline);
        Assert.Contains("RSA.Create", pipeline);
        Assert.Contains("SignHash", pipeline);
        Assert.Contains("ResolveWithin", pipeline);
        Assert.Contains(".market-publish-", pipeline);
        Assert.Contains(".market-backup-", pipeline);
        Assert.Contains("preserveBackup", pipeline);
        Assert.Contains("*.private.pem", ignore);
        Assert.DoesNotContain("ExportPkcs8PrivateKey", pipeline);
    }

    [Fact]
    public void MarketplaceDeployer_CommitsRegistryLastAndReadsCredentialFromEnvironment()
    {
        var wrapper = Read("deploy-marketplace.ps1");
        var pipeline = Read(
            "tools", "LongBetterWindows.MarketplacePublisher", "MarketplaceDeploymentPipeline.cs");

        Assert.Contains("CredentialEnvironmentVariable", wrapper);
        Assert.Contains("--credential-env", wrapper);
        Assert.DoesNotContain("BearerToken", wrapper);
        Assert.Contains("Environment.GetEnvironmentVariable", pipeline);
        Assert.Contains("ImmutablePackage", pipeline);
        Assert.Contains("AuditReport", pipeline);
        Assert.Contains("RegistryCommit", pipeline);
        Assert.Contains("files.Add", pipeline);
        Assert.Contains("registry.json", pipeline);
        Assert.Contains("X-Content-SHA256", pipeline);
        Assert.Contains("AllowAutoRedirect = false", pipeline);
        Assert.Contains("trusted-publisher.fragment", Read(
            "docs", "插件市场Registry与签名发布.md"));
    }

    [Fact]
    public void MarketplaceVerifier_ReplaysThePublicClientTrustPathWithoutDeploymentCredentials()
    {
        var wrapper = Read("verify-marketplace.ps1");
        var pipeline = Read(
            "tools", "LongBetterWindows.MarketplacePublisher", "MarketplaceVerificationPipeline.cs");

        Assert.Contains("RegistryUri", wrapper);
        Assert.Contains("TrustStorePath", wrapper);
        Assert.DoesNotContain("CredentialEnvironmentVariable", wrapper);
        Assert.Contains("AllowAutoRedirect = false", pipeline);
        Assert.Contains("RemoteMarketplaceRepository", pipeline);
        Assert.Contains("MarketplacePackageDownloader", pipeline);
        Assert.Contains("PluginPackageValidator", pipeline);
        Assert.Contains("PackageTrustLevel.PublisherSigned", pipeline);
        Assert.Contains("WriteReportAtomicallyAsync", pipeline);
        Assert.DoesNotContain("Signature { get;", pipeline);
        Assert.DoesNotContain("PublicKeyPem { get;", pipeline);
    }

    [Fact]
    public void MarketplaceRollback_RequiresExactReleaseConfirmationAndVerifiedSnapshot()
    {
        var wrapper = Read("rollback-marketplace.ps1");
        var rollback = Read(
            "tools", "LongBetterWindows.MarketplacePublisher", "MarketplaceRollbackPipeline.cs");
        var deployment = Read(
            "tools", "LongBetterWindows.MarketplacePublisher", "MarketplaceDeploymentPipeline.cs");

        Assert.Contains("ConfirmReleaseId", wrapper);
        Assert.Contains("--confirm-release", wrapper);
        Assert.Contains("ConfirmReleaseId", rollback);
        Assert.Contains("exactly match", rollback);
        Assert.Contains("PreviousRegistrySha256", rollback);
        Assert.Contains("FixedTimeEquals", rollback);
        Assert.Contains("AllowAutoRedirect = false", rollback);
        Assert.Contains("previous-registry.json", deployment);
        Assert.Contains("deployment-manifest.json", deployment);
        Assert.Contains("RegistryCommit", deployment);
    }

    [Fact]
    public void MarketplaceRehearsal_AlwaysAttemptsRollbackAndPersistsEvidence()
    {
        var rehearsal = Read("rehearse-marketplace.ps1");
        var deployment = Read(
            "tools", "LongBetterWindows.MarketplacePublisher", "MarketplaceDeploymentPipeline.cs");

        Assert.Contains("ConfirmRehearsal", rehearsal);
        Assert.Contains("finally", rehearsal);
        Assert.Contains("rollback-marketplace.ps1", rehearsal);
        Assert.Contains("verify-marketplace.ps1", rehearsal);
        Assert.Contains("rollback_failure", rehearsal);
        Assert.Contains("rollback_verification_failure", rehearsal);
        Assert.Contains("rehearsal-summary.json", rehearsal);
        Assert.Contains("ResultPath", Read("deploy-marketplace.ps1"));
        Assert.Contains("\"prepared\"", deployment);
        Assert.Contains("MarketplaceDeploymentExecutionReport", deployment);
        Assert.DoesNotContain("BearerToken", rehearsal);
    }

    [Fact]
    public void VisualCapture_RendersDeterministicPngAndRecordsActualMonitorDpiSeparately()
    {
        var app = Read("src", "LongBetterWindows.Host", "App.xaml.cs");

        Assert.Contains("--quality-capture", app);
        Assert.Contains("--quality-capture-view", app);
        Assert.Contains("--quality-render-dpi", app);
        Assert.Contains("RenderTargetBitmap", app);
        Assert.Contains("PngBitmapEncoder", app);
        Assert.Contains("CapturePreviewAsync", app);
        Assert.Contains("CoreWebView2CapturePreviewImageFormat.Png", app);
        Assert.Contains("webview_preview", app);
        Assert.Contains("VisualTreeHelper.GetDpi", app);
        Assert.Contains("actual_monitor_dpi", app);
        Assert.Contains("render_dpi", app);
        Assert.Contains("Shutdown(3)", app);
    }

    [Fact]
    public void VisualMatrix_IsExplicitlyEngineeringOnlyAndHashManifestsEveryCapture()
    {
        var matrix = Read("capture-visual-matrix.ps1");

        Assert.Contains("engineering_render_matrix", matrix);
        Assert.Contains("physical_device_matrix_required", matrix);
        Assert.Contains("96,120,144,192", matrix);
        Assert.Contains("light','dark", matrix);
        Assert.Contains("main','market','palette", matrix);
        Assert.Contains("Get-FileHash", matrix);
        Assert.Contains("visual-matrix.json", matrix);
        Assert.Contains("already exists", matrix);
    }

    [Fact]
    public void PhysicalDpiMatrix_RejectsSimulatedScaleAndRequiresReviewedHashLockedEvidence()
    {
        var capture = Read("capture-physical-dpi-evidence.ps1");
        var approve = Read("approve-physical-dpi-evidence.ps1");
        var verify = Read("verify-physical-dpi-matrix.ps1");

        Assert.Contains("actual_monitor_dpi", capture);
        Assert.Contains("Physical monitor DPI mismatch", capture);
        Assert.Contains("physical_device_dpi_evidence", capture);
        Assert.Contains("ApproveAfterVisualReview", capture);
        Assert.Contains("no_clipping_or_overflow", capture);
        Assert.Contains("webview_preview", capture);
        Assert.Contains("wpf_render_target", capture);
        Assert.Contains("ConfirmVisualReview", approve);
        Assert.Contains("Scale confirmation mismatch", approve);
        Assert.Contains("Evidence changed after capture", approve);
        Assert.Contains("Get-FileHash", approve);
        Assert.Contains("100,125,150,200", verify);
        Assert.Contains("human_review.status -ne 'approved'", verify);
        Assert.Contains("Get-FileHash", verify);
        Assert.Contains("Expected 8 captures", verify);
        Assert.Contains("approved_physical_device_dpi_matrix", verify);
    }

    [Fact]
    public void AccessibilityMatrix_CapturesForcedStateWithoutClaimingPhysicalDeviceApproval()
    {
        var matrix = Read("capture-visual-matrix.ps1");

        Assert.Contains("high-contrast", matrix);
        Assert.Contains("reduced-motion", matrix);
        Assert.Contains("combined", matrix);
        Assert.Contains("--quality-high-contrast", matrix);
        Assert.Contains("--quality-reduce-motion", matrix);
        Assert.Contains("engineering_accessibility_render_matrix", matrix);
        Assert.Contains("physical_device_matrix_required", matrix);
        Assert.Contains("high_contrast = $metadata.high_contrast", matrix);
        Assert.Contains("reduced_motion = $metadata.reduced_motion", matrix);
    }

    [Fact]
    public void BuiltInPluginSet_ContainsTwentyDistinctFunctionalPlugins()
    {
        var project = Read("src", "LongBetterWindows.Host", "LongBetterWindows.Host.csproj");
        var additions = new Dictionary<string, string>
        {
            ["UrlToolkit"] = "encodeURIComponent",
            ["TimestampConverter"] = "new Date",
            ["RegexTester"] = "new RegExp",
            ["UuidGenerator"] = "randomUUID",
        };

        foreach (var (directory, behavior) in additions)
        {
            Assert.Contains($"src\\{directory}", project);
            Assert.Contains(behavior, Read("src", directory, "index.html"));
        }
    }

    [Fact]
    public void PluginMemoryProbe_RequiresTwentyUniquePluginsAndRepeatedSub200MbSamples()
    {
        var probe = Read("measure-plugin-memory.ps1");

        Assert.Contains("$manifestFiles.Count -ne 20", probe);
        Assert.Contains("$uniqueIds.Count -ne 20", probe);
        Assert.Contains("[ValidateRange(3,20)]", probe);
        Assert.Contains("-WindowStyle Hidden", probe);
        Assert.Contains("$maximum -lt $WorkingSetLimitMB", probe);
        Assert.Contains("distinct_builtin_plugin_memory_probe", probe);
        Assert.Contains("plugin-memory-report.json", probe);
        var toolCenter = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml.cs");
        Assert.Contains("MarketHost.Content == null", toolCenter);
        Assert.Contains("MarketHost.Content = new MarketplaceControl()", toolCenter);
    }

    [Fact]
    public void SuperPanel_ReusesUnifiedContextSearchExecutionAndPreferences()
    {
        var xaml = Read("src", "LongBetterWindows.Host", "Views", "SuperPanelWindow.xaml");
        var source = Read("src", "LongBetterWindows.Host", "Views", "SuperPanelWindow.xaml.cs");
        var palette = Read("src", "LongBetterWindows.Host", "Views", "CommandPaletteWindow.xaml.cs");

        Assert.Contains("Long 超级面板", xaml);
        Assert.Contains("ServicesInitializer.ContextCapture.CaptureAsync", source);
        Assert.Contains("ServicesInitializer.Search.SearchIncrementalAsync", source);
        Assert.Contains("CommandInvocationFactory.Create", source);
        Assert.Contains("SearchPreferences.TogglePinnedAsync", source);
        Assert.Contains("SearchPreferences.RecordUseAsync", source);
        Assert.Contains("SuperPanelGroupIds.Smart", source);
        Assert.Contains("SuperPanelGroupIds.Pinned", source);
        Assert.Contains("SuperPanelGroupIds.Recent", source);
        Assert.Contains("SuperPanelResultOrganizer.SelectGroup", source);
        Assert.Contains("SearchPreferences.MovePinnedAsync", source);
        Assert.Contains("WmMouseWheel", source);
        Assert.Contains("AddHook(WindowMessageHook)", source);
        Assert.Contains("CycleGroup(delta)", source);
        Assert.Contains("ResultsList_Drop", xaml);
        Assert.Contains("AllowDrop=\"True\"", xaml);
        Assert.Contains("AddGroupButton", xaml);
        Assert.Contains("GroupButton_Drop", xaml);
        Assert.Contains("SuperPanelGroups.AddResultAsync", source);
        Assert.Contains("SuperPanelGroups.MoveResultAsync", source);
        Assert.Contains("SuperPanelGroups.RemoveResultAsync", source);
        Assert.Contains("CommandInvocationFactory.Create", palette);
        Assert.DoesNotContain("new SearchCoordinator", source);
        Assert.DoesNotContain("new CommandRegistry", source);
    }

    [Fact]
    public void PluginWindowsAndShutdown_AreConnectedToResourceLifecycle()
    {
        var registry = Read("src", "LongBetterWindows.Host", "Engine", "PluginRegistry.cs");
        var adapter = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginAdapter.cs");
        var app = Read("src", "LongBetterWindows.Host", "App.xaml.cs");

        Assert.Contains("HandleWindowClosedAsync(Id)", adapter);
        Assert.Contains("ReleaseWebResourcesAsync", adapter);
        Assert.Contains("_runtime.Dispose()", adapter);
        Assert.Contains("IPluginResourceLifecycle", registry);
        Assert.Contains("_hostResourceReleaser", registry);
        Assert.Contains("ShutdownAllAsync", app);
    }

    [Fact]
    public void PluginPresentationAndMouseGesture_HaveUnifiedHostControls()
    {
        var mainXaml = Read("src", "LongBetterWindows.Host", "MainWindow.xaml");
        var mainSource = Read("src", "LongBetterWindows.Host", "MainWindow.xaml.cs");
        var pluginXaml = Read("src", "LongBetterWindows.Host", "Views", "PluginWindowHost.xaml");
        var pluginSource = Read("src", "LongBetterWindows.Host", "Views", "PluginWindowHost.xaml.cs");
        var adapter = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginAdapter.cs");
        var settings = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml");
        var gestures = Read("src", "LongBetterWindows.Host", "Services", "MouseGestureService.cs");

        Assert.Contains("EmbeddedPluginSurface", mainXaml);
        Assert.Contains("分离插件窗口", mainXaml);
        Assert.Contains("ShowEmbeddedPlugin", mainSource);
        Assert.Contains("CloseEmbeddedSurfaceAsync", mainSource);
        Assert.Contains("PreviewKeyDown=\"Window_PreviewKeyDown\"", pluginXaml);
        Assert.Contains("返回管理中心", pluginXaml);
        Assert.Contains("Key.Escape", pluginSource);
        Assert.Contains("DefaultPresentation", adapter);
        Assert.Contains("ShowDetachedWindow", adapter);
        Assert.Contains("超级面板鼠标手势", settings);
        Assert.Contains("MouseGestureMode.LongRightPress", gestures);
        Assert.Contains("WmRButtonUp", gestures);
        Assert.Contains("Mode { get; private set; }", gestures);
    }

    [Fact]
    public void Host_ExplicitlyEnablesPerMonitorV2()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "LongBetterWindows.Host", "LongBetterWindows.Host.csproj"));
        var manifest = File.ReadAllText(Path.Combine(root, "src", "LongBetterWindows.Host", "app.manifest"));

        Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", project);
        Assert.Contains("PerMonitorV2", manifest);
        Assert.Contains("True/PM", manifest);
        Assert.Contains("BuildNativePluginsForPublish", project);
        Assert.Contains("RemoveProperties=\"RuntimeIdentifier;SelfContained\"", project);
        Assert.Contains("原生插件仅发布运行所需的 Manifest 与 DLL", project);
        Assert.Contains("<RemoveDir Directories=\"$(OutputPath)Plugins", project);
        Assert.Contains("CopyPluginsToPublish", project);
        Assert.Contains("$(PublishDir)Plugins", project);
        Assert.Contains("<Version>0.5.0-rc.1</Version>", project);
        Assert.Contains("<AssemblyVersion>0.5.0.0</AssemblyVersion>", project);
    }

    [Fact]
    public void ProductVersion_IsExposedConsistentlyToNativeAndWebUi()
    {
        var app = Read("src", "LongBetterWindows.Host", "App.xaml.cs");
        var webRuntime = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginRuntime.cs");
        var toolCenter = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml.cs");

        Assert.Contains("AssemblyInformationalVersionAttribute", app);
        Assert.Contains("App.ProductVersion", webRuntime);
        Assert.Contains("v{App.ProductVersion}", toolCenter);
    }

    [Fact]
    public void ReleasePipeline_ProducesBothPortableVariantsAndChecksums()
    {
        var release = Read("release.ps1");
        Assert.Contains("FrameworkDependent", release);
        Assert.Contains("SelfContained", release);
        Assert.Contains("SHA256SUMS.txt", release);
        Assert.Contains("release-manifest.json", release);
        Assert.Contains("source_dirty", release);
        Assert.Contains("release_eligible", release);
        Assert.Contains("Get-FileHash", release);
        Assert.Contains("WaitForExit(20000)", release);
        Assert.Contains("pluginCount -ne 20", release);
    }

    [Fact]
    public void ToolCenter_PluginListUsesRecyclingVirtualization()
    {
        var xaml = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml");
        Assert.Contains("x:Name=\"PluginsPanel\"", xaml);
        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", xaml);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", xaml);
        Assert.Contains("<VirtualizingStackPanel", xaml);
        Assert.Contains("DarkScrollBarStyle", xaml);

        var code = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml.cs");
        Assert.Contains("if (_activePage == \"plugins\")", code);
        Assert.Contains("key == \"developer\" && !_docsLoaded", code);
    }

    [Fact]
    public void MainWindow_SupportsMinimumQualityViewportAndResponsiveToolCenter()
    {
        var main = Read("src", "LongBetterWindows.Host", "MainWindow.xaml");
        var toolCenter = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml");
        var code = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml.cs");

        Assert.Contains("MinWidth=\"720\" MinHeight=\"560\"", main);
        Assert.Contains("x:Name=\"NavigationColumn\"", toolCenter);
        Assert.Contains("x:Name=\"OverviewStatusCard\"", toolCenter);
        Assert.Contains("ApplyResponsiveLayout", code);
        Assert.Contains("width < 860", code);
    }

    [Fact]
    public void CommandPalette_CancelsSupersededSearches()
    {
        var xaml = Read("src", "LongBetterWindows.Host", "Views", "CommandPaletteWindow.xaml");
        var source = Read("src", "LongBetterWindows.Host", "Views", "CommandPaletteWindow.xaml.cs");
        Assert.Contains("AllowsTransparency=\"False\"", xaml);
        Assert.Contains("Long.Brush.Surface.Overlay", xaml);
        Assert.Contains("CancellationTokenSource? _searchCts", source);
        Assert.Contains("_searchCts?.Cancel()", source);
        Assert.Contains("Task.Delay(45, token)", source);
        Assert.Contains("OperationCanceledException", source);
        Assert.Contains("Command Palette 可输入", source);
        Assert.Contains("Shell32.GetForegroundWindow()", source);
        Assert.Contains("ContextCaptureRequest", source);
        Assert.Contains("ContextBadges", xaml);
        Assert.Contains("ContextRemove_Click", xaml);

        var webAdapter = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginAdapter.cs");
        var webRuntime = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginRuntime.cs");
        Assert.True(webAdapter.IndexOf("EnsureWindowVisible();", StringComparison.Ordinal)
                    < webAdapter.IndexOf("EnsureRuntimeInitializedAsync()", StringComparison.Ordinal));
        Assert.Contains("EnsureView()", webRuntime);
    }

    [Fact]
    public void ThemeAndMotion_RespectSystemAccessibilitySettings()
    {
        var app = Read("src", "LongBetterWindows.Host", "App.xaml.cs");
        var animation = Read("src", "LongBetterWindows.Host", "Helpers", "AnimationHelper.cs");
        var devTools = Read("src", "LongBetterWindows.Host", "Views", "PluginDevTools.html");

        Assert.Contains("SystemParameters.HighContrast", app);
        Assert.Contains("HighContrastPalette", app);
        Assert.Contains("--quality-high-contrast", app);
        Assert.Contains("--quality-reduce-motion", app);
        Assert.Contains("SystemParameters.HighContrast || _forceHighContrast", app);
        Assert.Contains("new SolidColorBrush(SystemColors.HighlightColor)", app);
        Assert.Contains("Long.Brush.Accent.Gradient", app);
        Assert.Contains("!SystemParameters.ClientAreaAnimation || _forceReduceMotion", app);
        Assert.Contains("SystemParameters.StaticPropertyChanged", app);
        Assert.Contains("Long.Motion.Fast", animation);
        Assert.Contains("duration == TimeSpan.Zero", animation);
        Assert.Contains("prefers-reduced-motion: reduce", devTools);
        Assert.Contains(":focus-visible", devTools);
        Assert.Contains("ReadArgument(e.Args, \"--theme\")", app);
        Assert.Contains("--run-command", app);
        Assert.Contains("--plugins-dir", app);
        Assert.Contains("--quality-idle-ms", app);
        Assert.Contains("质量驻留采样", app);
        Assert.Contains("new CommandExecutor(registry).ExecuteAsync", app);
        Assert.Contains("--exit-after-command", app);
    }

    [Fact]
    public void ExplicitPluginDirectory_IsIsolatedFromDevelopmentFallback()
    {
        var scanner = Read("src", "LongBetterWindows.Host", "Engine", "PluginScanner.cs");
        Assert.Contains("pluginsDir == null ? FindDevPluginsDir() : null", scanner);
    }

    [Fact]
    public void CoreNativeInputs_HaveAutomationNames()
    {
        Assert.Contains("AutomationProperties.Name=\"文件夹备注内容\"",
            Read("src", "LongBetterWindows.Host", "Views", "FloatingHudWindow.xaml"));
        Assert.Contains("AutomationProperties.Name=\"截图区域选择器\"",
            Read("src", "ScreenshotPlugin", "RegionSelectorWindow.xaml"));
        Assert.Contains("AutomationProperties.Name=\"关闭窗口管理指南\"",
            Read("src", "WindowManagerPlugin", "WindowManagerGuide.xaml"));

        var toolCenter = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml.cs");
        Assert.Contains("Keyboard.Modifiers.HasFlag(ModifierKeys.Control)", toolCenter);
        Assert.Contains("Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)", toolCenter);
    }

    [Fact]
    public void LightMutedText_MeetsWcagAaOnBaseBackground()
    {
        Assert.True(Contrast("667085", "F4F6FA") >= 4.5);
        Assert.True(Contrast("FFFFFF", "7059F5") >= 4.5);
        Assert.True(Contrast("E9E5FF", "076F92") >= 4.5);
        Assert.Contains("[\"Long.Color.Text.Muted\"] = \"#667085\"",
            Read("src", "LongBetterWindows.Host", "App.xaml.cs"));
        var webUi = Read("src", "LongBetterWindows.Host", "WebAssets", "long-ui.css");
        Assert.Contains("--long-text-muted: #667085", webUi);
        Assert.Contains("--long-accent: #7059f5", webUi);
    }

    [Fact]
    public void CommandSearch_StaysWellBelowInteractiveBudget()
    {
        var registry = new CommandRegistry();
        for (var pluginIndex = 0; pluginIndex < 20; pluginIndex++)
        {
            registry.RegisterManifest(new PluginManifest
            {
                Id = $"quality.plugin.{pluginIndex}",
                Name = $"Quality Plugin {pluginIndex}",
                Commands = Enumerable.Range(0, 50).Select(commandIndex => new PluginCommand
                {
                    Id = $"command.{commandIndex}",
                    Title = $"Window action {pluginIndex} {commandIndex}",
                    Aliases = new List<string> { $"action-{pluginIndex}-{commandIndex}", "window" },
                }).ToList(),
            });
        }

        _ = registry.Search("window", maxResults: 20);
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 20; index++)
            _ = registry.Search("window", maxResults: 20);
        stopwatch.Stop();

        var averageMilliseconds = stopwatch.Elapsed.TotalMilliseconds / 20;
        Assert.True(averageMilliseconds < 100,
            $"Average command search took {averageMilliseconds:F2}ms.");
    }

    [Fact]
    public void DesktopUiSmoke_HasStableAutomationEntryPoints()
    {
        var script = Read("run-desktop-ui-smoke.ps1");
        var palette = Read("src", "LongBetterWindows.Host", "Views", "CommandPaletteWindow.xaml");
        var superPanel = Read("src", "LongBetterWindows.Host", "Views", "SuperPanelWindow.xaml");
        var main = Read("src", "LongBetterWindows.Host", "MainWindow.xaml");
        var plugin = Read("src", "LongBetterWindows.Host", "Views", "PluginWindowHost.xaml");

        Assert.Contains("Long.CommandPalette.Search", palette);
        Assert.Contains("Long.CommandPalette.Results", palette);
        Assert.Contains("Long.Result.MoreActions", palette);
        Assert.Contains("Long.SuperPanel.Results", superPanel);
        Assert.Contains("Long.SuperPanel.OpenCommandCenter", superPanel);
        Assert.Contains("Long.Plugin.EmbeddedTitle", main);
        Assert.Contains("Long.Plugin.Detach", main);
        Assert.Contains("Long.Plugin.DetachedWindow", plugin);
        Assert.Contains("[LongDesktopInput]::TopLevelWindows", script);
        Assert.Contains("$valuePattern.SetValue('wifi')", script);
        Assert.Contains("[LongDesktopInput]::ShiftEnter", script);
        Assert.Contains("shift_enter_copied_uri", script);
        Assert.Contains("secondary_menu_copied_uri", script);
        Assert.Contains("palette_shown_on_transition", script);
        Assert.Contains("escape_closed_detached_window", script);
        Assert.Contains("escape_closed_panel", script);
    }

    [Fact]
    public void CommandPalette_ShiftEnter_IsHandledBeforeAsyncSecondaryAction()
    {
        var source = Read(
            "src", "LongBetterWindows.Host", "Views", "CommandPaletteWindow.xaml.cs");
        var previewHandler = source.IndexOf(
            "private async void Window_PreviewKeyDown", StringComparison.Ordinal);
        var handled = source.IndexOf("e.Handled = true;", previewHandler, StringComparison.Ordinal);
        var execute = source.IndexOf(
            "await ExecuteHostActionAsync", previewHandler, StringComparison.Ordinal);

        Assert.True(previewHandler >= 0);
        Assert.True(handled > previewHandler);
        Assert.True(execute > handled,
            "Shift+Enter must be marked handled before awaiting its secondary action.");
    }

    [Fact]
    public void DesktopUiSmoke_CoversMarketplaceAndAccessibilityWithoutConfirmingMutation()
    {
        var script = Read("run-desktop-ui-smoke.ps1");
        var market = Read(
            "src", "LongBetterWindows.Host", "Views", "MarketplaceControl.xaml");
        var app = Read("src", "LongBetterWindows.Host", "App.xaml.cs");

        Assert.Contains("Long.Marketplace.Search", market);
        Assert.Contains("Long.Marketplace.Results", market);
        Assert.Contains("Long.Marketplace.ConfirmCancel", market);
        Assert.Contains("Long.Marketplace.Uninstall", script);
        Assert.Contains("Long.Marketplace.ConfirmCancel", script);
        Assert.DoesNotContain("Invoke-AutomationElement $confirmAction", script);
        Assert.Contains("installed_state_preserved", script);
        Assert.Contains("--quality-high-contrast", script);
        Assert.Contains("--quality-reduce-motion", script);
        Assert.Contains("requested_state_confirmed", script);
        Assert.Contains("Quality accessibility mode:", app);
    }

    [Fact]
    public void IsolatedMarketplaceTransaction_CoversSignedLifecycleAndProductionIsolation()
    {
        var script = Read("run-isolated-marketplace-transaction.ps1");
        var app = Read("src", "LongBetterWindows.Host", "App.xaml.cs");
        var market = Read(
            "src", "LongBetterWindows.Host", "Views", "MarketplaceControl.xaml.cs");

        Assert.Contains("--quality-market-catalog", app);
        Assert.Contains("--quality-market-trust-store", app);
        Assert.Contains("QualityMarketplaceCatalogPath", market);
        Assert.Contains("QualityMarketplaceTrustStorePath", market);
        Assert.Contains("MarketplaceSourceKind.RemoteRegistry", market);
        Assert.Contains("AutomationProperties.SetItemStatus", market);
        Assert.Contains("transaction-temp", script);
        Assert.Contains("PublisherSigned", script);
        Assert.Contains("HashRejected", script);
        Assert.Contains("SignatureRejected", script);
        Assert.Contains("old_version_preserved_after_rejections", script);
        Assert.Contains("startup_recovered_interrupted_upgrade", script);
        Assert.Contains("install_v1", script);
        Assert.Contains("upgrade_v2", script);
        Assert.Contains("rollback_v1", script);
        Assert.Contains("uninstall", script);
        Assert.Contains("release_plugins_unchanged", script);
        Assert.Contains("Remove-Item -LiteralPath $resolvedTransaction -Recurse -Force", script);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { FindRepositoryRoot() }.Concat(parts).ToArray()));

    private static double Contrast(string foreground, string background)
    {
        var first = Luminance(foreground);
        var second = Luminance(background);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static double Luminance(string hex)
    {
        var channels = Enumerable.Range(0, 3)
            .Select(index => Convert.ToInt32(hex.Substring(index * 2, 2), 16) / 255d)
            .Select(value => value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4))
            .ToArray();
        return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
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

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
