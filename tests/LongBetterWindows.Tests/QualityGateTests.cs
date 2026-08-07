using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class QualityGateTests
{
    [Fact]
    public void PluginHealthDiagnostics_AreKeyboardAccessibleAndNotTimerPolled()
    {
        var xaml = Read("src", "LongBetterWindows.Host", "Views", "PerformancePanel.xaml");
        var code = Read("src", "LongBetterWindows.Host", "Views", "PerformancePanel.xaml.cs");
        var presentation = Read(
            "src", "LongBetterWindows.Host", "Interaction",
            "PluginRuntimeDiagnosticPresentation.cs");
        var performanceRefresh = Read(
            "src", "LongBetterWindows.Host", "Interaction",
            "PerformanceRefreshCoordinator.cs");

        Assert.Contains("Long.Diagnostics.PluginHealth.Refresh", xaml);
        Assert.Contains("LongIconButton", xaml);
        Assert.Contains("<ListBox", xaml);
        Assert.Contains("MaxHeight=\"280\"", xaml);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{Binding AccessibilityName}\"", xaml);
        Assert.Contains("Value=\"{DynamicResource Long.Brush.State.Danger}\"", xaml);
        Assert.DoesNotContain("StatusBrush", code);
        Assert.Contains("RefreshHealthButton_Click", code);
        Assert.Contains("PluginRuntimeDiagnostics.Build", code);
        Assert.Contains("PluginRuntimeHealthState.Unhealthy => 0", presentation);
        Assert.Contains("PluginRuntimeHealthState.Degraded => 1", presentation);
        Assert.DoesNotContain("PluginRuntimeDiagnostics", performanceRefresh);
        var captureScript = Read("capture-visual-matrix.ps1");
        Assert.Contains("'super-panel','diagnostics'", captureScript);
        Assert.Contains("[int] $CaptureWidth = 1120", captureScript);
        Assert.Contains("'--quality-width', $CaptureWidth.ToString()", captureScript);
    }

    [Fact]
    public void Marketplace_ExposesTrustCompatibilityPermissionsAndRollbackActions()
    {
        var xaml = Read("src", "LongBetterWindows.Host", "Views", "MarketplaceControl.xaml");
        var source = Read("src", "LongBetterWindows.Host", "Views", "MarketplaceControl.xaml.cs");
        var installer = Read("src", "LongBetterWindows.Host", "Engine", "LpakInstaller.cs");
        var transport = Read("src", "LongBetterWindows.Host", "Engine", "MarketplaceTransport.cs");
        var session = Read(
            "src", "LongBetterWindows.Host", "Interaction", "MarketplaceSessionCoordinator.cs");

        Assert.DoesNotContain("MarketSearchBox", xaml);
        Assert.Contains("_workspaceQuery", source);
        Assert.Contains("VersionBox", xaml);
        Assert.Contains("PermissionDiffItems", xaml);
        Assert.Contains("ConfirmRecoveryText", xaml);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml);
        Assert.Contains("<RowDefinition Height=\"*\" />", xaml);
        Assert.Contains("VerticalAlignment=\"Top\"", xaml);
        Assert.Contains("HighTrustWarning", xaml);
        Assert.Contains("ConfirmOverlay", xaml);
        Assert.Contains("_session.PrepareLocalPackageAsync", source);
        Assert.Contains("MarketplaceOperationPresenter.CreateInstallReview", source);
        Assert.Contains("GetPreviewInstalledVersion(version.Version)", source);
        Assert.DoesNotContain(
            "ConfirmCard.VerticalAlignment = VerticalAlignment.Center",
            source);
        Assert.Contains("installer.InstallAsync(pending.PackagePath!, pending.Metadata)", source);
        Assert.Contains("installer.UninstallAsync(pending.PluginId!)", source);
        Assert.Contains("runtime.ValidatePackageAsync", session);
        Assert.Contains(".long-transaction-", installer);
        Assert.Contains("RecoverInterruptedTransactionsAsync", installer);
        Assert.Contains("TransactionPhase.Committed", installer);
        Assert.Contains("_transactionGate", installer);
        Assert.Contains("MoveDirectoryWithRetryAsync", installer);
        Assert.Contains("attempt < maximumAttempts - 1", installer);
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
    public void MarketplaceReleasePreparation_BindsSigningVerificationAndDryRun()
    {
        var wrapper = Read("prepare-marketplace-release.ps1");
        var pipeline = Read(
            "tools", "LongBetterWindows.MarketplacePublisher",
            "MarketplaceReleasePreparationPipeline.cs");
        var program = Read(
            "tools", "LongBetterWindows.MarketplacePublisher", "Program.cs");

        Assert.Contains("--private-key", wrapper);
        Assert.DoesNotContain("--force", wrapper);
        Assert.Contains("new MarketplacePublishingPipeline()", pipeline);
        Assert.Contains("new MarketplaceBundleVerificationPipeline()", pipeline);
        Assert.Contains("new MarketplaceDeploymentPipeline()", pipeline);
        Assert.Contains("DryRun = true", pipeline);
        Assert.Contains("preparation-summary.json", pipeline);
        Assert.Contains("BundleVerificationReportSha256", pipeline);
        Assert.Contains("DeploymentDryRunReportSha256", pipeline);
        Assert.Contains("args[0], \"prepare\"", program);
    }

    [Fact]
    public void MarketplaceProductionRelease_RequiresApprovedPreparationAndFailureRollback()
    {
        var release = Read("release-marketplace.ps1");
        var validator = Read(
            "tools", "LongBetterWindows.MarketplacePublisher",
            "MarketplaceReleasePreparationValidator.cs");
        var wrapper = Read("verify-marketplace-preparation.ps1");

        Assert.Contains("ConfirmReleaseId", release);
        Assert.Equal(2, release.Split("& $verifyPreparation").Length - 1);
        Assert.Contains("preparation_summary_sha256", release);
        Assert.Contains("baseline-verification.json", release);
        Assert.Contains("deployed-verification.json", release);
        Assert.Contains("deployed_matches_preparation", release);
        Assert.Contains("Public marketplace package set differs", release);
        Assert.True(
            release.IndexOf("Public marketplace package set differs", StringComparison.Ordinal) <
            release.IndexOf("$summary.deployment_verified = $true", StringComparison.Ordinal),
            "The release must not be marked verified before the public package set matches the approved preparation.");
        Assert.Contains("finally", release);
        Assert.Contains("& $rollback", release);
        Assert.Contains("baseline_restored", release);
        Assert.Contains("release-summary.json", release);
        Assert.Contains("verify-preparation", wrapper);
        Assert.Contains("FixedTimeEquals", validator);
        Assert.Contains("MarketplaceBundleVerificationPipeline().VerifyAsync", validator);
        Assert.Contains("MarketplaceDeploymentPipeline.CreatePlanAsync", validator);
        Assert.Contains("Confirmed Release ID must exactly match", validator);
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
        Assert.Contains("release-evidence-io.ps1", rehearsal);
        Assert.Contains("Write-NewJsonFileAtomically", rehearsal);
        Assert.DoesNotContain("Set-Content -LiteralPath $summaryPath", rehearsal);
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
    public void ExternalReleaseGate_BindsEveryApprovedGateToOneCandidateAndFullRollback()
    {
        var gate = Read("verify-external-release-gate.ps1");
        var evidenceIo = Read("release-evidence-io.ps1");
        var rehearsal = Read("rehearse-marketplace.ps1");

        Assert.Contains("ExpectedSourceCommit", gate);
        Assert.Contains("ExpectedDistributionChannel", gate);
        Assert.Contains("approved_release_download_gate", gate);
        Assert.Contains("approved_clean_windows_release_gate", gate);
        Assert.Contains("approved_physical_device_dpi_matrix", gate);
        Assert.Contains("approved_physical_accessibility_matrix", gate);
        Assert.Contains("marketplace_https_rehearsal", gate);
        Assert.Contains("Release Manifest source commit", gate);
        Assert.Contains("refer to different packages", gate);
        Assert.Contains("independent operator and reviewer", gate);
        Assert.Contains("preflight_only", gate);
        Assert.Contains("deployment_verified", gate);
        Assert.Contains("rollback_verified", gate);
        Assert.Contains("Physical DPI gate schema version 3 is required", gate);
        Assert.Contains("Physical DPI gate must contain exactly 32 captures", gate);
        Assert.Contains("Accessibility gate schema version 3 is required", gate);
        Assert.Contains("requires at least one screen-reader approval", gate);
        Assert.Contains("Read-PortableMatrixSource", gate);
        Assert.Contains("portable source hash mismatch", gate);
        Assert.Contains("portable source content does not match its summary", gate);
        Assert.Contains("Release-download gate summary contract is incomplete", gate);
        Assert.Contains("Clean-environment gate summary contract is incomplete", gate);
        Assert.Contains("Read-HashLockedSource", gate);
        Assert.Contains("'Release-download evidence'", gate);
        Assert.Contains("'Release-download approval'", gate);
        Assert.Contains("'Clean-environment evidence'", gate);
        Assert.Contains("source hash mismatch", gate);
        Assert.Contains("Release-download evidence source content does not match", gate);
        Assert.Contains("Release-download approval source content does not match", gate);
        Assert.Contains("Clean-environment evidence source content does not match", gate);
        Assert.Contains("Marketplace rehearsal schema version 2 is required", gate);
        Assert.Contains("$reports[$required.Key] = Read-HashLockedSource", gate);
        Assert.Contains("\"Marketplace rehearsal evidence $($required.Key)\"", gate);
        Assert.Contains("Marketplace deployment report differs from the approved preflight plan", gate);
        Assert.Contains("Marketplace verification package inventory is invalid", gate);
        Assert.Contains("Marketplace rollback verification did not restore the baseline Registry", gate);
        Assert.Contains("Marketplace deployed verification did not observe a Registry change", gate);
        Assert.Contains("Marketplace rehearsal report chronology is invalid", gate);
        Assert.Contains("Release Manifest candidate identity contract is incomplete", gate);
        Assert.Contains("Release Manifest package inventory is invalid", gate);
        Assert.Contains("Release Manifest installer inventory is invalid", gate);
        Assert.Contains("Release artifact file was not found", gate);
        Assert.Contains("artifact file size does not match the Manifest", gate);
        Assert.Contains("artifact file hash does not match the Manifest", gate);
        Assert.Contains("SHA256SUMS.txt", gate);
        Assert.Contains("exact Manifest artifact set", gate);
        Assert.Contains("Unsigned Release Manifest publisher disclosure is incomplete", gate);
        Assert.Contains("evidence_contract", gate);
        Assert.Contains("candidate = [ordered]@", gate);
        Assert.Contains("artifact_files_verified = $true", gate);
        Assert.Contains("checksum_file_verified = $true", gate);
        Assert.Contains("Get-FileHash", gate);
        Assert.Contains("external_release_gate_decision", gate);
        Assert.Contains("external_release_gate_preflight", gate);
        Assert.Contains("PreflightOnly does not accept OutputPath", gate);
        Assert.Contains("OutputPath is required unless PreflightOnly", gate);
        Assert.Contains("preflight_only = [bool]$PreflightOnly", gate);
        Assert.Contains("decision already exists", gate);
        Assert.Contains("Write-DecisionAtomically", gate);
        Assert.Contains("Write-NewJsonFileAtomically", gate);
        Assert.Contains("[IO.File]::Move($temporaryPath, $resolvedPath)", evidenceIo);
        Assert.Contains("Remove-Item -LiteralPath $temporaryPath -Force", evidenceIo);
        Assert.Contains("[IO.FileMode]::CreateNew", evidenceIo);
        Assert.Contains("[IO.FileOptions]::DeleteOnClose", evidenceIo);
        Assert.Contains("changed after validation", evidenceIo);
        Assert.Contains("[IO.File]::Replace($temporaryPath, $resolvedPath, $backupPath)", evidenceIo);
        Assert.DoesNotContain(
            "Set-Content -LiteralPath $resolvedOutput",
            gate,
            StringComparison.Ordinal);
        Assert.Contains("classification = 'marketplace_https_rehearsal'", rehearsal);
        Assert.Contains("schema_version = 2", rehearsal);
        Assert.Contains("preflight_dry_run = $dryRunReport", rehearsal);
        Assert.Contains("rollback_verification = $rollbackVerification", rehearsal);
        Assert.Contains("$summary.passed = -not $summary.preflight_only", rehearsal);
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
        Assert.Contains("WaitForWebViewReadyAsync", quality);
        Assert.Contains("document.readyState", quality);
        Assert.Contains("Math.Max(250, options.QualityCaptureDelayMilliseconds)", quality);
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
        Assert.Contains("release-evidence-io.ps1", matrix);
        Assert.Contains("Write-NewJsonFileAtomically", matrix);
        Assert.Contains("Visual matrix manifest", matrix);
        Assert.DoesNotContain("Set-Content `", matrix);
    }

    [Fact]
    public void WebUiKitVisualMatrix_CoversThemesAccessibilityAndNarrowWidth()
    {
        var matrix = Read("capture-web-ui-kit-matrix.ps1");

        Assert.Contains("com.long.reference-web-ui-kit", matrix);
        Assert.Contains("light','dark", matrix);
        Assert.Contains("normal','high-contrast','reduced-motion','combined", matrix);
        Assert.Contains("920,640", matrix);
        Assert.Contains("--quality-open-plugin-runtime", matrix);
        Assert.Contains("--quality-capture-view', 'plugin", matrix);
        Assert.Contains("physical_device_matrix_required = $true", matrix);
        Assert.Contains("Get-FileHash", matrix);
        Assert.Contains("Write-NewJsonFileAtomically", matrix);
        Assert.Contains("web-ui-kit-matrix.json", matrix);
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
        Assert.Contains("ExpectedSourceCommit", capture);
        Assert.Contains("Repository HEAD does not match ExpectedSourceCommit", capture);
        Assert.Contains("requires a clean tracked source tree", capture);
        Assert.Contains("must rebuild the expected source commit", capture);
        Assert.Contains("source_commit = $expectedCommit", capture);
        Assert.Contains("Write-NewJsonFileAtomically", capture);
        Assert.DoesNotContain("Set-Content -LiteralPath $manifestPath", capture);
        Assert.Contains("ApproveAfterVisualReview", capture);
        Assert.Contains("ProcessTimeoutSeconds = 90", capture);
        Assert.Contains("WaitForExit($ProcessTimeoutSeconds * 1000)", capture);
        Assert.Contains("no_clipping_or_overflow", capture);
        Assert.Contains("management_center_layout_is_stable", capture);
        Assert.Contains("management_module_tabs_are_readable", capture);
        Assert.Contains("must include the main management-center view", capture);
        Assert.Contains("webview_preview", capture);
        Assert.Contains("wpf_render_target", capture);
        Assert.Contains("'--command-text', $PluginCommandText", capture);
        Assert.Contains("ConfirmVisualReview", approve);
        Assert.Contains("ExpectedSourceCommit", approve);
        Assert.Contains("Physical DPI evidence source commit does not match ExpectedSourceCommit", approve);
        Assert.Contains("Scale confirmation mismatch", approve);
        Assert.Contains("Evidence changed after capture", approve);
        Assert.Contains("is not pending review", approve);
        Assert.Contains("Update-JsonFileAtomically", approve);
        Assert.DoesNotContain("Set-Content -LiteralPath $manifestPath", approve);
        Assert.Contains("Get-FileHash", approve);
        Assert.Contains("schema version 2 is required", approve);
        Assert.Contains("management_center_layout_is_stable", approve);
        Assert.Contains("management_module_tabs_are_readable", approve);
        Assert.Contains("100,125,150,200", verify);
        Assert.Contains("human_review.status -ne 'approved'", verify);
        Assert.Contains("Get-FileHash", verify);
        Assert.Contains("Expected 8 captures", verify);
        Assert.Contains("schema version 2 is required", verify);
        Assert.Contains("management-center view", verify);
        Assert.Contains("Manual physical DPI checklist is incomplete", verify);
        Assert.Contains("approved_physical_device_dpi_matrix", verify);
        Assert.Contains("ExpectedSourceCommit", verify);
        Assert.Contains("Physical DPI evidence source commit does not match ExpectedSourceCommit", verify);
        Assert.Contains("source_commit = $expectedCommit", verify);
        Assert.Contains("schema_version = 3", verify);
        Assert.Contains("source_manifest = [ordered]@", verify);
        Assert.Contains(".sources", verify);
        Assert.Contains("Copy-Item -LiteralPath $source.path", verify);
        Assert.Contains("Write-NewJsonFileAtomically", verify);
        Assert.Contains("[IO.Directory]::Move($temporarySourceDirectory, $sourceDirectory)", verify);
        Assert.DoesNotContain("Set-Content -LiteralPath $resolvedOutput", verify);
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
        Assert.Contains("Write-NewJsonFileAtomically", capture);
        Assert.DoesNotContain("Set-Content -LiteralPath $manifestPath", capture);
        Assert.Contains("ConfirmKeyboardNavigation", approve);
        Assert.Contains("ConfirmFocusVisibility", approve);
        Assert.Contains("ConfirmMotionBehavior", approve);
        Assert.Contains("ConfirmManagementTabOrder", approve);
        Assert.Contains("ConfirmManagementActivation", approve);
        Assert.Contains("ConfirmManagementModuleCloseMru", approve);
        Assert.Contains("ConfirmScreenReaderAnnouncements", approve);
        Assert.Contains("ConfirmManagementCloseAnnouncements", approve);
        Assert.Contains("evidence changed after capture", approve, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is not pending review", approve);
        Assert.Contains("Update-JsonFileAtomically", approve);
        Assert.DoesNotContain("Set-Content -LiteralPath $manifestPath", approve);
        Assert.Contains("management_destination_tab_order", capture);
        Assert.Contains("management_destination_activation", capture);
        Assert.Contains("management_module_close_mru", capture);
        Assert.Contains("management_close_announcements", capture);
        Assert.Contains("high_contrast", verify);
        Assert.Contains("reduced_motion", verify);
        Assert.Contains("combined", verify);
        Assert.Contains("at least one approved Narrator or NVDA", verify);
        Assert.Contains("schema version 2 is required", verify);
        Assert.Contains("management_destination_tab_order", verify);
        Assert.Contains("management_destination_activation", verify);
        Assert.Contains("management_module_close_mru", verify);
        Assert.Contains("management_close_announcements", verify);
        Assert.Contains("approved_physical_accessibility_matrix", verify);
        Assert.Contains("schema_version = 3", verify);
        Assert.Contains("source_manifest = [ordered]@", verify);
        Assert.Contains(".sources", verify);
        Assert.Contains("Copy-Item -LiteralPath $source.path", verify);
        Assert.Contains("Write-NewJsonFileAtomically", verify);
        Assert.Contains("[IO.Directory]::Move($temporarySourceDirectory, $sourceDirectory)", verify);
        Assert.DoesNotContain("Set-Content -LiteralPath $resolvedOutput", verify);
    }

    [Fact]
    public void CleanWindowsReleaseEvidence_UsesCandidatePackageAndRequiresIndependentLifecycleApproval()
    {
        var capture = Read("capture-clean-environment-evidence.ps1");
        var approve = Read("approve-clean-environment-evidence.ps1");
        var verify = Read("verify-clean-environment-evidence.ps1");
        var desktopSmoke = Read("run-desktop-ui-smoke.ps1");

        Assert.Contains("ConfirmCleanUserEnvironment", capture);
        Assert.Contains("ExpectedSourceCommit", capture);
        Assert.Contains("ExpectedDistributionChannel", capture);
        Assert.Contains("Release signature state does not match its distribution channel", capture);
        Assert.Contains("Release manifest source commit does not match ExpectedSourceCommit", capture);
        Assert.Contains("source_commit = $expectedCommit", capture);
        Assert.Contains("Release ZIP hash does not match", capture);
        Assert.Contains("Start capture before the first launch", capture);
        Assert.Contains("-ReleaseDirectory $installRoot", capture);
        Assert.Contains("clean_windows_release_evidence", capture);
        Assert.Contains("Write-NewJsonFileAtomically", capture);
        Assert.DoesNotContain("Set-Content -LiteralPath $evidencePath", capture);
        Assert.Contains("ConfirmTrayIcon", approve);
        Assert.Contains("ExpectedSourceCommit", approve);
        Assert.Contains("ExpectedDistributionChannel", approve);
        Assert.Contains("Clean-environment evidence source commit does not match ExpectedSourceCommit", approve);
        Assert.Contains("ConfirmGlobalHotkey", approve);
        Assert.Contains("ConfirmWebViewRuntime", approve);
        Assert.Contains("ConfirmParallelUpgradeDataPreserved", approve);
        Assert.Contains("ConfirmRollbackToPreviousVersion", approve);
        Assert.Contains("ConfirmUninstallIntegrationsRemoved", approve);
        Assert.Contains("Reviewer must differ", approve);
        Assert.Contains("evidence changed after capture", approve, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is not pending review", approve);
        Assert.Contains("Update-JsonFileAtomically", approve);
        Assert.DoesNotContain("Set-Content -LiteralPath $manifestPath", approve);
        Assert.Contains("human_review.status -ne 'approved'", verify);
        Assert.Contains("ExpectedDistributionChannel", verify);
        Assert.Contains("expected eligible distribution channel", verify);
        Assert.Contains("Manual lifecycle checklist is incomplete", verify);
        Assert.Contains("approved_clean_windows_release_gate", verify);
        Assert.Contains("ExpectedSourceCommit", verify);
        Assert.Contains("Clean-environment evidence source commit does not match ExpectedSourceCommit", verify);
        Assert.Contains("source_commit = $expectedCommit", verify);
        Assert.Contains("Captured release manifest source commit does not match ExpectedSourceCommit", verify);
        Assert.Contains("package identity does not match the captured release manifest", verify);
        Assert.Contains("schema_version = 2", verify);
        Assert.Contains("evidence_manifest = [ordered]@", verify);
        Assert.Contains("summary and evidence manifest must share one directory", verify);
        Assert.Contains("Write-NewJsonFileAtomically", verify);
        Assert.DoesNotContain("Set-Content -LiteralPath $resolvedOutput", verify);
        Assert.Contains("ReleaseDirectory", desktopSmoke);
        Assert.Contains("Plugins directory was not found", desktopSmoke);
    }

    [Fact]
    public void ReleaseDownloadEvidence_VerifiesManifestHashAndInternetOriginWithoutLaunchingPackage()
    {
        var capture = Read("capture-release-download-evidence.ps1");
        var approve = Read("approve-release-download-evidence.ps1");
        var verify = Read("verify-release-download-evidence.ps1");

        Assert.Contains("ExpectedSourceCommit", capture);
        Assert.Contains("ExpectedDistributionChannel", capture);
        Assert.Contains("Get-FileHash", capture);
        Assert.Contains("Zone.Identifier", capture);
        Assert.Contains("ZoneId=3", capture);
        Assert.Contains("AllowedDownloadHosts", capture);
        Assert.Contains("Download evidence output already exists", capture);
        Assert.Contains("query_parameters_recorded = $false", capture);
        Assert.Contains("verified_release_download_provenance", capture);
        Assert.Contains("Write-NewJsonFileAtomically", capture);
        Assert.DoesNotContain("Set-Content -LiteralPath $resolvedOutputPath", capture);
        Assert.Contains("smartscreen_observed = $false", capture);
        Assert.Contains("first_launch_observed = $false", capture);
        Assert.DoesNotContain("Start-Process", capture);
        Assert.DoesNotContain("Expand-Archive", capture);
        Assert.Contains("Reviewer must differ", approve);
        Assert.Contains("ConfirmExtractedExecutableOriginChecked", approve);
        Assert.Contains("ConfirmSmartScreenObserved", approve);
        Assert.Contains("ConfirmAntivirusObserved", approve);
        Assert.Contains("ConfirmFirstLaunchObserved", approve);
        Assert.Contains("Get-FileHash", approve);
        Assert.Contains("release_download_human_approval", approve);
        Assert.Contains("changed during human approval", approve);
        Assert.Contains("Write-NewJsonFileAtomically", approve);
        Assert.DoesNotContain("Set-Content -LiteralPath $resolvedOutputPath", approve);
        Assert.Contains("Release-download evidence changed after human approval", verify);
        Assert.Contains("distinct operator and reviewer identities", verify);
        Assert.Contains("Interactive release-download checklist is incomplete", verify);
        Assert.Contains("approved_release_download_gate", verify);
        Assert.Contains("schema_version = 2", verify);
        Assert.Contains("evidence = [ordered]@", verify);
        Assert.Contains("approval = [ordered]@", verify);
        Assert.Contains("summary and source files must share one directory", verify);
        Assert.Contains("Write-NewJsonFileAtomically", verify);
        Assert.DoesNotContain("Set-Content -LiteralPath $resolvedOutputPath", verify);
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
        Assert.Contains("Invoke-CodeSign $installerPath", sign);
        Assert.Contains("$manifest.installers = $signedInstallers", sign);
        Assert.Contains("@($signedPackages) + @($signedInstallers)", sign);
        Assert.Contains("Move-Item -LiteralPath $stagingRoot -Destination $outputRoot", sign);
        Assert.Contains("release_eligible = $true", sign);
        Assert.Contains("release-evidence-io.ps1", sign);
        Assert.Contains("Update-JsonFileAtomically", sign);
        Assert.Contains("Update-TextFileAtomically", sign);
        Assert.Contains("Write-NewTextFileAtomically", sign);
        Assert.DoesNotContain("Set-Content -LiteralPath (Join-Path $stagingRoot 'release-manifest.json')", sign);
        Assert.DoesNotContain("Set-Content -LiteralPath (Join-Path $stagingRoot 'SHA256SUMS.txt')", sign);
        Assert.Contains("Expand-Archive", verify);
        Assert.Contains("Resolve-Within", verify);
        Assert.Contains("escapes release root", verify);
        Assert.Contains("Get-AuthenticodeSignature", verify);
        Assert.Contains("verify /pa /all /tw", verify);
        Assert.Contains("Signed file count mismatch", verify);
        Assert.Contains("Signed installer identity mismatch", verify);
        Assert.Contains("Installer Authenticode signature is invalid", verify);
        Assert.Contains("verified_windows_authenticode_release", verify);
        Assert.Contains("ExpectedSourceCommit", verify);
        Assert.Contains("Signed release source commit does not match ExpectedSourceCommit", verify);
        Assert.Contains("source_commit = $expectedCommit", verify);
        Assert.Contains("release-evidence-io.ps1", verify);
        Assert.Contains("Write-NewJsonFileAtomically", verify);
        Assert.DoesNotContain("Set-Content -LiteralPath $resolvedOutput", verify);
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
    public void PhysicalAccessibilityEvidence_BindsEveryProfileToOneCleanSourceCommit()
    {
        var capture = Read("capture-accessibility-evidence.ps1");
        var approve = Read("approve-accessibility-evidence.ps1");
        var verify = Read("verify-accessibility-matrix.ps1");

        Assert.Contains("ExpectedSourceCommit", capture);
        Assert.Contains("Repository HEAD does not match ExpectedSourceCommit", capture);
        Assert.Contains("requires a clean tracked source tree", capture);
        Assert.Contains("must rebuild the expected source commit", capture);
        Assert.Contains("source_commit = $expectedCommit", capture);
        Assert.Contains("ExpectedSourceCommit", approve);
        Assert.Contains("Accessibility evidence source commit does not match ExpectedSourceCommit", approve);
        Assert.Contains("ExpectedSourceCommit", verify);
        Assert.Contains("Accessibility evidence source commit does not match ExpectedSourceCommit", verify);
        Assert.Contains("source_commit = $expectedCommit", verify);
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
        var toolCenter = Read(
            "src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml.cs");
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
    public void WorkflowEditor_DelegatesDraftAndPersistenceToInteractionLayer()
    {
        var view = Read(
            "src", "LongBetterWindows.Host", "Views", "WorkflowEditorControl.xaml.cs");
        var xaml = Read(
            "src", "LongBetterWindows.Host", "Views", "WorkflowEditorControl.xaml");
        var invocationEditor = Read(
            "src", "LongBetterWindows.Host", "Views", "WorkflowInvocationEditorControl.xaml.cs");
        var invocationEditorXaml = Read(
            "src", "LongBetterWindows.Host", "Views", "WorkflowInvocationEditorControl.xaml");
        var session = Read(
            "src", "LongBetterWindows.Host", "Interaction", "CommandWorkflowEditorSession.cs");

        Assert.Contains("new CommandWorkflowEditorSession", view);
        Assert.Contains("_session.AddStep", view);
        Assert.Contains("_session.UpdateStep", view);
        Assert.Contains("_session.UpdateInvocation", view);
        Assert.Contains("_session.SaveAsync", view);
        Assert.Contains("_session.DeleteCurrentAsync", view);
        Assert.Contains("_session.PreviewImportAsync", view);
        Assert.Contains("_session.AdoptImport", view);
        Assert.Contains("_session.ExportCurrentAsync", view);
        Assert.Contains("new CommandWorkflowRunSession", view);
        Assert.Contains("_runSession.Prepare", view);
        Assert.Contains("_runSession.ExecuteApprovedAsync", view);
        Assert.Contains("_runSession.CancelExecution", view);
        Assert.Contains("_reports.ListAsync", view);
        Assert.Contains("ApplyResponsiveLayout", view);
        Assert.Contains("AutomationProperties.SetItemStatus", view);
        Assert.Contains("CompactWorkflowCombo", xaml);
        Assert.Contains("WorkflowInvocationEditorControl", xaml);
        Assert.Contains(
            "AutomationProperties.Name=\"{DynamicResource i18n.workflow.action.importExternal}\"",
            xaml);
        Assert.Contains(
            "AutomationProperties.Name=\"{DynamicResource i18n.workflow.import.adoptDraft}\"",
            xaml);
        Assert.Contains(
            "AutomationProperties.Name=\"{DynamicResource i18n.workflow.action.export}\"",
            xaml);
        Assert.Contains(
            "AutomationProperties.Name=\"{DynamicResource i18n.workflow.execution.review.confirmA11y}\"",
            xaml);
        Assert.Contains("ReportTimeline", xaml);
        Assert.DoesNotContain("File.WriteAllText", view);
        Assert.DoesNotContain("OpenFolderDialog", view);
        Assert.Contains("OpenFolderDialog", invocationEditor);
        Assert.Contains("MaximumImageBytes", invocationEditor);
        Assert.Contains(
            "AutomationProperties.Name=\"{DynamicResource i18n.workflow.invocation.argumentKey}\"",
            invocationEditorXaml);
        Assert.Contains(
            "AutomationProperties.Name=\"{DynamicResource i18n.workflow.invocation.addArgumentA11y}\"",
            invocationEditorXaml);
        Assert.Contains("Long.Workflow.ArgumentPreset", invocationEditorXaml);
        Assert.Contains("Long.Workflow.ArgumentPreset.Apply", invocationEditorXaml);
        Assert.Contains("SchemaArguments", invocationEditorXaml);
        Assert.Contains("RelativeSource AncestorType={x:Type UserControl}", invocationEditorXaml);
        Assert.Contains("AutomationProperties.AutomationId=\"{Binding Key}\"", invocationEditorXaml);
        Assert.Contains("LongPasswordBox", invocationEditorXaml);
        Assert.Contains("SchemaSensitive_PasswordChanged", invocationEditorXaml);
        Assert.Contains("BindingArgumentKey_SelectionChanged", invocationEditorXaml);
        Assert.Contains("ShowArgumentKeyOptions", invocationEditorXaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", invocationEditorXaml);
        Assert.Contains("SnapshotArgumentSchema", view);
        Assert.Contains("IsKeyboardFocusWithin: true", view);
        Assert.Contains("IsKeyboardFocusWithin: true", invocationEditor);
        Assert.Contains("PluginCommandArgumentValidator.ValidateForWorkflowPreflight", invocationEditor);
        var uuidManifest = Read("src", "UuidGenerator", "manifest.json");
        Assert.Contains("argument_schema", uuidManifest);
        Assert.Contains("argument_presets", uuidManifest);
        Assert.Contains(
            "AutomationProperties.Name=\"{DynamicResource i18n.workflow.action.save}\"",
            xaml);
        Assert.Contains("CommandWorkflowPlanner", session);
        Assert.Contains("CommandWorkflowBindingResolver.Resolve", Read(
            "src", "LongBetterWindows.Host", "Interaction", "CommandWorkflowExecutor.cs"));
        Assert.Contains("MaximumOutputCount", Read(
            "src", "LongBetterWindows.Host", "Interaction", "CommandWorkflowBindingResolver.cs"));
        Assert.Contains("ExpectedExistingDefinitionSha256", Read(
            "src", "LongBetterWindows.Host", "Interaction", "CommandWorkflowRepository.cs"));
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
        var expansion = Read(
            "src", "LongBetterWindows.Host", "Interaction", "PanelExpansionIntent.cs");

        Assert.Contains("i18n.superPanel.title", xaml);
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
        Assert.Contains("ForegroundWindowActivator.TryActivate", lifecycle);
        Assert.Contains("ForegroundWindowActivator.TryActivate", palette);
        Assert.Contains("_windowLifecycle.ForegroundWindowHandle", source);
        Assert.Contains("_windowLifecycle.ReleaseForegroundAsync", source);
        Assert.Contains("--quality-origin-window", Read("run-desktop-ui-smoke.ps1"));
        Assert.Contains("new PanelExpansionIntent", source);
        Assert.Contains("CommandPaletteWindow.ShowPalette(intent)", source);
        Assert.Contains("internal static void ShowPalette(PanelExpansionIntent intent)", palette);
        Assert.Contains("_preferredSelectionId", palette);
        Assert.Contains("ClearSensitiveState", expansion);
        Assert.Contains("SuperPanelPresentationMode.CompactGrid", groups);
        Assert.Contains("SuperPanelPresentationMode.ContextList", groups);
        Assert.Contains("_groupCoordinator.MovePage", source);
        Assert.Contains("SuperPanelCompactResultTemplate", xaml);
        Assert.Contains("SuperPanelContextResultTemplate", xaml);
        Assert.Contains("Long.SuperPanel.PreviousPage", xaml);
        Assert.Contains("Long.SuperPanel.NextPage", xaml);
        Assert.Contains("context_preserved_on_transition", Read("run-desktop-ui-smoke.ps1"));
        Assert.Contains("selection_preserved_on_transition", Read("run-desktop-ui-smoke.ps1"));
        Assert.Contains("context_list_mode", Read("run-desktop-ui-smoke.ps1"));
        Assert.Contains("compact_grid_mode", Read("run-desktop-ui-smoke.ps1"));
        Assert.Contains("context_matrix", Read("run-desktop-ui-smoke.ps1"));
        Assert.Contains("--quality-context", Read("run-desktop-ui-smoke.ps1"));
        Assert.Contains("ContextMetadataProjection.Project", source);
        Assert.Contains("ContextInputClassifier.ClassifyExplorerSelection", Read(
            "src", "LongBetterWindows.Host", "Interaction", "ExplorerContextProvider.cs"));
        Assert.Contains("app._startupOptions.OpenSuperPanelForQuality", Read(
            "src", "LongBetterWindows.Host", "App.xaml.cs"));
        Assert.DoesNotContain("CommandPaletteWindow.ShowPalette();", source);
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
        var settings = Read("src", "LongBetterWindows.Host", "Views", "SettingsPageControl.xaml");
        var zhResources = Read("src", "LongBetterWindows.Host", "i18n", "zh-CN.json");
        var gestures = Read("src", "LongBetterWindows.Host", "Services", "MouseGestureService.cs");

        Assert.DoesNotContain("EmbeddedPluginSurface", mainXaml);
        Assert.DoesNotContain("ShowEmbeddedPlugin", mainSource);
        Assert.DoesNotContain("CloseEmbeddedSurfaceAsync", mainSource);
        Assert.Contains("WorkspaceShell.DetachActivePluginRuntime()", mainSource);
        Assert.Contains("ShowPluginRuntimeModuleAsync", mainSource);
        Assert.Contains("PreviewKeyDown=\"Window_PreviewKeyDown\"", pluginXaml);
        Assert.Contains("i18n.pluginWindow.back", pluginXaml);
        Assert.Contains("Key.Escape", pluginSource);
        Assert.Contains("DefaultPresentation", presentation);
        Assert.Contains("ShowDetachedWindow", presentation);
        Assert.Contains("i18n.settings.gesture.title", settings);
        Assert.Contains("超级面板鼠标手势", zhResources);
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
        var pluginWindow = File.ReadAllText(Path.Combine(root, "src", "LongBetterWindows.Host", "Views", "PluginWindowHost.xaml.cs"));
        var pluginPlacement = File.ReadAllText(Path.Combine(root, "src", "LongBetterWindows.Host", "Views", "PluginWindowPlacement.cs"));

        Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", project);
        Assert.Contains("PerMonitorV2", manifest);
        Assert.Contains("True/PM", manifest);
        Assert.DoesNotContain("SystemParameters.WorkArea", pluginWindow);
        Assert.Contains("PluginWindowPlacement.TryApply", pluginWindow);
        Assert.Contains("ContentRendered +=", pluginWindow);
        Assert.Contains("WmDisplayChange", pluginWindow);
        Assert.Contains("SpiSetWorkArea", pluginWindow);
        Assert.Contains("TryConstrainToNearestWorkArea", pluginWindow);
        Assert.Contains("GetDpiForWindow", pluginPlacement);
        Assert.Contains("MonitorFromWindow", pluginPlacement);
        Assert.Contains("BuildNativePluginsForPublish", project);
        Assert.Contains("RemoveProperties=\"RuntimeIdentifier;SelfContained\"", project);
        Assert.Contains("原生插件仅发布运行所需的 Manifest 与 DLL", project);
        Assert.Contains("<RemoveDir Directories=\"$(OutputPath)Plugins", project);
        Assert.Contains("CopyPluginsToPublish", project);
        Assert.Contains("$(PublishDir)Plugins", project);
        Assert.Contains("<Version>1.11.0-rc.4</Version>", project);
        Assert.Contains("<AssemblyVersion>1.11.0.0</AssemblyVersion>", project);
    }

    [Fact]
    public void ProductVersion_IsExposedConsistentlyToNativeAndWebUi()
    {
        var app = Read("src", "LongBetterWindows.Host", "App.xaml.cs");
        var webDispatcher = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginHostDispatcher.cs");
        var developerPage = Read(
            "src", "LongBetterWindows.Host", "Views", "DeveloperPageControl.xaml.cs");

        Assert.Contains("AssemblyInformationalVersionAttribute", app);
        Assert.Contains("App.ProductVersion", webDispatcher);
        Assert.Contains("App.ProductVersion", developerPage);
        Assert.Contains("developer.about.version", developerPage);
    }

    [Fact]
    public void ReleaseCandidateVersion_IsConsistentAcrossPackagingAndIpcFixture()
    {
        const string version = "1.11.0-rc.4";
        var project = Read("src", "LongBetterWindows.Host", "LongBetterWindows.Host.csproj");
        var release = Read("release.ps1");
        var installerBuild = Read("build-installer.ps1");
        var installer = Read("installer", "LongAssistant.iss");
        var helloFixture = Read("tests", "fixtures", "ipc", "host-hello.response.json");

        Assert.Contains($"<Version>{version}</Version>", project);
        Assert.Contains($"<InformationalVersion>{version}</InformationalVersion>", project);
        Assert.Contains($"[string] $Version = '{version}'", release);
        Assert.Contains($"[string] $Version = '{version}'", installerBuild);
        Assert.Contains($"#define AppVersion \"{version}\"", installer);
        Assert.Contains($"\"host_version\": \"{version}\"", helloFixture);
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
        Assert.Contains("schema_version = 1", release);
        Assert.Contains("release_eligible", release);
        Assert.Contains("distribution_channel = 'unsigned'", release);
        Assert.Contains("publisher_identity = 'unverified'", release);
        Assert.Contains("release_eligible = -not $sourceDirty", release);
        Assert.Contains("Get-FileHash", release);
        Assert.Contains("release-evidence-io.ps1", release);
        Assert.Contains("Write-NewJsonFileAtomically", release);
        Assert.Contains("Write-NewTextFileAtomically", release);
        Assert.DoesNotContain("$checksumLines | Set-Content", release);
        Assert.DoesNotContain("ConvertTo-Json -Depth 5 | Set-Content", release);
        Assert.Contains("[IO.Compression.ZipArchiveMode]::Create", release);
        Assert.Contains("Add-Type -AssemblyName System.IO.Compression", release);
        Assert.Contains(".Replace('\\', '/')", release);
        Assert.Contains("Release archive input escapes source root", release);
        Assert.DoesNotContain("Path]::GetRelativePath", release);
        Assert.Contains("Assert-ReleaseZipLayout", release);
        Assert.Contains("PluginManifests=$manifestCount", release);
        Assert.DoesNotContain("Compress-Archive", release);
        Assert.Contains("WaitForExit($smokeTimeoutMilliseconds)", release);
        Assert.Contains("pluginCount -ne $expectedPluginCount", release);
        Assert.Contains("Remove-PublishRuntimeState", release);
        Assert.Contains("runtime-generated WebView2 state was packaged", release);
        Assert.Contains("build-installer.ps1", release);
        Assert.Contains("installers = $installers", release);
    }

    [Fact]
    public void InstallerPipeline_ProducesPerUserExeWithUpgradeAndUninstallSupport()
    {
        var build = Read("build-installer.ps1");
        var installer = Read("installer", "LongAssistant.iss");

        Assert.Contains("expectedPluginCount = 25", build);
        Assert.Contains("expectedCommandCount = 42", build);
        Assert.Contains("JRSoftware.InnoSetup", build);
        Assert.Contains("inno-setup-exe", build);
        Assert.Contains("requires_elevation = $false", build);
        Assert.Contains("Get-FileHash", build);
        Assert.Contains("AppId={{7B95AC62-8C5A-45E3-B0F0-A77EA8CF318A}", installer);
        Assert.Contains(@"DefaultDirName={localappdata}\Programs\LongAssistant", installer);
        Assert.Contains("PrivilegesRequired=lowest", installer);
        Assert.Contains("UsePreviousAppDir=yes", installer);
        Assert.Contains("UninstallDisplayIcon={app}\\LongBetterWindows.Host.exe", installer);
        Assert.Contains(
            @"Type: filesandordirs; Name: ""{app}\LongBetterWindows.Host.exe.WebView2""",
            installer);
        Assert.Contains("LongAssistant-Setup-v{#AppVersion}", installer);
        Assert.Contains("SetupIconFile=..\\Assets\\app.ico", installer);
    }

    [Fact]
    public void PluginManagement_UsesRecyclingVirtualizationAndRoutesDetailsToWorkspace()
    {
        var xaml = Read("src", "LongBetterWindows.Host", "Views", "PluginManagementControl.xaml");
        Assert.Contains("x:Name=\"PluginsPanel\"", xaml);
        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", xaml);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", xaml);
        Assert.Contains("<VirtualizingStackPanel", xaml);
        Assert.Contains("DarkScrollBarStyle", xaml);
        Assert.Contains("x:Key=\"PluginListCard\"", xaml);
        Assert.Contains("<Setter Property=\"Effect\" Value=\"{x:Null}\"", xaml);

        var code = Read("src", "LongBetterWindows.Host", "Views", "PluginManagementControl.xaml.cs");
        var main = Read("src", "LongBetterWindows.Host", "MainWindow.xaml.cs");
        Assert.Contains("PluginActions_Click", code);
        Assert.Contains("OpenPluginAsync", code);
        Assert.Contains("PluginMainUiLauncher.OpenAsync", code);
        Assert.Contains("PluginMainUiLauncher.OpenAsync", main);
        Assert.DoesNotContain(
            "entry.State != PluginState.Running",
            code);
        Assert.DoesNotContain("mainUi.ShowMainUI()", code);
        Assert.DoesNotContain("mainUi.ShowMainUI()", main);
        Assert.DoesNotContain("ToolCenter.Visibility", main);
        Assert.Contains("PluginSettingsRequested?.Invoke", code);
        Assert.DoesNotContain("OpenCapabilityDetails", code);
        Assert.Contains("TogglePluginAsync", code);
        Assert.Contains("AddMenuAction", code);
        Assert.Equal(1, xaml.Split("Click=\"PluginActions_Click\"").Length - 1);
        Assert.Contains(
            "Foreground=\"{DynamicResource Long.Brush.Text.Secondary}\"",
            xaml);
        Assert.DoesNotContain("Click=\"OpenPlugin_Click\"", xaml);
        Assert.DoesNotContain("Click=\"PluginToggle_Click\"", xaml);
        Assert.DoesNotContain("Click=\"PluginSettings_Click\"", xaml);
        Assert.DoesNotContain("Click=\"CapabilityDetails_Click\"", xaml);
        Assert.Contains("Interlocked.Exchange(ref _refreshDebounce, next)", code);
        Assert.Contains("Task.Delay(150, source.Token)", code);
        Assert.Contains("previous.Cancel()", code);
        Assert.Contains("DispatcherPriority.ContextIdle", code);
        Assert.Contains("IDisposable", code);
        Assert.Contains("_pluginStore.PluginsChanged -= OnPluginsChanged", code);
        Assert.Contains("PluginsPanel.ItemsSource = null", code);
        Assert.Contains("CapabilitySummary", code);
        Assert.DoesNotContain("<ItemsControl ItemsSource=\"{Binding VisibleCapabilities}\">", xaml);

        var toolCenterXaml = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml");
        var toolCenterCode = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml.cs");
        var mainWindowCode = Read(
            "src",
            "LongBetterWindows.Host",
            "MainWindow.xaml.cs");
        Assert.Contains("x:Name=\"PluginManagementHost\"", toolCenterXaml);
        Assert.DoesNotContain("<local:PluginManagementControl", toolCenterXaml);
        Assert.Contains("plugins = new PluginManagementControl()", toolCenterCode);
        Assert.Contains("plugins.Refresh()", toolCenterCode);
        Assert.Contains("ReleasePluginManagementPage()", toolCenterCode);
        Assert.Contains("PluginManagementHost.Content = null", toolCenterCode);
        Assert.Contains("plugins.Dispose()", toolCenterCode);
        Assert.Contains("OpenPluginSettingsModule", toolCenterCode);
        Assert.Contains("_pluginSettingsModules", toolCenterCode);
        Assert.Contains("PanelPluginSettings", toolCenterXaml);
        Assert.Contains("PluginSettingsModuleHost", toolCenterXaml);
        Assert.Contains("SystemHost.Content ??= new SystemIntegrationPageControl()", toolCenterCode);
        Assert.Contains("new SettingsPageControl()", toolCenterCode);
        Assert.Contains("OpenPluginsForQuality", toolCenterCode);
        Assert.DoesNotContain("CreatePluginCard", toolCenterCode);
        Assert.DoesNotContain("PluginToggle_Click", toolCenterCode);
        var showPageStart = toolCenterCode.IndexOf(
            "private void ShowManagementPage",
            StringComparison.Ordinal);
        var releaseMethodStart = toolCenterCode.IndexOf(
            "private void ReleasePluginManagementPage",
            StringComparison.Ordinal);
        Assert.True(showPageStart >= 0);
        Assert.True(releaseMethodStart > showPageStart);
        Assert.DoesNotContain(
            "ReleasePluginManagementPage()",
            toolCenterCode[showPageStart..releaseMethodStart]);
        Assert.DoesNotContain("ShowPage(", toolCenterCode);
        Assert.DoesNotContain("WorkspaceLegacyModuleCatalog", toolCenterCode);
        Assert.DoesNotContain("Tag=\"overview\"", toolCenterXaml);
        Assert.Contains(
            "Action<WorkspaceManagementPage>? PageNavigationRequested",
            toolCenterCode);
        Assert.DoesNotContain("OpenLegacyWorkspacePageAsync", mainWindowCode);
        Assert.Contains("OpenManagementPageAsync", mainWindowCode);
        Assert.Contains("WorkspaceManagementModuleCatalog.Create", mainWindowCode);
    }

    [Fact]
    public void ToolCenterOverview_UsesLightweightCards()
    {
        var xaml = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml");
        var developerXaml = Read(
            "src", "LongBetterWindows.Host", "Views", "DeveloperPageControl.xaml");
        var developerCode = Read(
            "src", "LongBetterWindows.Host", "Views", "DeveloperPageControl.xaml.cs");
        var systemXaml = Read(
            "src", "LongBetterWindows.Host", "Views", "SystemIntegrationPageControl.xaml");
        var systemCode = Read(
            "src", "LongBetterWindows.Host", "Views", "SystemIntegrationPageControl.xaml.cs");
        var settingsXaml = Read(
            "src", "LongBetterWindows.Host", "Views", "SettingsPageControl.xaml");
        var settingsCode = Read(
            "src", "LongBetterWindows.Host", "Views", "SettingsPageControl.xaml.cs");

        Assert.Contains("x:Key=\"ManagementCard\"", xaml);
        Assert.Contains("BasedOn=\"{StaticResource LongCard}\"", xaml);
        Assert.Equal(0, xaml.Split("Style=\"{StaticResource ManagementCard}\"").Length - 1);
        Assert.Contains("x:Key=\"OverviewCard\"", xaml);
        Assert.Contains("BasedOn=\"{StaticResource ManagementCard}\"", xaml);
        Assert.Contains("<Setter Property=\"Effect\" Value=\"{x:Null}\"", xaml);
        Assert.Equal(5, xaml.Split("Style=\"{StaticResource OverviewCard}\"").Length - 1);

        var code = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml.cs");
        Assert.Contains("App.ShowManagementCardShadowsForQuality", code);
        Assert.Contains("ApplyManagementCardShadowsForQuality", code);
        Assert.Contains("x:Name=\"DeveloperHost\"", xaml);
        Assert.DoesNotContain("<local:DeveloperPageControl", xaml);
        Assert.Contains("DeveloperHost.Content ??= new DeveloperPageControl()", code);
        Assert.Equal(4, developerXaml.Split("Style=\"{StaticResource DeveloperCard}\"").Length - 1);
        Assert.Contains("VerticalAlignment=\"Top\"", developerXaml);
        Assert.Contains("MaxHeight=\"240\"", developerXaml);
        Assert.Contains("IDisposable", developerCode);
        Assert.Contains("PluginsChanged -= OnPluginsChanged", developerCode);
        Assert.Contains("x:Name=\"SystemHost\"", xaml);
        Assert.DoesNotContain("<local:SystemIntegrationPageControl", xaml);
        Assert.Contains("SystemHost.Content ??= new SystemIntegrationPageControl()", code);
        Assert.Equal(5, systemXaml.Split("Style=\"{StaticResource SystemCard}\"").Length - 1);
        Assert.Contains("VerticalAlignment=\"Top\"", systemXaml);
        Assert.Contains("IDisposable", systemCode);
        Assert.Contains("LanguageChanged -= OnLanguageChanged", systemCode);
        Assert.True(
            systemCode.Split("SetSparsePackageBusy(false)").Length - 1 >= 3);
        Assert.Contains("x:Name=\"SettingsHost\"", xaml);
        Assert.DoesNotContain("<local:SettingsPageControl", xaml);
        Assert.Contains("SettingsHost.Content is null", code);
        Assert.Contains("new SettingsPageControl()", code);
        Assert.Equal(7, settingsXaml.Split("Style=\"{StaticResource SettingsCard}\"").Length - 1);
        Assert.Contains("x:Name=\"BrokerToggle\"", settingsXaml);
        Assert.Contains("Long.Settings.CategoryList", settingsXaml);
        Assert.Contains("Long.Settings.CategorySelector", settingsXaml);
        Assert.Contains("NavigateToCategory", settingsCode);
        Assert.Contains("settings.category.appearance", settingsXaml);
        Assert.Contains("Long.Settings.CategoryItem.updates", settingsXaml);
        Assert.Contains("ExportBrokerDiagnostics_Click", settingsCode);
        Assert.Contains("VerticalAlignment=\"Top\"", settingsXaml);
        Assert.Contains("IDisposable", settingsCode);
        Assert.Contains("UpdateUiState", settingsCode);
        Assert.Contains("LanguageApplied?.Invoke", settingsCode);
        Assert.Contains("_updateService?.Dispose()", settingsCode);
    }

    [Fact]
    public void MainWindow_SupportsMinimumQualityViewportAndResponsiveToolCenter()
    {
        var main = Read("src", "LongBetterWindows.Host", "MainWindow.xaml");
        var toolCenter = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml");
        var code = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml.cs");
        var pluginRail = Read(
            "src",
            "LongBetterWindows.Host",
            "Views",
            "InstalledPluginRailControl.xaml");

        Assert.Contains("MinWidth=\"720\" MinHeight=\"560\"", main);
        Assert.Contains("Width=\"220\"", pluginRail);
        Assert.DoesNotContain("x:Name=\"NavigationColumn\"", toolCenter);
        Assert.DoesNotContain("LongNavigationItem", toolCenter);
        Assert.Contains("x:Name=\"ManagementDestinationGrid\"", toolCenter);
        Assert.Contains("ManagementDestination_Click", code);
        Assert.Equal(
            9,
            toolCenter.Split(
                "automation:AutomationProperties.AutomationId=\"Long.Management.Destination.",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            9,
            toolCenter.Split(
                "KeyboardNavigation.TabIndex=\"",
                StringSplitOptions.None).Length - 1);
        Assert.Contains("KeyboardNavigation.TabNavigation=\"Local\"", toolCenter);
        Assert.Contains("ManagementDestinationGrid.Columns = isNarrow ? 2 : 4", code);
        Assert.Contains("x:Name=\"OverviewStatusCard\"", toolCenter);
        Assert.Contains("ApplyResponsiveLayout", code);
        Assert.Contains("width < 860", code);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"{Binding AutomationId}\"",
            Read(
                "src",
                "LongBetterWindows.Host",
                "Views",
                "WorkspaceShellControl.xaml"));
        Assert.Contains(
            "AutomationProperties.Name=\"{Binding CloseAutomationName}\"",
            Read(
                "src",
                "LongBetterWindows.Host",
                "Views",
                "WorkspaceShellControl.xaml"));
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
        Assert.Contains("SystemColors.HighlightTextColor", app);
        Assert.Contains("public static void ApplyTheme(bool isLight", app);
        Assert.Contains("highContrast ? !changed : changed", quality);
        Assert.Contains("system_palette_preserved", quality);
        Assert.Contains("App.ApplyTheme(_isLightMode, persist: true)",
            Read("src", "LongBetterWindows.Host", "Views", "SettingsPageControl.xaml.cs"));
        Assert.Contains("Long.Settings.Theme.Toggle",
            Read("src", "LongBetterWindows.Host", "Views", "SettingsPageControl.xaml"));
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
    public void PluginPagePerformanceProbe_IsOptInAndCapturesPageStages()
    {
        var app = Read("src", "LongBetterWindows.Host", "App.xaml.cs");
        var options = Read(
            "src",
            "LongBetterWindows.Host",
            "Services",
            "AppStartupOptions.cs");
        var trace = Read(
            "src",
            "LongBetterWindows.Host",
            "Services",
            "PluginPagePerformanceTrace.cs");
        var quality = Read(
            "src",
            "LongBetterWindows.Host",
            "Services",
            "QualityRuntimeService.cs");
        var windowMessages = Read(
            "src",
            "LongBetterWindows.Host",
            "Services",
            "WindowMessageActivityTrace.cs");
        var plugins = Read(
            "src",
            "LongBetterWindows.Host",
            "Views",
            "PluginManagementControl.xaml.cs");

        Assert.Contains("--quality-plugin-page-performance-report", options);
        Assert.Contains("--quality-skip-auto-start-plugin", options);
        Assert.Contains("--quality-hide-window-during-idle", options);
        Assert.Contains("new PluginPagePerformanceTrace", app);
        Assert.Contains("suppressedAutoStartPluginIds", app);
        Assert.Contains("RunPluginPagePerformanceProbeAsync", quality);
        Assert.Contains("checkpoints.Add(1_000)", quality);
        Assert.Contains("checkpoints.Add(3_000)", quality);
        Assert.Contains("\"plugin_page_settled\"", quality);
        Assert.Contains("ui_thread_id", trace);
        Assert.Contains("top_threads", trace);
        Assert.Contains("window_message_checkpoints", trace);
        Assert.Contains("_source.AddHook", windowMessages);
        Assert.Contains("_source.RemoveHook", windowMessages);
        Assert.Contains("window.Hide", quality);
        Assert.Contains("plugin_page_constructor_begin", plugins);
        Assert.Contains("plugin_projection_begin", plugins);
        Assert.Contains("plugin_projection_end", plugins);
        Assert.Contains("plugin_page_first_idle", plugins);
        Assert.Contains("realized_container_count", trace);
        Assert.Contains("visual_descendant_count", trace);
        Assert.Contains("animated_property_count", trace);
        Assert.Contains("gc_committed_mb", trace);
        Assert.DoesNotContain("DispatcherTimer", trace);
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
    public void PluginBuild_ExcludesRuntimeSettingsFromBuildAndPublishArtifacts()
    {
        var project = Read(
            "src",
            "LongBetterWindows.Host",
            "LongBetterWindows.Host.csproj");

        Assert.Contains(
            "<RemoveDir Directories=\"$(OutputPath)Plugins\\ClipboardTool;",
            project);
        Assert.Equal(
            18,
            project.Split(
                "/XD bin obj /XF config.json",
                StringSplitOptions.None).Length - 1);
        Assert.Contains(
            "$(PublishDir)Plugins&quot; /S /XF config.json",
            project);
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
    public void PluginRuntime_DefersInstanceCreationUntilLifecycleStart()
    {
        var entry = Read(
            "src", "LongBetterWindows.Host", "Engine", "PluginEntry.cs");
        var registry = Read(
            "src", "LongBetterWindows.Host", "Engine", "PluginRegistry.cs");
        var scanner = Read(
            "src", "LongBetterWindows.Host", "Engine", "PluginScanner.cs");
        var launcher = Read(
            "src", "LongBetterWindows.Host", "Interaction",
            "PluginMainUiLauncher.cs");

        Assert.Contains("EnsureActivatedAsync", entry);
        Assert.Contains("_activationGate.WaitAsync", entry);
        Assert.Contains("LifecycleGate.WaitAsync", registry);
        Assert.Contains("RegisterDeferred", registry);
        Assert.Contains("BeginChangeBatch", registry);
        Assert.Contains("BeginChangeBatch", scanner);
        Assert.Contains("ActivatePluginAsync", scanner);
        Assert.Contains("运行时等待按需激活", scanner);
        Assert.Contains("persistAutoStart: false", launcher);
    }

    [Fact]
    public void ClipboardHistory_UsesNativeBackgroundWithoutAHiddenWebView()
    {
        var manifest = Read("src", "ClipboardHistory", "manifest.json");
        var page = Read("src", "ClipboardHistory", "index.html");
        var background = Read(
            "src",
            "ClipboardHistoryBackground",
            "ClipboardHistoryBackgroundPlugin.cs");
        var runtime = Read(
            "src",
            "LongBetterWindows.Host",
            "Engine",
            "PluginRuntimeLoader.cs");
        var hostProject = Read(
            "src",
            "LongBetterWindows.Host",
            "LongBetterWindows.Host.csproj");

        Assert.Contains("\"background\"", manifest);
        Assert.Contains("ClipboardHistory.Background.dll", manifest);
        Assert.Contains("IPluginOpenRequestSource", background);
        Assert.Contains("StartMonitoringAsync", background);
        Assert.Contains("StorageKey = \"clipboard_history\"", background);
        Assert.Contains("CompareExchangeAsync", background);
        Assert.Contains("long.storage.compareExchange", page);
        Assert.DoesNotContain("long.storage.set(storageKey", page);
        Assert.Contains("new WebPluginWithBackgroundAdapter", runtime);
        Assert.Contains(
            "ClipboardHistory.Background.dll",
            hostProject);
        Assert.DoesNotContain("long.hotkey.register", page);
    }

    [Fact]
    public void PluginManagement_ReleaseProbeUsesWeakReferenceAndFullCollection()
    {
        var toolCenter = Read(
            "src",
            "LongBetterWindows.Host",
            "Views",
            "ToolCenterControl.xaml.cs");
        var qualityRuntime = Read(
            "src",
            "LongBetterWindows.Host",
            "Services",
            "QualityRuntimeService.cs");

        Assert.Contains("ReleasePluginsForQuality", toolCenter);
        Assert.Contains("new WeakReference(PluginManagementHost.Content)", toolCenter);
        Assert.Contains("plugin_page_collected", qualityRuntime);
        Assert.Contains("GCCollectionMode.Forced", qualityRuntime);
        Assert.Contains("reference.IsAlive", qualityRuntime);
    }

    [Fact]
    public void TaskbarIdentityEvidence_AtomicallyAnnotatesHostReport()
    {
        var script = Read("verify-taskbar-identity.ps1");

        Assert.Contains("--quality-taskbar-identity-report", script);
        Assert.Contains("Taskbar identity evidence requires a clean", script);
        Assert.Contains("Taskbar identity evidence output already exists", script);
        Assert.Contains("source_commit", script);
        Assert.Contains("host_executable_sha256", script);
        Assert.Contains("release-evidence-io.ps1", script);
        Assert.Contains("originalReportHash", script);
        Assert.Contains("Update-JsonFileAtomically", script);
        Assert.DoesNotContain("Set-Content -LiteralPath $outputFile", script);
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
        Assert.Contains(
            "AutomationProperties.Name=\"{DynamicResource i18n.folderNote.hud.inputAutomationName}\"",
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
        var workflow = Read("src", "LongBetterWindows.Host", "Views", "WorkflowEditorControl.xaml");
        var workflowCode = Read(
            "src", "LongBetterWindows.Host", "Views", "WorkflowEditorControl.xaml.cs");
        var workflowUpgradeScript = Read("run-isolated-workflow-upgrade.ps1");
        var appResources = Read("src", "LongBetterWindows.Host", "App.xaml");
        var main = Read("src", "LongBetterWindows.Host", "MainWindow.xaml");
        var mainCode = Read("src", "LongBetterWindows.Host", "MainWindow.xaml.cs");
        var mainAutomation = Read(
            "src", "LongBetterWindows.Host", "Automation", "QualityWorkflowAutomation.cs");
        var plugin = Read("src", "LongBetterWindows.Host", "Views", "PluginWindowHost.xaml");
        var workspace = Read(
            "src",
            "LongBetterWindows.Host",
            "Views",
            "WorkspaceShellControl.xaml");

        Assert.Contains("Long.CommandPalette.Search", palette);
        Assert.Contains("Long.CommandPalette.Results", palette);
        Assert.Contains("Long.Result.MoreActions", palette);
        Assert.Contains("Long.SuperPanel.Results", superPanel);
        Assert.Contains("Long.SuperPanel.OpenCommandCenter", superPanel);
        Assert.Contains("Long.Workflow.ExecutionReview", workflow);
        Assert.Contains("Long.Workflow.ExecutionReview.Cancel", workflow);
        Assert.Contains("Long.Workflow.ExecutionResult.Title", workflow);
        Assert.Contains("Long.Workflow.TerminalOutput.Value", workflow);
        Assert.Contains("Long.Workflow.TerminalOutput.Clear", workflow);
        Assert.Contains("Long.Workflow.Duplicate", workflow);
        Assert.Contains("Long.Workflow.Templates", workflow);
        Assert.Contains("Long.Workflow.Id", workflow);
        Assert.Contains("Long.Workflow.Name", workflow);
        Assert.Contains("Long.Icon.Copy", workflow);
        Assert.Contains("CommandWorkflowTemplateCatalog", Read(
            "src", "LongBetterWindows.Host", "Interaction",
            "CommandWorkflowTemplateCatalog.cs"));
        Assert.Contains("_session.PreviewTemplateAsync", workflowCode);
        Assert.Contains("WorkflowTemplates\\**\\*", Read(
            "src", "LongBetterWindows.Host", "LongBetterWindows.Host.csproj"));
        Assert.Contains("Long.Workflow.WideNavigation", workflow);
        Assert.Contains("Long.Workflow.CompactNavigation", workflow);
        Assert.Contains("Long.ToolCenter.ContentScroll", Read(
            "src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml"));
        Assert.Contains("HorizontalContentAlignment=\"Stretch\"", Read(
            "src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml"));
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", Read(
            "src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml"));
        Assert.Contains("HorizontalContentAlignment=\"Stretch\"", Read(
            "src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml"));
        Assert.Contains("Long.Workflow.ReviewCancel", main);
        Assert.Contains("BooleanToVisibilityConverter x:Key=\"BooleanToVisibility\"", appResources);
        Assert.DoesNotContain("Long.Plugin.EmbeddedTitle", main);
        Assert.DoesNotContain("Long.Plugin.Detach", main);
        Assert.Contains("Long.Workspace.PluginRuntime.Title", workspace);
        Assert.Contains("Long.Workspace.PluginRuntime.Detach", workspace);
        Assert.Contains("Long.Plugin.DetachedWindow", plugin);
        Assert.Contains("[LongDesktopInput]::TopLevelWindows", script);
        Assert.Contains("[LongDesktopInput]::WindowAction", script);
        Assert.Contains("--quality-window-automation", script);
        Assert.DoesNotContain("keybd_event", script);
        Assert.Contains("$valuePattern.SetValue('wifi')", script);
        Assert.Contains("automation_transport = 'quality_window_message'", script);
        Assert.Contains("physical_keyboard_validated = $false", script);
        Assert.Contains("$lastProbeError = $_.Exception.Message", script);
        Assert.Contains("shift_enter_copied_uri", script);
        Assert.Contains("secondary_menu_copied_uri", script);
        Assert.Contains("palette_shown_on_transition", script);
        Assert.Contains("escape_closed_detached_window", script);
        Assert.Contains("escape_closed_panel", script);
        Assert.Contains("--quality-workflows-dir", script);
        Assert.Contains("--quality-open-workflow", script);
        Assert.Contains("palette_enter_opened_review", script);
        Assert.Contains("super_panel_enter_opened_review", script);
        Assert.Contains("Long.Workflow.ReviewCancel", script);
        Assert.Contains("duplicate_action_completed", script);
        Assert.Contains("duplicate_remained_unsaved", script);
        Assert.Contains("source_file_preserved", script);
        Assert.Contains("source_file_sha256", script);
        Assert.Contains("wide_layout_announced", script);
        Assert.Contains("compact_layout_announced", script);
        Assert.Contains("terminal_output_length", script);
        Assert.Contains("terminal_output_bounded_scroll", script);
        Assert.Contains("MaxHeight=\"120\"", workflow);
        Assert.Contains("Text=\"{Binding Value, Mode=OneWay}\"", workflow);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", workflow);
        Assert.Contains("isolated_report_written", script);
        Assert.Contains("[switch] $WorkflowOutputOnly", script);
        Assert.Contains(
            "LongBetterWindows.Quality.WindowAction.v1",
            script);
        Assert.Contains("Long.Workflow.TerminalOutput.ApproveTopLevel", main);
        Assert.Contains("Long.Workflow.ReviewConfirmTopLevel", main);
        Assert.Contains("Long.Workflow.TerminalOutput.ClearTopLevel", main);
        Assert.Contains("EditorConfigurationPanel", workflow);
        Assert.Contains("LongBetterWindows.Quality.WorkflowAction.v1", mainAutomation);
        Assert.Contains("QualityWorkflowAutomationEnabled", mainCode);
        Assert.Contains("WorkflowAutomationWndProc", mainCode);
        Assert.Contains("Invoke-WindowWorkflowAction", script);
        Assert.Contains("[LongDesktopInput]::WorkflowMessage", script);
        Assert.Contains("SendMessageTimeout", script);
        Assert.Contains("UpgradePluginPackage", mainAutomation);
        Assert.Contains("QueryPluginUpgradeStatus", mainAutomation);
        Assert.Contains("QueryExecutionRejected", mainAutomation);
        Assert.Contains("RunTerminalOutputExportMatrix", mainAutomation);
        Assert.Contains("QueryTerminalOutputExportStatus", mainAutomation);
        Assert.Contains("ExecutionReviewPanel.Visibility == Visibility.Visible", workflowCode);
        Assert.Contains("--quality-workflow-upgrade-package", workflowUpgradeScript);
        Assert.Contains("--quality-terminal-export-dir", script);
        Assert.Contains("[switch] $WorkflowExportMatrix", script);
        Assert.Contains("host-export-matrix.json", script);
        Assert.Contains("icacls.exe", script);
        Assert.Contains("-ItemType Junction", script);
        Assert.Contains("zero_steps_executed", workflowUpgradeScript);
        Assert.Contains("transaction_directories_cleaned", workflowUpgradeScript);
        Assert.DoesNotContain("Click-AutomationElement $outputTerminalApproval", script);
        Assert.DoesNotContain("Click-AutomationElement $outputReviewConfirm", script);
        Assert.DoesNotContain("Click-AutomationElement $clearTerminalOutput", script);
        Assert.Contains(";layout:{(compact ? \"compact\" : \"wide\")};width:{Math.Round(width)}", mainCode);
        Assert.Contains("'--quality-width', '720'", script);
        Assert.Contains("$attempt -le 5", script);
        Assert.Contains("$selection.Current.IsSelected", script);
        Assert.Contains("$element.SetFocus()", script);
        Assert.Contains("function Set-AutomationFocus", script);
        Assert.Contains("Set-AutomationFocus {", script);
        Assert.Contains("function Find-VisibleDescendantByAutomationId", script);
        Assert.Contains("-not $match.Current.IsOffscreen", script);
        Assert.Contains("--quality-storage-path", script);
        Assert.Contains("function Wait-CommandFeedback", script);
        Assert.Contains("Custom aliases saved.", script);
        Assert.Contains("Start-Sleep -Milliseconds 120", script);
        Assert.Contains("Long.Management.Destination.Market", script);
        Assert.Contains(
            "Long.Workspace.ModuleTab.marketplace:catalog",
            script);
        Assert.Contains(
            "Long.Workspace.ModuleClose.settings:root",
            script);
        Assert.Contains("coordinate_clicks_used = $false", script);
        Assert.Contains("destination_focus_order = $destinationFocusOrder", script);
        Assert.Contains("physical_narrator_validated = $false", script);
        Assert.Contains("Close Settings", script);
        Assert.Contains("Close Plugin Market", script);
        Assert.Contains(
            "Workflow review search did not receive keyboard focus before selection.",
            script);
        Assert.Contains("QualityWindowAction.SelectDeterministicResult", Read(
            "src",
            "LongBetterWindows.Host",
            "Views",
            "CommandPaletteWindow.xaml.cs"));
        Assert.Contains("Current.IsSelected", script);
        Assert.Contains("selection_transport = 'quality_window_message'", script);
        Assert.Contains("execution_was_not_confirmed", script);
        Assert.DoesNotContain("Invoke-AutomationElement $paletteWorkflowConfirm", script);
        Assert.DoesNotContain("Invoke-AutomationElement $panelWorkflowConfirm", script);
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
        var workspace = Read(
            "src", "LongBetterWindows.Host", "Views", "WorkspaceShellControl.xaml");
        var app = Read("src", "LongBetterWindows.Host", "App.xaml.cs");

        Assert.Contains("Long.Workspace.Search", workspace);
        Assert.DoesNotContain("Long.Marketplace.Search", market);
        Assert.Contains("Long.Marketplace.Results", market);
        Assert.Contains("Long.Marketplace.ConfirmCancel", market);
        Assert.Contains("Long.Marketplace.Back", market);
        Assert.Contains("AutomationProperties.ItemStatus", market);
        Assert.Contains("x:Name=\"MarketHeroTitle\"", market);
        Assert.Contains("TextWrapping=\"Wrap\"", market);
        Assert.Contains("Long.Marketplace.Uninstall", script);
        Assert.Contains("Long.Marketplace.ConfirmCancel", script);
        Assert.Contains("ControlType.ListItem", script);
        Assert.Contains("Long.Marketplace.ConfirmAction", script);
        Assert.Contains("uninstall_focus_restored", script);
        Assert.DoesNotContain("Invoke-AutomationElement $confirmAction", script);
        Assert.Contains("installed_state_preserved", script);
        Assert.Contains("--quality-high-contrast", script);
        Assert.Contains("--quality-reduce-motion", script);
        Assert.Contains("requested_state_confirmed", script);
        Assert.Contains("Quality accessibility mode:", app);
    }

    [Fact]
    public void WorkspaceShell_UsesScopedSearchFocusBookmarksAndSingleLayerEscape()
    {
        var shell = Read(
            "src", "LongBetterWindows.Host", "Views", "WorkspaceShellControl.xaml.cs");
        var main = Read("src", "LongBetterWindows.Host", "MainWindow.xaml.cs");
        var market = Read(
            "src", "LongBetterWindows.Host", "Views", "MarketplaceControl.xaml.cs");
        var pluginXaml = Read(
            "src", "LongBetterWindows.Host", "Views", "PluginManagementControl.xaml");

        Assert.Contains("WorkspaceSearchSession", shell);
        Assert.Contains("TimeSpan.FromMilliseconds(180)", shell);
        Assert.Contains("FocusScopedSearch", shell);
        Assert.Contains("WorkspaceEscapeRouter.Route", main);
        Assert.Contains("WorkspaceFocusBookmarkStore", main);
        Assert.Contains("key == Key.K", main);
        Assert.Contains("RememberConfirmationFocus", market);
        Assert.Contains("DismissConfirmation", market);
        Assert.DoesNotContain("PluginSearchBox", pluginXaml);
    }

    [Fact]
    public void WorkspacePluginRail_IsVirtualizedIncrementalAndForwardsIntents()
    {
        var xaml = Read(
            "src", "LongBetterWindows.Host", "Views", "InstalledPluginRailControl.xaml");
        var code = Read(
            "src", "LongBetterWindows.Host", "Views", "InstalledPluginRailControl.xaml.cs");
        var projection = Read(
            "src", "LongBetterWindows.Host", "Interaction",
            "InstalledPluginRailProjection.cs");
        var shell = Read(
            "src", "LongBetterWindows.Host", "Views", "WorkspaceShellControl.xaml");

        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", xaml);
        Assert.Contains("VirtualizationMode=\"Recycling\"", xaml);
        Assert.Contains("<Setter Property=\"Height\" Value=\"54\"", xaml);
        Assert.Contains("ImageFailed=\"PluginIcon_ImageFailed\"", xaml);
        Assert.Contains("Long.Workspace.PluginRail.Search", xaml);
        Assert.Contains("_plugins.PluginsChanged += Plugins_PluginsChanged", code);
        Assert.Contains("InstalledPluginRailProjection.Reconcile", code);
        Assert.Contains("PluginSettingsRequested?.Invoke", code);
        Assert.Contains("PluginRunRequested?.Invoke", code);
        Assert.Contains("current[targetIndex] != target", projection);
        Assert.Contains("<local:InstalledPluginRailControl", shell);
    }

    [Fact]
    public void WorkspacePluginRail_IsLimitedToManagementContext()
    {
        var shell = Read(
            "src",
            "LongBetterWindows.Host",
            "Views",
            "WorkspaceShellControl.xaml.cs");

        Assert.Contains("SetPluginRuntimePresentation(isVisible: true)", shell);
        Assert.Contains("SetPluginRuntimePresentation(isVisible: false)", shell);
        Assert.Contains("WorkspaceChromePolicy.ShowsInstalledPluginRail", shell);

        var desktopSmoke = Read("run-desktop-ui-smoke.ps1");
        Assert.Contains("market_plugin_rail_visible", desktopSmoke);
        Assert.Contains("settings_plugin_rail_hidden", desktopSmoke);
        Assert.Contains("plugin_rail_hidden_in_runtime", desktopSmoke);
        Assert.Contains("plugin_rail_hidden_after_restore", desktopSmoke);
    }

    [Fact]
    public void PluginSettingsModule_EmbedsSettingsAndKeepsLifecycleInHost()
    {
        var xaml = Read(
            "src", "LongBetterWindows.Host", "Views",
            "PluginSettingsModuleControl.xaml");
        var code = Read(
            "src", "LongBetterWindows.Host", "Views",
            "PluginSettingsModuleControl.xaml.cs");
        var main = Read("src", "LongBetterWindows.Host", "MainWindow.xaml.cs");
        var resolver = Read(
            "src", "LongBetterWindows.Host", "Interaction",
            "WorkspaceModuleAddress.cs");
        var desktopSmoke = Read("run-desktop-ui-smoke.ps1");

        Assert.Contains("Long.Workspace.PluginSettings.Tabs", xaml);
        Assert.Contains("Long.Workspace.PluginSettings.Tab.Commands", xaml);
        Assert.Contains("Long.Workspace.PluginSettings.Commands", xaml);
        Assert.Contains("Long.Workspace.PluginSettings.CommandHotkey.", xaml);
        Assert.Contains("{Binding EnabledAutomationName}", xaml);
        Assert.Contains("{Binding AliasesAutomationName}", xaml);
        Assert.Contains("{Binding HotkeyAutomationName}", xaml);
        Assert.Contains("{Binding HotkeyStatusAutomationName}", xaml);
        Assert.Contains("Long.Workspace.PluginSettings.CommandHotkeyStatus.", xaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);
        Assert.Contains("CommandHotkeySave_Click", code);
        Assert.Contains("CommandHotkeyClear_Click", code);
        Assert.Contains("CommandPin_Click", code);
        Assert.Contains("BuildCommands", code);
        Assert.Contains("Long.Workspace.PluginSettings.Content", xaml);
        Assert.Contains("<local:CapabilityDetailPanel", xaml);
        Assert.Contains("CreateSettingsUI()", code);
        Assert.Contains("PluginRunRequested?.Invoke", code);
        Assert.Contains("PluginToggleRequested?.Invoke", code);
        Assert.DoesNotContain("StartPluginAsync", code);
        Assert.DoesNotContain("StopPluginAsync", code);
        Assert.DoesNotContain("DynamicResource plugins.", xaml);
        Assert.Contains("OpenPluginSettingsModule", main);
        Assert.Contains("HandleMissingPluginModulesAsync", main);
        Assert.Contains("await plugin.EnsureActivatedAsync()", resolver);
        Assert.Contains("PluginCommandManagementOnly", desktopSmoke);
        Assert.Contains("pin_state_restored", desktopSmoke);
        Assert.Contains("command_hotkey_persisted", desktopSmoke);
        Assert.Contains("command_hotkey_restored", desktopSmoke);
        Assert.Contains("unique command context", desktopSmoke);
        Assert.Contains("controls = $commandSemanticSnapshot", desktopSmoke);
        Assert.DoesNotContain(
            "plugin.Instance is not IHasSettingsUI",
            resolver);
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
        var widgetRuntime = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginRuntime.Widget.cs");
        var protocol = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginBridgeProtocol.cs");
        var dispatcher = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginHostDispatcher.cs");
        var widgetLifecycle = Read("src", "LongBetterWindows.Host", "Engine", "WidgetLifecycleCoordinator.cs");
        var widgetLayout = Read("src", "LongBetterWindows.Host", "Engine", "WidgetSurfaceLayout.cs");
        var widgetState = Read("src", "LongBetterWindows.Host", "Engine", "WidgetInstanceStateStore.cs");
        var widgetSurface = Read("src", "LongBetterWindows.Host", "Engine", "WebWidgetSurfaceSession.cs");
        var widgetSurfaceHost = Read("src", "LongBetterWindows.Host", "Views", "WebWidgetSurfaceHost.cs");
        var widgetCatalog = Read("src", "LongBetterWindows.Host", "Interaction", "WidgetCatalogProjection.cs");
        var widgetLayoutStore = Read("src", "LongBetterWindows.Host", "Interaction", "WidgetLayoutStore.cs");
        var widgetLayoutCoordinator = Read("src", "LongBetterWindows.Host", "Interaction", "WidgetLayoutCoordinator.cs");
        var widgetDashboard = Read("src", "LongBetterWindows.Host", "Views", "WidgetDashboardControl.xaml.cs");
        var widgetDashboardView = Read("src", "LongBetterWindows.Host", "Views", "WidgetDashboardControl.xaml");
        var workspaceAddress = Read("src", "LongBetterWindows.Host", "Interaction", "WorkspaceModuleAddress.cs");
        var toolCenter = Read("src", "LongBetterWindows.Host", "Views", "ToolCenterControl.xaml");
        var services = Read("src", "LongBetterWindows.Host", "Services", "ServicesInitializer.cs");
        var arguments = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginArguments.cs");
        var lifecycle = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginViewLifecycle.cs");

        Assert.Contains("WebPluginBridgeProtocol.ParseRequest", runtime);
        Assert.Contains("WebPluginBridgeProtocol.GetRequiredCapability", runtime);
        Assert.Contains("_hostDispatcher.DispatchAsync", runtime);
        Assert.Contains("SerializeResult", protocol);
        Assert.Contains("BuildInjectionScript", protocol);
        Assert.Contains("WidgetLifecycleCoordinator", runtime);
        Assert.Contains("NotifyWidgetLayoutChanged", widgetRuntime);
        Assert.Contains("_widgetLifecycle?.Mount()", runtime);
        Assert.Contains("_widgetLifecycle?.Dispose()", runtime);
        Assert.Contains("coordinator.MarkReady", Read("tests", "LongBetterWindows.Tests", "CoreTests.cs"));
        Assert.Contains("long.widget-mounted", widgetLifecycle);
        Assert.Contains("long.widget-suspend", widgetLifecycle);
        Assert.Contains("long.widget-resume", widgetLifecycle);
        Assert.Contains("long.widget-unmount", widgetLifecycle);
        Assert.Contains("ready-timeout", widgetLifecycle);
        Assert.Contains("long.widget-resized", widgetLifecycle);
        Assert.Contains("long.widget-visibility-changed", widgetLifecycle);
        Assert.Contains("WidgetSurfaceLayout", widgetLifecycle);
        Assert.Contains("TryFromLogicalSize", widgetLayout);
        Assert.Contains("MidpointRounding.AwayFromZero", widgetLayout);
        Assert.Contains("DpiScale", widgetLayout);
        Assert.Contains("new WebPluginBridgeContext(", widgetSurface);
        Assert.Contains("surface: \"widget\"", widgetSurface);
        Assert.Contains("declaredWidget.EntryPoint", widgetSurface);
        Assert.Contains("DefaultHiddenSuspendDelay", widgetSurface);
        Assert.Contains("PluginWidgetHiddenBehavior.Suspend", widgetSurface);
        Assert.Contains("_runtime.NotifyWidgetLayoutChanged", widgetSurface);
        Assert.Contains("_runtime.NotifyWidgetVisibilityChanged", widgetSurface);
        Assert.Contains("_runtime.SuspendWidget", widgetSurface);
        Assert.Contains("_runtime.ResumeWidget", widgetSurface);
        Assert.Contains("ActualWidth", widgetSurfaceHost);
        Assert.Contains("ActualHeight", widgetSurfaceHost);
        Assert.Contains("VisualTreeHelper.GetDpi(this)", widgetSurfaceHost);
        Assert.Contains("OnDpiChanged", widgetSurfaceHost);
        Assert.Contains("IsVisibleChanged +=", widgetSurfaceHost);
        Assert.Contains("SizeChanged +=", widgetSurfaceHost);
        Assert.Contains("_session.SetVisible(false, \"surface-unloaded\")", widgetSurfaceHost);
        Assert.DoesNotContain("PluginWindowHost", widgetSurfaceHost);
        Assert.DoesNotContain("PluginWorkspaceSession", widgetSurfaceHost);
        Assert.Contains("entry.Manifest.Widgets", widgetCatalog);
        Assert.Contains("ResolveIcon", widgetCatalog);
        Assert.Contains("SchemaVersion = 1", widgetLayoutStore);
        Assert.Contains("MaximumPlacements = 256", widgetLayoutStore);
        Assert.Contains("FileOptions.WriteThrough", widgetLayoutStore);
        Assert.Contains("File.Move(temporaryPath, _path, overwrite: true)", widgetLayoutStore);
        Assert.Contains("FileAttributes.ReparsePoint", widgetLayoutStore);
        Assert.Contains("MultipleInstancesNotAllowed", widgetLayoutCoordinator);
        Assert.Contains("PlacementOccupied", widgetLayoutCoordinator);
        Assert.Contains("Reconcile(", widgetLayoutCoordinator);
        Assert.Contains("new WebWidgetSurfaceSession", widgetDashboard);
        Assert.Contains("if (!_cards.TryGetValue", widgetDashboard);
        Assert.Contains("card.Host.SetGridSize", widgetDashboard);
        Assert.Contains("ServicesInitializer.Widgets.MoveResizeAsync", widgetDashboard);
        Assert.Contains("ServicesInitializer.Widgets.RemoveAsync", widgetDashboard);
        Assert.Contains("for (var column = 0; column < 24; column++)", widgetDashboard);
        Assert.Contains("AutomationProperties.SetAutomationId", widgetDashboard);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", widgetDashboardView);
        Assert.Contains("x:Name=\"PanelWidgets\"", toolCenter);
        Assert.Contains("\"widgets\" => WorkspaceModuleAddressKind.Widgets", workspaceAddress);
        Assert.Contains("WidgetCatalogProjection.Build", services);
        Assert.Contains("new WidgetLayoutStore", services);
        Assert.Contains("WidgetInstanceStateStore", dispatcher);
        Assert.Contains("WidgetGetInstanceState()", dispatcher);
        Assert.Contains("_widgetStateStore.SetAsync", dispatcher);
        Assert.Contains("HashIdentity", widgetState);
        Assert.Contains("File.Replace", widgetState);
        Assert.Contains("LocalApplicationData", widgetState);
        Assert.Contains("FileOps.MoveAsync", dispatcher);
        Assert.Contains("WidgetReady(args)", dispatcher);
        Assert.Contains("WebPluginArguments.GetJson", dispatcher);
        Assert.Contains("GetHeaders", arguments);
        Assert.Contains("CoreWebView2.NavigationStarting +=", lifecycle);
        Assert.Contains("_navigationPolicy.IsTrustedWebViewUri", lifecycle);
        Assert.Contains("SetVirtualHostNameToFolderMapping", lifecycle);
        Assert.Contains("AddWebResourceRequestedFilter", lifecycle);
        Assert.Contains("WebResourceRequested += OnWebResourceRequested", lifecycle);
        Assert.Contains("ShouldBlockWebResourceRequest", lifecycle);
        Assert.Contains("BuildContentSecurityPolicyResponseHeader", lifecycle);
        Assert.Contains("BuildContentSecurityPolicyInjectionScript", lifecycle);
        Assert.Contains("dispatcher.Invoke(Dispose)", lifecycle);
        Assert.Contains("dispatcher.InvokeAsync(() => PostMessageCore(json))", lifecycle);
        Assert.DoesNotContain("window.long =", runtime);
        Assert.DoesNotContain("FileOps.MoveAsync", runtime);
        Assert.DoesNotContain("CoreWebView2NavigationStartingEventArgs", runtime);
        Assert.True(runtime.Split('\n').Length < 170);
    }

    [Fact]
    public void WebPluginPresentation_IsSeparatedFromPluginStateAdapter()
    {
        var adapter = Read("src", "LongBetterWindows.Host", "Engine", "WebPluginAdapter.cs");
        var presentation = Read(
            "src", "LongBetterWindows.Host", "Engine", "WebPluginPresentationCoordinator.cs");
        var shell = Read(
            "src", "LongBetterWindows.Host", "Views", "WorkspaceShellControl.xaml");
        var address = Read(
            "src", "LongBetterWindows.Host", "Interaction", "WorkspaceModuleAddress.cs");
        var quality = Read(
            "src", "LongBetterWindows.Host", "Services", "QualityRuntimeService.cs");
        var options = Read(
            "src", "LongBetterWindows.Host", "Services", "AppStartupOptions.cs");
        var mainWindow = Read(
            "src", "LongBetterWindows.Host", "MainWindow.xaml.cs");
        var launcher = Read(
            "src", "LongBetterWindows.Host", "Interaction",
            "PluginMainUiLauncher.cs");
        var inputProbe = Read(
            "src", "LongBetterWindows.Host", "Services",
            "PluginRuntimeInputProbe.cs");
        var keyboardInput = Read(
            "src", "LongBetterWindows.Host", "Services",
            "QualityKeyboardInput.cs");

        Assert.Contains("_presentation.EnsureVisible()", adapter);
        Assert.Contains("_presentation.CloseVisibleSurface()", adapter);
        Assert.Contains("_presentation.ReleaseAsync()", adapter);
        Assert.Contains("ShowPluginRuntimeModuleAsync", presentation);
        Assert.Contains("PluginWorkspaceSession", presentation);
        Assert.Contains("PluginSurfaceCloseRouter.Route", presentation);
        Assert.Contains("PluginWindowHost", presentation);
        Assert.Contains("NotifyWindowClosedAsync", presentation);
        Assert.Contains("Long.Workspace.PluginRuntime", shell);
        Assert.Contains("<ContentControl x:Name=\"PluginRuntimeContent\"", shell);
        Assert.Contains("WorkspaceModuleAddressKind.PluginRuntime", address);
        Assert.Contains("RunPluginRuntimeSessionProbeAsync", quality);
        Assert.Contains("same_session_across_move", quality);
        Assert.Contains("background_state_ready", quality);
        Assert.Contains("background_resumed_ready", quality);
        Assert.Contains("webview_input_preserved", quality);
        Assert.Contains("webview_scroll_preserved", quality);
        Assert.Contains("physical_ctrl_d_sent", quality);
        Assert.Contains("Input.insertText", inputProbe);
        Assert.Contains("Input.dispatchKeyEvent", inputProbe);
        Assert.Contains("SendControlD", inputProbe);
        Assert.Contains("AttachThreadInput", keyboardInput);
        Assert.Contains("Size = 32", keyboardInput);
        Assert.Contains(
            "PluginState.Running or PluginState.Background",
            launcher);
        Assert.Contains("PluginMainUiLauncher.OpenAsync", mainWindow);
        Assert.Contains("--quality-plugin-runtime-session-report", options);
        Assert.DoesNotContain("PluginWindowHost", adapter);
        Assert.DoesNotContain("ShowEmbeddedPlugin", presentation);
        Assert.True(adapter.Split('\n').Length < 130);
    }

    [Fact]
    public void PluginScaffold_ExposesTheDocumentedScriptTemplateContract()
    {
        var scaffold = Read("new-plugin.ps1");
        var template = Read(
            "src", "Templates", "script-plugin", "manifest.json");
        var guide = Read("docs", "插件开发指南.md");

        Assert.Contains(
            "[ValidateSet(\"empty\", \"hotkey\", \"full\", \"script\")]",
            scaffold);
        Assert.Contains("\"script\" { $null }", scaffold);
        Assert.Contains("*.csx", scaffold);
        Assert.Contains("$csprojNew = \"$pluginDir/$typeName.csproj\"", scaffold);
        Assert.DoesNotContain("$csprojNew = \"$pluginDir/$dirName.csproj\"", scaffold);
        Assert.Contains(@"C:\Program Files\dotnet\dotnet.exe", scaffold);
        Assert.Contains("& $dotnet test $testProjectNew", scaffold);
        Assert.Contains(
            "sln $slnFile add $csprojNew $testProjectNew",
            scaffold);
        Assert.Contains("\"-p:SolutionDir=$solutionDir\"", scaffold);
        Assert.Contains("$manifest.runtime -eq \"csharp-script\"", scaffold);
        Assert.Contains("Test-Path -LiteralPath $entryPath -PathType Leaf", scaffold);
        Assert.Contains("\"runtime\": \"csharp-script\"", template);
        Assert.Contains("\"entry_point\": \"plugin.csx\"", template);
        Assert.Contains("-Template script", guide);
        Assert.DoesNotContain("\"entry\":", guide);
    }

    [Fact]
    public void PluginValidatorCli_ReusesProductionValidationAndDocumentsStableOutput()
    {
        var script = Read("validate-plugin.ps1");
        var program = Read(
            "tools", "LongBetterWindows.PluginValidator", "Program.cs");
        var validator = Read(
            "src", "LongBetterWindows.Host", "Engine",
            "PluginPackageValidator.cs");
        var guide = Read("docs", "插件开发指南.md");

        Assert.Contains("LongBetterWindows.PluginValidator.csproj", script);
        Assert.Contains("exit $LASTEXITCODE", script);
        Assert.Contains("new PluginPackageValidator()", program);
        Assert.Contains("ValidateDirectoryAsync", program);
        Assert.Contains("ValidateAsync", program);
        Assert.Contains("long_plugin_validation", program);
        Assert.Contains("ValidateDirectoryContents", validator);
        Assert.Contains("manifest.Background", validator);
        Assert.Contains("localization.Resources", validator);
        Assert.Contains("validate-plugin.ps1", guide);
        Assert.Contains("成功为 `0`", guide);
        Assert.Contains("验证失败为 `1`", guide);
        Assert.Contains("参数错误为 `2`", guide);
    }

    [Fact]
    public void PluginPacker_IsDeterministicAuditedAndProductionValidated()
    {
        var packer = Read("pack-plugin.ps1");
        var validator = Read(
            "src", "LongBetterWindows.Host", "Engine",
            "PluginPackageValidator.cs");

        Assert.Contains("Invoke-ProductionValidation", packer);
        Assert.Contains("package-files.json", packer);
        Assert.Contains("long_plugin_file_manifest", packer);
        Assert.Contains("Get-FileHash", packer);
        Assert.Contains("1980, 1, 1", packer);
        Assert.Contains("Sort-Object Path", packer);
        Assert.Contains("ZipArchive", packer);
        Assert.Contains("$postflight", packer);
        Assert.Contains("-NoBuild", packer);
        Assert.Contains("permission_summary", packer);
        Assert.Contains("distribution_eligibility", packer);
        Assert.Contains("仍需发布者签名", packer);
        Assert.Contains(".env", packer);
        Assert.Contains("pfx|p12|snk", packer);
        Assert.DoesNotContain("Compress-Archive", packer);
        Assert.Contains("ValidateFileManifest", validator);
        Assert.Contains("CryptographicOperations.FixedTimeEquals", validator);
    }

    [Fact]
    public void PluginRuntimeMatrix_CoversAllTrustAndDistributionCases()
    {
        var matrix = Read("verify-plugin-runtime-matrix.ps1");
        var program = Read(
            "tools", "LongBetterWindows.PluginValidator", "Program.cs");
        var policy = Read(
            "src", "LongBetterWindows.Host", "Engine",
            "PluginDistributionPolicy.cs");

        Assert.Contains("long_plugin_runtime_matrix", matrix);
        Assert.Contains("-RuntimeKind \"web\"", matrix);
        Assert.Contains("-RuntimeKind \"script\"", matrix);
        Assert.Contains("-RuntimeKind \"native\"", matrix);
        Assert.Contains("-RuntimeKind \"hybrid\"", matrix);
        Assert.Contains("src\\Base64Tool", matrix);
        Assert.Contains("src\\Templates\\script-plugin", matrix);
        Assert.Contains("SamplePlugin", matrix);
        Assert.Contains("ClipboardHistory", matrix);
        Assert.Contains("Invoke-Package", matrix);
        Assert.Contains("Invoke-Validation", matrix);
        Assert.Contains("requires_publisher_signature", program);
        Assert.Contains("permission_summary", program);
        Assert.Contains("distribution_eligibility", program);
        Assert.Contains("high_trust_runtime_not_supported", policy);
    }

    [Fact]
    public void WebPluginSdk_MatchesProductionBridgeAndProvidesStrictMockTests()
    {
        var bridge = Read(
            "src", "LongBetterWindows.Host", "Engine",
            "WebPluginBridgeProtocol.cs");
        var apiVersion = Read(
            "src", "LongBetterWindows.Host", "Contracts",
            "ApiVersion.cs");
        var types = Read("sdk", "web", "index.d.ts");
        var mock = Read("sdk", "web", "mock", "index.js");
        var mockTypes = Read("sdk", "web", "mock", "index.d.ts");
        var package = Read("sdk", "web", "package.json");
        var typeTest = Read("sdk", "web", "tests", "usage.test.ts");
        var behaviorTest = Read("sdk", "web", "tests", "mock.test.mjs");

        var productionMethods = Regex.Matches(
                bridge,
                @"call\('([^']+)'")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var ledgerBlock = Regex.Match(
            mock,
            @"BRIDGE_METHODS = Object\.freeze\(\[(?<body>[\s\S]*?)\]\);");
        Assert.True(ledgerBlock.Success);
        var mockMethods = Regex.Matches(
                ledgerBlock.Groups["body"].Value,
                "\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(135, productionMethods.Length);
        Assert.Equal(productionMethods, mockMethods);
        Assert.Contains("\"name\": \"@long-assistant/plugin-sdk\"", package);
        using var packageDocument = System.Text.Json.JsonDocument.Parse(package);
        var sdkVersion = packageDocument.RootElement
            .GetProperty("version")
            .GetString();
        var hostVersion = Regex.Match(
            apiVersion,
            @"Current\s*=>\s*new\((\d+),\s*(\d+),\s*(\d+)\)");
        Assert.True(hostVersion.Success);
        Assert.Equal(
            $"{hostVersion.Groups[1].Value}.{hostVersion.Groups[2].Value}."
            + hostVersion.Groups[3].Value,
            sdkVersion);
        Assert.Contains("\"typescript\": \"5.9.3\"", package);
        Assert.Contains("interface LongApi", types);
        Assert.Contains("interface Window", types);
        Assert.Contains("LongClipboardChangedEvent", types);
        Assert.Contains("LongLanguageChangedMessage", types);
        Assert.Contains("LongHostInfo", types);
        Assert.Contains("LongWidgetApi", types);
        Assert.Contains("interface WindowEventMap", types);
        Assert.Contains("long.widget-resized", bridge);
        Assert.Contains("LongMockController", mockTypes);
        Assert.Contains("host.getInfo", mock);
        Assert.Contains("widget.setInstanceState", mock);
        Assert.Contains("storage.compareExchange", mock);
        Assert.Contains("@ts-expect-error", typeTest);
        Assert.Contains("node:test", behaviorTest);
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
