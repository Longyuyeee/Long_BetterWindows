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
        var session = Read(
            "src", "LongBetterWindows.Host", "Interaction", "MarketplaceSessionCoordinator.cs");

        Assert.Contains("MarketSearchBox", xaml);
        Assert.Contains("VersionBox", xaml);
        Assert.Contains("PermissionDiffItems", xaml);
        Assert.Contains("HighTrustWarning", xaml);
        Assert.Contains("ConfirmOverlay", xaml);
        Assert.Contains("_session.PrepareLocalPackageAsync", source);
        Assert.Contains("installer.InstallAsync(pending.PackagePath!, pending.Metadata)", source);
        Assert.Contains("installer.UninstallAsync(pending.PluginId!)", source);
        Assert.Contains("runtime.ValidatePackageAsync", session);
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
        Assert.Contains("preflight-dry-run.json", rehearsal);
        Assert.Contains("baseline-verification.json", rehearsal);
        Assert.Contains("preflight_dry_run_verified", rehearsal);
        Assert.Contains("baseline_verified", rehearsal);
        Assert.Contains("PreflightOnly", rehearsal);
        Assert.Contains("if (-not $PreflightOnly -and -not $ConfirmRehearsal)", rehearsal);
        Assert.Contains("if ($PreflightOnly)", rehearsal);
        Assert.Contains("deployment_started", rehearsal);
        Assert.True(
            rehearsal.IndexOf("$summary.preflight_dry_run_verified = $true", StringComparison.Ordinal)
            < rehearsal.IndexOf("$summary.deployment_started = $true", StringComparison.Ordinal));
        Assert.True(
            rehearsal.IndexOf("$summary.baseline_verified = $true", StringComparison.Ordinal)
            < rehearsal.IndexOf("$summary.deployment_started = $true", StringComparison.Ordinal));
        Assert.Contains("ResultPath", Read("deploy-marketplace.ps1"));
        Assert.Contains("\"prepared\"", deployment);
        Assert.Contains("MarketplaceDeploymentExecutionReport", deployment);
        Assert.DoesNotContain("BearerToken", rehearsal);
    }

    [Fact]
    public void VisualCapture_RendersDeterministicPngAndRecordsActualMonitorDpiSeparately()
    {
        var app = Read("src", "LongBetterWindows.Host", "App.xaml.cs");
        var options = Read("src", "LongBetterWindows.Host", "Services", "AppStartupOptions.cs");
        var quality = Read("src", "LongBetterWindows.Host", "Services", "QualityRuntimeService.cs");

        Assert.Contains("--quality-capture", options);
        Assert.Contains("--quality-capture-view", options);
        Assert.Contains("--quality-render-dpi", options);
        Assert.Contains("RenderTargetBitmap", quality);
        Assert.Contains("PngBitmapEncoder", quality);
        Assert.Contains("CapturePreviewAsync", quality);
        Assert.Contains("CoreWebView2CapturePreviewImageFormat.Png", quality);
        Assert.Contains("webview_preview", quality);
        Assert.Contains("VisualTreeHelper.GetDpi", quality);
        Assert.Contains("actual_monitor_dpi", quality);
        Assert.Contains("render_dpi", quality);
        Assert.Contains("_qualityRuntime!.CaptureAsync", app);
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
    public void PhysicalAccessibilityEvidence_RequiresRealSettingsManualReviewAndScreenReaderApproval()
    {
        var capture = Read("capture-accessibility-evidence.ps1");
        var approve = Read("approve-accessibility-evidence.ps1");
        var verify = Read("verify-accessibility-matrix.ps1");

        Assert.Contains("SystemParameters]::HighContrast", capture);
        Assert.Contains("SystemParameters]::ClientAreaAnimation", capture);
        Assert.Contains("Requested screen reader process is not running", capture);
        Assert.Contains("run-desktop-ui-smoke.ps1", capture);
        Assert.Contains("physical_accessibility_evidence", capture);
        Assert.Contains("ConfirmKeyboardNavigation", approve);
        Assert.Contains("ConfirmFocusVisibility", approve);
        Assert.Contains("ConfirmMotionBehavior", approve);
        Assert.Contains("ConfirmScreenReaderAnnouncements", approve);
        Assert.Contains("evidence changed after capture", approve, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("high_contrast", verify);
        Assert.Contains("reduced_motion", verify);
        Assert.Contains("combined", verify);
        Assert.Contains("at least one approved Narrator or NVDA", verify);
        Assert.Contains("approved_physical_accessibility_matrix", verify);
    }

    [Fact]
    public void CleanWindowsReleaseEvidence_UsesCandidatePackageAndRequiresIndependentLifecycleApproval()
    {
        var capture = Read("capture-clean-environment-evidence.ps1");
        var approve = Read("approve-clean-environment-evidence.ps1");
        var verify = Read("verify-clean-environment-evidence.ps1");
        var desktopSmoke = Read("run-desktop-ui-smoke.ps1");

        Assert.Contains("ConfirmCleanUserEnvironment", capture);
        Assert.Contains("Release ZIP hash does not match", capture);
        Assert.Contains("Start capture before the first launch", capture);
        Assert.Contains("-ReleaseDirectory $installRoot", capture);
        Assert.Contains("clean_windows_release_evidence", capture);
        Assert.Contains("ConfirmTrayIcon", approve);
        Assert.Contains("ConfirmGlobalHotkey", approve);
        Assert.Contains("ConfirmWebViewRuntime", approve);
        Assert.Contains("ConfirmParallelUpgradeDataPreserved", approve);
        Assert.Contains("ConfirmRollbackToPreviousVersion", approve);
        Assert.Contains("ConfirmUninstallIntegrationsRemoved", approve);
        Assert.Contains("Reviewer must differ", approve);
        Assert.Contains("evidence changed after capture", approve, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("human_review.status -ne 'approved'", verify);
        Assert.Contains("signed, release-eligible package", verify);
        Assert.Contains("Manual lifecycle checklist is incomplete", verify);
        Assert.Contains("approved_clean_windows_release_gate", verify);
        Assert.Contains("ReleaseDirectory", desktopSmoke);
        Assert.Contains("Plugins directory was not found", desktopSmoke);
    }

    [Fact]
    public void WindowsCodeSigning_UsesProtectedCertificateStoreTimestampAndIndependentZipVerification()
    {
        var sign = Read("sign-release.ps1");
        var verify = Read("verify-signed-release.ps1");

        Assert.Contains("ConfirmSign", sign);
        Assert.Contains("ExpectedSourceCommit", sign);
        Assert.Contains("ExpectedSourceCommit must be a full 40-character Git commit SHA", sign);
        Assert.Contains("Candidate source commit does not match ExpectedSourceCommit", sign);
        Assert.Contains("source_commit = $expectedCommit", sign);
        Assert.Contains("Code signing requires a candidate rebuilt from a clean source commit", sign);
        Assert.Contains("Cert:\\$CertificateStoreLocation\\My\\$thumbprint", sign);
        Assert.DoesNotContain("PfxPassword", sign, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.3.6.1.5.5.7.3.3", sign);
        Assert.Contains("/fd','SHA256'", sign);
        Assert.Contains("/tr',$TimestampUrl.AbsoluteUri", sign);
        Assert.Contains("/td','SHA256'", sign);
        Assert.Contains(".long-signing-", sign);
        Assert.Contains("Resolve-Within", sign);
        Assert.Contains("must not contain reparse points", sign);
        Assert.Contains("escapes release root", sign);
        Assert.Contains("Move-Item -LiteralPath $stagingRoot -Destination $outputRoot", sign);
        Assert.Contains("release_eligible = $true", sign);
        Assert.Contains("Expand-Archive", verify);
        Assert.Contains("Resolve-Within", verify);
        Assert.Contains("escapes release root", verify);
        Assert.Contains("Get-AuthenticodeSignature", verify);
        Assert.Contains("verify /pa /all /tw", verify);
        Assert.Contains("Signed file count mismatch", verify);
        Assert.Contains("verified_windows_authenticode_release", verify);
        Assert.Contains("ExpectedSourceCommit", verify);
        Assert.Contains("Signed release source commit does not match ExpectedSourceCommit", verify);
        Assert.Contains("source_commit = $expectedCommit", verify);
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
    public void BuiltInPluginSet_ContainsTwentyFiveDistinctFunctionalPlugins()
    {
        var root = FindRepositoryRoot();
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

        var pluginDirectories = Directory.GetDirectories(Path.Combine(root, "src"))
            .Where(directory => File.Exists(Path.Combine(directory, "manifest.json")))
            .ToArray();
        Assert.Equal(25, pluginDirectories.Length);
        foreach (var directory in pluginDirectories)
            Assert.Contains($"src\\{Path.GetFileName(directory)}", project);
    }

    [Fact]
    public void PluginMemoryProbe_RequiresTwentyFiveUniquePluginsAndRepeatedSub200MbSamples()
    {
        var probe = Read("measure-plugin-memory.ps1");

        Assert.Contains("$manifestFiles.Count -ne 25", probe);
        Assert.Contains("$uniqueIds.Count -ne 25", probe);
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
    public void Marketplace_UsesOnlyTheTrustedDistributionPipeline()
    {
        var root = FindRepositoryRoot();
        var host = Path.Combine(root, "src", "LongBetterWindows.Host");
        var retiredFiles = new[]
        {
            Path.Combine(host, "Contracts", "MarketPlugin.cs"),
            Path.Combine(host, "Services", "LpakInstallerService.cs"),
            Path.Combine(host, "Services", "MarketApiService.cs"),
            Path.Combine(host, "Services", "PluginInstallService.cs"),
            Path.Combine(host, "Services", "PluginUpdateService.cs"),
            Path.Combine(host, "Views", "MarketPanel.xaml"),
            Path.Combine(host, "Views", "MarketPanel.xaml.cs"),
        };

        Assert.All(retiredFiles, path => Assert.False(File.Exists(path), path));
        Assert.True(File.Exists(Path.Combine(host, "Views", "MarketplaceControl.xaml")));
        Assert.True(File.Exists(Path.Combine(host, "Engine", "MarketplaceRepository.cs")));
        Assert.True(File.Exists(Path.Combine(host, "Engine", "MarketplaceTransport.cs")));
        Assert.True(File.Exists(Path.Combine(host, "Engine", "LpakInstaller.cs")));
    }

    [Fact]
    public void MarketplaceView_DelegatesSessionStateAndRuntimeLifetimeToCoordinators()
    {
        var view = Read(
            "src", "LongBetterWindows.Host", "Views", "MarketplaceControl.xaml.cs");
        var runtime = Read(
            "src", "LongBetterWindows.Host", "Services", "MarketplaceRuntimeService.cs");
        var presentation = Read(
            "src", "LongBetterWindows.Host", "Interaction", "MarketplacePresentation.cs");
        var session = Read(
            "src", "LongBetterWindows.Host", "Interaction", "MarketplaceSessionCoordinator.cs");

        Assert.Contains("new MarketplaceRuntimeService", view);
        Assert.Contains("new MarketplaceSessionCoordinator", view);
        Assert.Contains("_session.LoadCatalogAsync", view);
        Assert.Contains("_session.PrepareLocalPackageAsync", view);
        Assert.Contains("_session.PrepareRemotePackageAsync", view);
        Assert.Contains("_session.ExecutePendingAsync", view);
        Assert.Contains("MarketplaceControl_Unloaded", view);
        Assert.DoesNotContain("_pendingPackagePath", view);
        Assert.DoesNotContain("_pendingUninstallId", view);
        Assert.DoesNotContain("_pendingValidation", view);
        Assert.DoesNotContain("new HttpClient", view);
        Assert.DoesNotContain("new RemoteMarketplaceRepository", view);
        Assert.DoesNotContain("new MarketplacePackageDownloader", view);
        Assert.Contains("MarketplacePresentation.ProjectEntries", view);
        Assert.Contains("MarketplacePresentation.GetCompatibility", view);
        Assert.Contains("MarketplacePresentation.CreatePackageMetadata", view);
        Assert.DoesNotContain("class MarketCardModel", view);
        Assert.Contains("new RemoteMarketplaceRepository", runtime);
        Assert.Contains("new MarketplacePackageDownloader", runtime);
        Assert.Contains("MarketplaceConfigurationLoader.LoadTrustStoreAsync", runtime);
        Assert.Contains("if (_ownsHttpClient) _httpClient.Dispose()", runtime);
        Assert.Contains("runtime.LoadCatalogAsync", session);
        Assert.Contains("runtime.ValidatePackageAsync", session);
        Assert.Contains("runtime.DownloadPackageAsync", session);
        Assert.Contains("_catalogLoad?.Cancel()", session);
        Assert.Contains("return MarketplacePreparationResult.Busy()", session);
        Assert.Contains("if (result.IsSuccess", session);
        Assert.Contains("class MarketCardModel", presentation);
        Assert.Contains("MarketplaceCatalogCodec.Search", presentation);
    }

    [Fact]
    public void SuperPanel_ReusesUnifiedContextSearchExecutionAndPreferences()
    {
        var xaml = Read("src", "LongBetterWindows.Host", "Views", "SuperPanelWindow.xaml");
        var source = Read("src", "LongBetterWindows.Host", "Views", "SuperPanelWindow.xaml.cs");
        var groups = Read(
            "src", "LongBetterWindows.Host", "Interaction", "SuperPanelGroupCoordinator.cs");
        var session = Read(
            "src", "LongBetterWindows.Host", "Interaction", "SuperPanelSearchSession.cs");
        var actions = Read(
            "src", "LongBetterWindows.Host", "Interaction", "SuperPanelActionCoordinator.cs");
        var drag = Read(
            "src", "LongBetterWindows.Host", "Interaction", "SuperPanelDragSession.cs");
        var keyboard = Read(
            "src", "LongBetterWindows.Host", "Interaction", "SuperPanelKeyboardRouter.cs");
        var lifecycle = Read(
            "src", "LongBetterWindows.Host", "Views", "SuperPanelWindowLifecycle.cs");
        var editor = Read(
            "src", "LongBetterWindows.Host", "Interaction", "SuperPanelGroupEditorSession.cs");
        var menu = Read(
            "src", "LongBetterWindows.Host", "Interaction", "SearchResultActionMenuProjection.cs");
        var projection = Read(
            "src", "LongBetterWindows.Host", "Interaction", "SuperPanelViewProjection.cs");
        var palette = Read("src", "LongBetterWindows.Host", "Views", "CommandPaletteWindow.xaml.cs");

        Assert.Contains("Long 超级面板", xaml);
        Assert.Contains("new SuperPanelSearchSession", source);
        Assert.Contains("_contextCapture.CaptureAsync", session);
        Assert.Contains("_search.SearchIncrementalAsync", session);
        Assert.Contains("new SuperPanelActionCoordinator", source);
        Assert.Contains("_actionCoordinator.ExecuteAsync", source);
        Assert.Contains("SearchResultActionExecutor", actions);
        Assert.Contains("RecordUseAsync", actions);
        Assert.Contains("_groupCoordinator.TogglePinnedAsync", source);
        Assert.DoesNotContain("new CommandExecutor", source);
        Assert.Contains("SuperPanelGroupIds.Smart", groups);
        Assert.Contains("SuperPanelGroupIds.Pinned", groups);
        Assert.Contains("SuperPanelGroupIds.Recent", groups);
        Assert.Contains("SuperPanelResultOrganizer.SelectGroup", groups);
        Assert.Contains("new SuperPanelGroupCoordinator", source);
        Assert.Contains("_groupCoordinator.BuildView()", source);
        Assert.DoesNotContain("IReadOnlyList<SearchResultItem> _allResults", source);
        Assert.DoesNotContain("string _activeGroupId", source);
        Assert.DoesNotContain("CancellationTokenSource? _loadCts", source);
        Assert.DoesNotContain("CancellationTokenSource? _searchCts", source);
        Assert.Contains("_preferences.MovePinnedAsync", groups);
        Assert.Contains("_groups.MoveResultAsync", groups);
        Assert.Contains("_groups.AddResultAsync", groups);
        Assert.Contains("_groups.RemoveResultAsync", groups);
        Assert.DoesNotContain("SuperPanelGroups.AddResultAsync", source);
        Assert.DoesNotContain("SuperPanelGroups.MoveResultAsync", source);
        Assert.DoesNotContain("SuperPanelGroups.RemoveResultAsync", source);
        Assert.DoesNotContain("SuperPanelGroups.CreateAsync", source);
        Assert.DoesNotContain("SuperPanelGroups.RenameAsync", source);
        Assert.DoesNotContain("SuperPanelGroups.DeleteAsync", source);
        Assert.Contains("_windowLifecycle.AttachWindowMessageHook", source);
        Assert.Contains("_windowLifecycle.Present", source);
        Assert.Contains("WmMouseWheel", lifecycle);
        Assert.Contains("AddHook(WindowMessageHook)", lifecycle);
        Assert.Contains("CalculatePosition", lifecycle);
        Assert.Contains("Shell32.SetForegroundWindow", lifecycle);
        Assert.DoesNotContain("HwndSource? _windowSource", source);
        Assert.Contains("_cycleGroup(delta)", lifecycle);
        Assert.Contains("_dragSession.TryBegin", source);
        Assert.Contains("_dragSession.TryStartDrag", source);
        Assert.Contains("SuperPanelKeyboardRouter.Resolve", source);
        Assert.Contains("minimumHorizontalDistance", drag);
        Assert.Contains("SuperPanelKeyboardCommand.ExecuteSecondary", keyboard);
        Assert.Contains("new SuperPanelGroupEditorSession", source);
        Assert.Contains("SearchResultActionMenuProjection.Build", source);
        Assert.Contains("SuperPanelViewProjection.ProjectContext", source);
        Assert.Contains("SuperPanelViewProjection.ProjectAction", source);
        Assert.Contains("SuperPanelActionDisposition.ContinueSearch", projection);
        Assert.DoesNotContain("outcome.IsSuccess && !outcome.KeepPanelOpen", source);
        Assert.Contains("SuperPanelGroupEditorState.Closed", editor);
        Assert.Contains("Long.Result.SecondaryAction.{index}", menu);
        Assert.DoesNotContain("SearchResultItem? _dragCandidate", source);
        Assert.DoesNotContain("bool _suppressClick", source);
        Assert.DoesNotContain("string? _editingGroupId", source);
        Assert.Contains("ResultsList_Drop", xaml);
        Assert.Contains("AllowDrop=\"True\"", xaml);
        Assert.Contains("AddGroupButton", xaml);
        Assert.Contains("GroupButton_Drop", xaml);
        Assert.Contains("CommandInvocationFactory.Create", palette);
        Assert.DoesNotContain("new SearchCoordinator", source);
        Assert.DoesNotContain("new CommandRegistry", source);
    }

    [Fact]
    public void PluginWindowsAndShutdown_AreConnectedToResourceLifecycle()
    {
        var registry = Read("src", "LongBetterWindows.Host", "Engine", "PluginRegistry.cs");
        var adapter = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginAdapter.cs");
        var presentation = Read(
            "src", "LongBetterWindows.Host", "Engine", "WebPluginPresentationCoordinator.cs");
        var app = Read("src", "LongBetterWindows.Host", "App.xaml.cs");

        Assert.Contains("HandleWindowClosedAsync(id)", adapter);
        Assert.Contains("ReleaseWebResourcesAsync", adapter);
        Assert.Contains("_presentation.ReleaseAsync()", adapter);
        Assert.Contains("_runtime.Dispose()", presentation);
        Assert.Contains("IPluginResourceLifecycle", registry);
        Assert.Contains("_hostResourceReleaser", registry);
        Assert.Contains("ShutdownAllAsync", app);
    }

    [Fact]
    public void App_DelegatesPluginStartupAndPackageOwnershipToCoordinator()
    {
        var app = Read("src", "LongBetterWindows.Host", "App.xaml.cs");
        var coordinator = Read(
            "src", "LongBetterWindows.Host", "Services", "PluginRuntimeCoordinator.cs");

        Assert.Contains("new PluginRuntimeCoordinator", app);
        Assert.Contains("_pluginRuntime.StartAsync", app);
        Assert.Contains("_pluginRuntime?.PackageInstaller", app);
        Assert.Contains("_pluginRuntime?.Dispose()", app);
        Assert.DoesNotContain("new PluginScanner", app);
        Assert.DoesNotContain("new CommandExecutor", app);
        Assert.Contains("RecoverInterruptedTransactionsAsync", coordinator);
        Assert.Contains("InstallAllFromDirectoryAsync", coordinator);
        Assert.Contains("_scanner.ScanAsync", coordinator);
        Assert.Contains("new CommandExecutor(registry).ExecuteAsync", coordinator);
    }

    [Fact]
    public void PluginPresentationAndMouseGesture_HaveUnifiedHostControls()
    {
        var mainXaml = Read("src", "LongBetterWindows.Host", "MainWindow.xaml");
        var mainSource = Read("src", "LongBetterWindows.Host", "MainWindow.xaml.cs");
        var pluginXaml = Read("src", "LongBetterWindows.Host", "Views", "PluginWindowHost.xaml");
        var pluginSource = Read("src", "LongBetterWindows.Host", "Views", "PluginWindowHost.xaml.cs");
        var adapter = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginAdapter.cs");
        var presentation = Read(
            "src", "LongBetterWindows.Host", "Engine", "WebPluginPresentationCoordinator.cs");
        var settings = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml");
        var gestures = Read("src", "LongBetterWindows.Host", "Services", "MouseGestureService.cs");

        Assert.Contains("EmbeddedPluginSurface", mainXaml);
        Assert.Contains("分离插件窗口", mainXaml);
        Assert.Contains("ShowEmbeddedPlugin", mainSource);
        Assert.Contains("CloseEmbeddedSurfaceAsync", mainSource);
        Assert.Contains("PreviewKeyDown=\"Window_PreviewKeyDown\"", pluginXaml);
        Assert.Contains("返回管理中心", pluginXaml);
        Assert.Contains("Key.Escape", pluginSource);
        Assert.Contains("DefaultPresentation", presentation);
        Assert.Contains("ShowDetachedWindow", presentation);
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
        Assert.Contains("<Version>1.9.0</Version>", project);
        Assert.Contains("<AssemblyVersion>1.9.0.0</AssemblyVersion>", project);
    }

    [Fact]
    public void ProductVersion_IsExposedConsistentlyToNativeAndWebUi()
    {
        var app = Read("src", "LongBetterWindows.Host", "App.xaml.cs");
        var webDispatcher = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginHostDispatcher.cs");
        var toolCenter = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml.cs");

        Assert.Contains("AssemblyInformationalVersionAttribute", app);
        Assert.Contains("App.ProductVersion", webDispatcher);
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
        Assert.Contains("WaitForExit($smokeTimeoutMilliseconds)", release);
        Assert.Contains("pluginCount -ne $expectedPluginCount", release);
    }

    [Fact]
    public void PluginManagement_UsesRecyclingVirtualizationAndOwnsPluginActions()
    {
        var xaml = Read("src", "LongBetterWindows.Host", "Views", "PluginManagementControl.xaml");
        Assert.Contains("x:Name=\"PluginsPanel\"", xaml);
        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", xaml);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", xaml);
        Assert.Contains("<VirtualizingStackPanel", xaml);
        Assert.Contains("DarkScrollBarStyle", xaml);

        var code = Read("src", "LongBetterWindows.Host", "Views", "PluginManagementControl.xaml.cs");
        Assert.Contains("PluginToggle_Click", code);
        Assert.Contains("PluginSettings_Click", code);
        Assert.Contains("CapabilityDetails_Click", code);

        var toolCenterXaml = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml");
        var toolCenterCode = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml.cs");
        Assert.Contains("PluginManagementControl", toolCenterXaml);
        Assert.Contains("PluginManagementHost.Refresh()", toolCenterCode);
        Assert.Contains("OpenPluginsForQuality", toolCenterCode);
        Assert.DoesNotContain("CreatePluginCard", toolCenterCode);
        Assert.DoesNotContain("PluginToggle_Click", toolCenterCode);
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
        var options = Read("src", "LongBetterWindows.Host", "Services", "AppStartupOptions.cs");
        var quality = Read("src", "LongBetterWindows.Host", "Services", "QualityRuntimeService.cs");
        var animation = Read("src", "LongBetterWindows.Host", "Helpers", "AnimationHelper.cs");
        var devTools = Read("src", "LongBetterWindows.Host", "Views", "PluginDevTools.html");

        Assert.Contains("SystemParameters.HighContrast", app);
        Assert.Contains("HighContrastPalette", app);
        Assert.Contains("--quality-high-contrast", options);
        Assert.Contains("--quality-reduce-motion", options);
        Assert.Contains("SystemParameters.HighContrast || _forceHighContrast", app);
        Assert.Contains("new SolidColorBrush(SystemColors.HighlightColor)", app);
        Assert.Contains("Long.Brush.Accent.Gradient", app);
        Assert.Contains("!SystemParameters.ClientAreaAnimation || _forceReduceMotion", app);
        Assert.Contains("SystemParameters.StaticPropertyChanged", app);
        Assert.Contains("Long.Motion.Fast", animation);
        Assert.Contains("duration == TimeSpan.Zero", animation);
        Assert.Contains("prefers-reduced-motion: reduce", devTools);
        Assert.Contains(":focus-visible", devTools);
        Assert.Contains("ReadArgument(arguments, \"--theme\")", options);
        Assert.Contains("--run-command", options);
        Assert.Contains("--plugins-dir", options);
        Assert.Contains("--quality-idle-ms", options);
        Assert.Contains("Quality idle sample:", quality);
        Assert.Contains("_qualityRuntime!.RunIdleProbeAsync", app);
        Assert.Contains("new PluginRuntimeStartRequest", app);
        Assert.Contains("--exit-after-command", options);
        var marketplace = Read("src", "LongBetterWindows.Host", "Views", "MarketplaceControl.xaml");
        var palette = Read("src", "LongBetterWindows.Host", "Views", "CommandPaletteWindow.xaml");
        var superPanel = Read("src", "LongBetterWindows.Host", "Views", "SuperPanelWindow.xaml");
        var toast = Read("src", "LongBetterWindows.Host", "Views", "ToastWindow.xaml");
        var desktopSmoke = Read("run-desktop-ui-smoke.ps1");
        Assert.Contains("AutomationProperties.LiveSetting=\"Assertive\"", marketplace);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", marketplace);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", palette);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", superPanel);
        Assert.Contains("AutomationProperties.LiveSetting=\"Assertive\"", toast);
        Assert.Contains("Get-AutomationSemantics", desktopSmoke);
        Assert.Contains("Current.Name", desktopSmoke);
        Assert.Contains("ControlType.ProgrammaticName", desktopSmoke);
        Assert.Contains("automation_semantics", desktopSmoke);
    }

    [Fact]
    public void ExplicitPluginDirectory_IsIsolatedFromDevelopmentFallback()
    {
        var scanner = Read("src", "LongBetterWindows.Host", "Engine", "PluginScanner.cs");
        var discovery = Read(
            "src", "LongBetterWindows.Host", "Engine", "PluginSourceDiscovery.cs");

        Assert.Contains("new PluginSourceDiscovery(pluginsDir)", scanner);
        Assert.Contains("pluginsDirectory is null ? FindDevelopmentPluginsDirectory() : null", discovery);
        Assert.Contains("_sourceDiscovery.Discover()", scanner);
        Assert.DoesNotContain("Directory.GetDirectories", scanner);
        Assert.DoesNotContain("FindDevPluginsDir", scanner);
    }

    [Fact]
    public void ReleasePipeline_ValidatesPublishedCommandsAndWebViewCleanup()
    {
        var release = Read("release.ps1");

        Assert.Contains("$expectedPluginCount = 25", release);
        Assert.Contains("$expectedCommandCount = 42", release);
        Assert.Contains("$uniquePluginIdCount", release);
        Assert.Contains("$commandCount -ne $expectedCommandCount", release);
        Assert.Contains("com.long.base64:base64.encode", release);
        Assert.Contains("$smokeTimeoutMilliseconds = 60000", release);
        Assert.Contains("startup_smoke_elapsed_ms", release);
        Assert.Contains("command_smoke_elapsed_ms", release);
        Assert.Contains("Get-ProductWebViewProcessIds", release);
        Assert.Contains("$addedWebViewProcessIds.Count -gt 0", release);
        Assert.Contains("Wait-ForNoAddedProductWebViewProcesses", release);
        Assert.Contains("[int] $TimeoutSeconds = 15", release);
        Assert.Contains("$webViewCleanupTimeoutSeconds = 45", release);
        Assert.Contains("webview_cleanup_elapsed_ms", release);
        Assert.Contains("command_smoke_exit_code", release);
    }

    [Fact]
    public void LocalMarketplaceRehearsal_UsesEphemeralSigningAndVerifiesRollbackPackages()
    {
        var rehearsal = Read("rehearse-marketplace-local.ps1");

        Assert.Contains("RSA]::Create(3072)", rehearsal);
        Assert.Contains("candidate-dry-run.json", rehearsal);
        Assert.Contains("-Target Local", rehearsal);
        Assert.Contains("-ConfirmReleaseId", rehearsal);
        Assert.Contains("rollback_registry_hash_matches", rehearsal);
        Assert.Contains("rollback_packages_available", rehearsal);
        Assert.Contains("Remove-Item -LiteralPath $workRoot -Recurse -Force", rehearsal);
        Assert.Contains("ephemeral_private_key_deleted", rehearsal);
    }

    [Fact]
    public void PluginScanner_DelegatesRuntimeInstanceCreationToLoader()
    {
        var scanner = Read("src", "LongBetterWindows.Host", "Engine", "PluginScanner.cs");
        var loader = Read(
            "src", "LongBetterWindows.Host", "Engine", "PluginRuntimeLoader.cs");

        Assert.Contains("new PluginRuntimeLoader", scanner);
        Assert.Contains("_runtimeLoader.LoadAsync", scanner);
        Assert.Contains("_runtimeLoader.Release", scanner);
        Assert.DoesNotContain("string.Equals(manifest.Runtime", scanner);
        Assert.DoesNotContain("_loader.LoadAsync", scanner);
        Assert.Contains("new WebPluginRuntime", loader);
        Assert.Contains("_scriptLoader.LoadAsync", loader);
        Assert.Contains("_nativeLoader.LoadAsync", loader);
    }

    [Fact]
    public void PluginScanner_DelegatesStandalonePackagingAndLifecycleToLoader()
    {
        var scanner = Read("src", "LongBetterWindows.Host", "Engine", "PluginScanner.cs");
        var standalone = Read(
            "src", "LongBetterWindows.Host", "Engine", "StandalonePluginLoader.cs");

        Assert.Contains("new StandalonePluginLoader", scanner);
        Assert.Contains("_standaloneLoader.LoadAsync", scanner);
        Assert.Contains("_standaloneLoader.UnloadAsync", scanner);
        Assert.DoesNotContain("BuildJavaScriptWrapper", scanner);
        Assert.DoesNotContain("Regex.Matches", scanner);
        Assert.DoesNotContain("new WebPluginRuntime", scanner);
        Assert.DoesNotContain("_scriptLoader", scanner);
        Assert.Contains("BuildJavaScriptWrapper", standalone);
        Assert.Contains("DeleteTemporaryDirectory", standalone);
        Assert.Contains("_registry.Unregister", standalone);
    }

    [Fact]
    public void PluginScanner_DelegatesMonitoringAndHandlesRenameAtomically()
    {
        var scanner = Read("src", "LongBetterWindows.Host", "Engine", "PluginScanner.cs");
        var monitor = Read(
            "src", "LongBetterWindows.Host", "Engine", "PluginChangeMonitor.cs");

        Assert.Contains("new PluginChangeMonitor", scanner);
        Assert.Contains("_changeMonitor.Start()", scanner);
        Assert.Contains("change.OldPath", scanner);
        Assert.Contains("reloadIfAvailable: false", scanner);
        Assert.Contains("change.NewPath", scanner);
        Assert.Contains("reloadIfAvailable: true", scanner);
        Assert.Contains("_reloadGate.WaitAsync", scanner);
        Assert.DoesNotContain("new FileSystemWatcher", scanner);
        Assert.DoesNotContain("DebounceReload", scanner);
        Assert.Contains("new FileSystemWatcher", monitor);
        Assert.Contains("Dictionary<string, CancellationTokenSource>", monitor);
        Assert.Contains("e.OldFullPath", monitor);
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
        var options = Read("src", "LongBetterWindows.Host", "Services", "AppStartupOptions.cs");
        var market = Read(
            "src", "LongBetterWindows.Host", "Views", "MarketplaceControl.xaml.cs");

        Assert.Contains("--quality-market-catalog", options);
        Assert.Contains("--quality-market-trust-store", options);
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

    [Fact]
    public void WebBridgeProtocolAndHostDispatch_AreSeparatedFromWebViewRuntime()
    {
        var runtime = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginRuntime.cs");
        var protocol = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginBridgeProtocol.cs");
        var dispatcher = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginHostDispatcher.cs");
        var arguments = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginArguments.cs");
        var lifecycle = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginViewLifecycle.cs");

        Assert.Contains("WebPluginBridgeProtocol.ParseRequest", runtime);
        Assert.Contains("WebPluginBridgeProtocol.GetRequiredCapability", runtime);
        Assert.Contains("_hostDispatcher.DispatchAsync", runtime);
        Assert.Contains("SerializeResult", protocol);
        Assert.Contains("BuildInjectionScript", protocol);
        Assert.Contains("FileOps.MoveAsync", dispatcher);
        Assert.Contains("WebPluginArguments.GetJson", dispatcher);
        Assert.Contains("GetHeaders", arguments);
        Assert.Contains("CoreWebView2.NavigationStarting +=", lifecycle);
        Assert.Contains("_navigationPolicy.IsTrustedLocalUri", lifecycle);
        Assert.Contains("dispatcher.Invoke(Dispose)", lifecycle);
        Assert.Contains("dispatcher.InvokeAsync(() => PostMessageCore(json))", lifecycle);
        Assert.DoesNotContain("window.long =", runtime);
        Assert.DoesNotContain("FileOps.MoveAsync", runtime);
        Assert.DoesNotContain("CoreWebView2NavigationStartingEventArgs", runtime);
        Assert.True(runtime.Split('\n').Length < 150);
    }

    [Fact]
    public void WebPluginPresentation_IsSeparatedFromPluginStateAdapter()
    {
        var adapter = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginAdapter.cs");
        var presentation = Read(
            "src", "LongBetterWindows.Host", "Engine", "WebPluginPresentationCoordinator.cs");

        Assert.Contains("_presentation.EnsureVisible()", adapter);
        Assert.Contains("_presentation.CloseVisibleSurface()", adapter);
        Assert.Contains("_presentation.ReleaseAsync()", adapter);
        Assert.Contains("ShowEmbeddedPlugin", presentation);
        Assert.Contains("PluginWindowHost", presentation);
        Assert.Contains("NotifyWindowClosedAsync", presentation);
        Assert.DoesNotContain("PluginWindowHost", adapter);
        Assert.True(adapter.Split('\n').Length < 130);
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
