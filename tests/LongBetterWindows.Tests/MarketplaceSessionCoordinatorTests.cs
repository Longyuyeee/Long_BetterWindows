using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class MarketplaceSessionCoordinatorTests
{
    [Fact]
    public async Task CatalogLoad_CancelsSupersededRequestAndKeepsLatestCatalog()
    {
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var session = CreateSession(
            loadCatalog: async cancellationToken =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    firstStarted.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                return MarketplaceCatalogResult.Ok(Catalog("latest"));
            });

        var first = session.LoadCatalogAsync();
        await firstStarted.Task;
        var second = session.LoadCatalogAsync();
        var results = await Task.WhenAll(first, second);

        Assert.True(results[0].IsSuperseded);
        Assert.False(results[1].IsSuperseded);
        Assert.Equal("latest", Assert.Single(session.Catalog!.Entries).Id);
    }

    [Fact]
    public async Task PackagePreparation_BlocksDuplicateAndFailurePreservesPendingUntilSuccess()
    {
        var validationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseValidation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var validation = ValidValidation();
        using var session = CreateSession(
            validatePackage: async (_, _, _) =>
            {
                validationStarted.SetResult();
                await releaseValidation.Task;
                return validation;
            });
        var metadata = new MarketplacePackageMetadata
        {
            ExpectedPluginId = validation.Manifest!.Id,
        };

        var first = session.PrepareLocalPackageAsync("fixture.lpak", metadata);
        await validationStarted.Task;
        var duplicate = await session.PrepareLocalPackageAsync("fixture.lpak", metadata);
        releaseValidation.SetResult();
        var prepared = await first;

        Assert.True(duplicate.IsBusy);
        Assert.Equal(MarketplaceErrorCode.OperationBusy, duplicate.ErrorCode);
        Assert.True(prepared.IsSuccess);
        Assert.NotNull(session.PendingAction);

        var failed = await session.ExecutePendingAsync((_, _) =>
            Task.FromResult(InstallResult.Fail(
                InstallErrorCode.InstallFailedRolledBack,
                "simulated failure")));
        Assert.False(failed.Result!.IsSuccess);
        Assert.NotNull(session.PendingAction);

        var succeeded = await session.ExecutePendingAsync((_, _) =>
            Task.FromResult(InstallResult.Ok(
                "Fixture",
                validation.Manifest.Id,
                validation.Manifest.Version,
                InstallAction.Install,
                validation,
                new PermissionDiff())));
        Assert.True(succeeded.Result!.IsSuccess);
        Assert.Null(session.PendingAction);
    }

    [Fact]
    public async Task CancelActiveRequests_CancelsDownloadAndClearsOperationState()
    {
        var downloadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = CreateSession(
            canDownload: () => true,
            downloadPackage: async (_, _, cancellationToken) =>
            {
                downloadStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return PackageDownloadResult.Fail(
                    MarketplaceErrorCode.DownloadFailed,
                    "unreachable");
            });
        var entry = Assert.Single(Catalog("fixture").Entries);
        var version = Assert.Single(entry.Versions);

        var preparation = session.PrepareRemotePackageAsync(entry, version);
        await downloadStarted.Task;
        session.CancelActiveRequests();
        var result = await preparation;

        Assert.True(result.IsCanceled);
        Assert.Equal(MarketplaceErrorCode.OperationCanceled, result.ErrorCode);
        Assert.False(session.IsOperationActive);
        Assert.Null(session.PendingAction);
    }

    [Fact]
    public async Task PreparationFailures_PropagateStableMarketplaceCodes()
    {
        var entry = Assert.Single(Catalog("fixture").Entries);
        var version = Assert.Single(entry.Versions);
        using var unavailable = CreateSession();

        var notConfigured = await unavailable.PrepareRemotePackageAsync(entry, version);

        using var rejected = CreateSession(
            validatePackage: (_, _, _) => Task.FromResult(
                PackageValidationResult.Fail("rejected")));
        var packageRejected = await rejected.PrepareLocalPackageAsync(
            "fixture.lpak",
            new MarketplacePackageMetadata());

        Assert.Equal(
            MarketplaceErrorCode.DownloadNotConfigured,
            notConfigured.ErrorCode);
        Assert.Equal(
            MarketplaceErrorCode.PackageRejected,
            packageRejected.ErrorCode);
    }

    [Fact]
    public async Task ExecutePending_HoldsPendingStateAgainstConcurrentCancellation()
    {
        using var session = CreateSession();
        var manifest = ValidValidation().Manifest!;
        Assert.True(session.PrepareUninstall(manifest).IsSuccess);
        var executionStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var finishExecution = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var execution = session.ExecutePendingAsync(async (_, _) =>
        {
            executionStarted.SetResult();
            await finishExecution.Task;
            return InstallResult.Ok(
                manifest.Name,
                manifest.Id,
                manifest.Version,
                InstallAction.Uninstall,
                null,
                new PermissionDiff());
        });
        await executionStarted.Task;

        Assert.False(session.CancelPending());
        Assert.NotNull(session.PendingAction);
        finishExecution.SetResult();
        Assert.True((await execution).Result!.IsSuccess);
        Assert.Null(session.PendingAction);
    }

    private static MarketplaceSessionCoordinator CreateSession(
        Func<CancellationToken, Task<MarketplaceCatalogResult>>? loadCatalog = null,
        Func<string, MarketplacePackageMetadata, PluginManifest?, Task<PackageValidationResult>>? validatePackage = null,
        Func<string, MarketplacePackageVersion, CancellationToken, Task<PackageDownloadResult>>? downloadPackage = null,
        Func<bool>? canDownload = null)
        => new(
            loadCatalog ?? (_ => Task.FromResult(MarketplaceCatalogResult.Ok(Catalog("fixture")))),
            validatePackage ?? ((_, _, _) => Task.FromResult(ValidValidation())),
            downloadPackage ?? ((_, _, _) => Task.FromResult(
                PackageDownloadResult.Fail(
                    MarketplaceErrorCode.DownloadNotConfigured,
                    "not configured"))),
            canDownload ?? (() => false),
            _ => null);

    private static MarketplaceCatalog Catalog(string id)
        => new()
        {
            Source = MarketplaceSourceKind.RemoteRegistry,
            Entries =
            [
                new MarketplaceEntry
                {
                    Id = id,
                    Name = "Fixture",
                    Summary = "Fixture",
                    Publisher = "Long",
                    Category = "Quality",
                    Versions =
                    [
                        new MarketplacePackageVersion
                        {
                            Version = "1.0.0",
                            PackageUri = new Uri("https://packages.example/fixture.lpak"),
                            Sha256 = new string('0', 64),
                        },
                    ],
                },
            ],
        };

    private static PackageValidationResult ValidValidation()
        => PackageValidationResult.Ok(
            new PluginManifest
            {
                Id = "fixture",
                Name = "Fixture",
                Version = "1.0.0",
                EntryPoint = "index.html",
                Runtime = "webview",
            },
            new string('A', 64),
            PackageTrustLevel.PublisherSigned,
            new PermissionDiff(),
            false);
}
