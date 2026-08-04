using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class MarketplaceOperationPresentationTests
{
    [Theory]
    [InlineData(null, "1.0.0", "Install")]
    [InlineData("1.0.0", "2.0.0", "Update")]
    [InlineData("2.0.0", "1.0.0", "Downgrade")]
    [InlineData("1.0.0", "1.0.0", "Reinstall")]
    [InlineData("v1.0.0-beta", "1.1.0", "Update")]
    public void ForInstall_ClassifiesVersionIntent(
        string? installedVersion,
        string targetVersion,
        string expected)
    {
        var presentation = MarketplaceOperationPresenter.ForInstall(
            installedVersion,
            targetVersion);

        Assert.Equal(expected, presentation.Intent.ToString());
        Assert.Contains(expected.ToLowerInvariant(),
            presentation.SuccessResourceKey);
    }

    [Fact]
    public void Uninstall_HasDistinctProgressAndSuccessKeys()
    {
        var presentation = MarketplaceOperationPresenter.ForUninstall();

        Assert.Equal(MarketplaceOperationIntent.Uninstall, presentation.Intent);
        Assert.Equal(
            "market.operation.uninstall.progress",
            presentation.ProgressResourceKey);
        Assert.Equal(
            "market.operation.uninstall.success",
            presentation.SuccessResourceKey);
    }

    [Fact]
    public void CreateInstallReview_UpdateCarriesVerifiedFactsAndRestoreVersion()
    {
        var permissionDiff = new PermissionDiff
        {
            Added = ["system.screenshot"],
            Unchanged = ["system.clipboard"],
        };
        var validation = PackageValidationResult.Ok(
            new PluginManifest
            {
                Id = "com.long.screenshot",
                Name = "Screenshot",
                Version = "1.1.2",
                Author = "Long",
                EntryPoint = "ScreenshotPlugin.dll",
                MinHostVersion = "1.10.0",
            },
            "ABC123",
            PackageTrustLevel.PublisherSigned,
            permissionDiff,
            highTrust: true);

        var review = MarketplaceOperationPresenter.CreateInstallReview(
            validation,
            new PluginManifest { Version = "1.1.1" },
            new MarketplacePackageMetadata { PublisherKeyId = "long-release" },
            "1.11.0-rc.3");

        Assert.Equal(MarketplaceOperationIntent.Update, review.Operation.Intent);
        Assert.Equal("1.1.1", review.InstalledVersion);
        Assert.Equal("1.1.2", review.TargetVersion);
        Assert.Equal("Long", review.DeclaredPublisher);
        Assert.Equal("long-release", review.PublisherKeyId);
        Assert.Equal("ABC123", review.Sha256);
        Assert.Same(permissionDiff, review.PermissionDiff);
        Assert.True(review.RequiresHighTrustWarning);
        Assert.True(review.Compatibility.IsCompatible);
        Assert.Contains("Host >= 1.10.0", review.Compatibility.Requirements);
        Assert.Equal(
            MarketplaceRecoveryStrategy.RestoreInstalledVersion,
            review.RecoveryStrategy);
    }

    [Fact]
    public void CreateInstallReview_FreshUnsignedInstallUsesCleanupStrategy()
    {
        var validation = PackageValidationResult.Ok(
            new PluginManifest
            {
                Id = "dev.long.note",
                Name = "Note",
                Version = "1.0.0",
                EntryPoint = "index.html",
            },
            "DEF456",
            PackageTrustLevel.LocalUnsigned,
            new PermissionDiff(),
            highTrust: false);

        var review = MarketplaceOperationPresenter.CreateInstallReview(
            validation,
            installedManifest: null,
            metadata: null,
            hostVersion: "1.11.0");

        Assert.Equal(MarketplaceOperationIntent.Install, review.Operation.Intent);
        Assert.Null(review.InstalledVersion);
        Assert.Null(review.DeclaredPublisher);
        Assert.Null(review.PublisherKeyId);
        Assert.Equal(
            MarketplaceRecoveryStrategy.RemovePartialInstall,
            review.RecoveryStrategy);
    }

    [Fact]
    public void CreateInstallReview_RejectsUnvalidatedPackage()
    {
        Assert.Throws<ArgumentException>(() =>
            MarketplaceOperationPresenter.CreateInstallReview(
                PackageValidationResult.Fail("invalid"),
                installedManifest: null,
                metadata: null,
                hostVersion: "1.11.0"));
    }

    [Fact]
    public void OperationPresentationKeys_AreAvailableInBothLanguages()
    {
        var root = FindRepositoryRoot();
        using var chinese = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "i18n", "zh-CN.json")));
        using var english = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "i18n", "en-US.json")));
        var presentations = new[]
        {
            MarketplaceOperationPresenter.ForInstall(null, "1.0.0"),
            MarketplaceOperationPresenter.ForInstall("1.0.0", "2.0.0"),
            MarketplaceOperationPresenter.ForInstall("2.0.0", "1.0.0"),
            MarketplaceOperationPresenter.ForInstall("1.0.0", "1.0.0"),
            MarketplaceOperationPresenter.ForUninstall(),
        };

        foreach (var key in presentations.SelectMany(presentation => new[]
                 {
                     presentation.ReviewTitleResourceKey,
                     presentation.ConfirmActionResourceKey,
                     presentation.ProgressResourceKey,
                     presentation.SuccessResourceKey,
                     presentation.RemoteActionResourceKey,
                     presentation.LocalActionResourceKey,
                 }))
        {
            Assert.True(chinese.RootElement.TryGetProperty(key, out _), key);
            Assert.True(english.RootElement.TryGetProperty(key, out _), key);
        }
    }

    [Fact]
    public void OperationView_DisablesDismissalWhileBusyAndRestoresVisibleFocus()
    {
        var view = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "LongBetterWindows.Host",
            "Views",
            "MarketplaceControl.xaml.cs"));

        Assert.Contains("ConfirmCancelButton.IsEnabled = !busy", view);
        Assert.Contains("ConfirmCloseButton.IsEnabled = !busy", view);
        Assert.Contains("AutomationProperties.SetName", view);
        Assert.Contains("preferredFocus", view);
        Assert.Contains("CreateInstallReview", view);
        Assert.Contains("ConfirmRecoveryText", view);
        Assert.Contains("element.IsVisible", view);
        var successBlock = view[view.IndexOf(
            "var status = FormatOperationSuccess",
            StringComparison.Ordinal)..];
        Assert.True(
            successBlock.IndexOf(
                "await ApplyFiltersAsync();",
                StringComparison.Ordinal)
            < successBlock.IndexOf(
                "_operationStatus = status",
                StringComparison.Ordinal));
        Assert.Contains("_operationStatus = null", view);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "LongBetterWindows.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
