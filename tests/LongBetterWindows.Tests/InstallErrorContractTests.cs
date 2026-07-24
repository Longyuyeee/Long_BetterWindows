using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class InstallErrorContractTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "long-install-error-tests-" + Guid.NewGuid().ToString("N"));

    public InstallErrorContractTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void InstallErrorCodes_HaveStablePublishedValues()
    {
        Assert.Equal(0, (int)InstallErrorCode.None);
        Assert.Equal(3000, (int)InstallErrorCode.SourceNotFound);
        Assert.Equal(3001, (int)InstallErrorCode.InvalidPackageExtension);
        Assert.Equal(3002, (int)InstallErrorCode.PackageValidationFailed);
        Assert.Equal(3003, (int)InstallErrorCode.InstallFailedRolledBack);
        Assert.Equal(3004, (int)InstallErrorCode.InstallRollbackFailed);
        Assert.Equal(3005, (int)InstallErrorCode.PluginNotInstalled);
        Assert.Equal(3006, (int)InstallErrorCode.InstalledManifestInvalid);
        Assert.Equal(3007, (int)InstallErrorCode.UninstallFailedRolledBack);
        Assert.Equal(3008, (int)InstallErrorCode.UninstallRollbackFailed);
    }

    [Fact]
    public async Task InstallAsync_InvalidSourceFailuresReturnStableCodes()
    {
        var plugins = Path.Combine(_root, "source-plugins");
        using var scanner = new PluginScanner(plugins);
        var installer = new LpakInstaller(scanner, plugins);

        var missing = await installer.InstallAsync(Path.Combine(_root, "missing.lpak"));
        var wrongExtension = Path.Combine(_root, "plugin.zip");
        await File.WriteAllTextAsync(wrongExtension, "not a package");
        var invalidExtension = await installer.InstallAsync(wrongExtension);

        Assert.Equal(InstallErrorCode.SourceNotFound, missing.ErrorCode);
        Assert.Equal(InstallErrorCode.InvalidPackageExtension, invalidExtension.ErrorCode);
    }

    [Fact]
    public async Task InstallAsync_RejectedPackagePreservesValidationResult()
    {
        var plugins = Path.Combine(_root, "validation-plugins");
        using var scanner = new PluginScanner(plugins);
        var installer = new LpakInstaller(scanner, plugins);
        var package = Path.Combine(_root, "invalid.lpak");
        await File.WriteAllTextAsync(package, "not a zip archive");

        var result = await installer.InstallAsync(package);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallErrorCode.PackageValidationFailed, result.ErrorCode);
        Assert.NotNull(result.Validation);
        Assert.False(result.Validation.IsSuccess);
    }

    [Fact]
    public async Task UninstallAsync_MissingAndInvalidManifestReturnStableCodes()
    {
        var plugins = Path.Combine(_root, "uninstall-plugins");
        using var scanner = new PluginScanner(plugins);
        var installer = new LpakInstaller(scanner, plugins);

        var missing = await installer.UninstallAsync("dev.long.missing");

        var invalidDirectory = Path.Combine(plugins, "dev-long-invalid");
        Directory.CreateDirectory(invalidDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(invalidDirectory, "manifest.json"),
            "{");
        var invalid = await installer.UninstallAsync("dev.long.invalid");

        Assert.Equal(InstallErrorCode.PluginNotInstalled, missing.ErrorCode);
        Assert.Equal(InstallErrorCode.InstalledManifestInvalid, invalid.ErrorCode);
        Assert.Equal(ManifestErrorCode.InvalidJson, invalid.ManifestFailureCode);
    }

    [Fact]
    public void InstallationFailures_HaveBilingualPresentationKeys()
    {
        var repository = FindRepositoryRoot();
        using var chinese = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repository,
            "src",
            "LongBetterWindows.Host",
            "i18n",
            "zh-CN.json")));
        using var english = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repository,
            "src",
            "LongBetterWindows.Host",
            "i18n",
            "en-US.json")));

        foreach (var code in Enum.GetValues<InstallErrorCode>()
                     .Where(code => code != InstallErrorCode.None))
        {
            var key = MarketplacePresentation.GetInstallErrorResourceKey(code);
            Assert.NotEqual("market.error.unknown", key);
            Assert.True(chinese.RootElement.TryGetProperty(key, out _), key);
            Assert.True(english.RootElement.TryGetProperty(key, out _), key);
        }

        Assert.Equal(
            "market.error.unknown",
            MarketplacePresentation.GetInstallErrorResourceKey(InstallErrorCode.None));

        var marketplaceView = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "MarketplaceControl.xaml.cs"));
        Assert.DoesNotContain(
            "ConfirmErrorText.Text = result.Error",
            marketplaceView,
            StringComparison.Ordinal);
        Assert.Contains(
            "MarketplacePresentation.GetInstallErrorResourceKey(result.ErrorCode)",
            marketplaceView,
            StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
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

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
