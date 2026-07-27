using System.IO;
using System.Xml.Linq;

namespace LongBetterWindows.Tests;

public class SparsePackageTests
{
    private const string CommandClsid = "A17F41AD-74BC-47F8-984B-2DF6F22263A1";

    [Fact]
    public void Manifest_DeclaresExternalWin32IdentityAndModernExplorerCommands()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Package",
            "appxmanifest.xml");
        var document = XDocument.Load(path);
        XNamespace foundation =
            "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XNamespace uap10 =
            "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";
        XNamespace com =
            "http://schemas.microsoft.com/appx/manifest/com/windows10";
        XNamespace desktop4 =
            "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4";
        XNamespace desktop5 =
            "http://schemas.microsoft.com/appx/manifest/desktop/windows10/5";
        XNamespace rescap =
            "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";

        Assert.Equal(
            "true",
            document.Descendants(uap10 + "AllowExternalContent").Single().Value);
        var application = document.Descendants(foundation + "Application").Single();
        Assert.Equal("win32App", application.Attribute(uap10 + "RuntimeBehavior")?.Value);
        Assert.Equal("mediumIL", application.Attribute(uap10 + "TrustLevel")?.Value);
        Assert.Equal(
            "none",
            document.Descendants().Single(element =>
                element.Name.LocalName == "VisualElements").Attribute("AppListEntry")?.Value);
        Assert.Equal(
            "ShellExtension\\LongBetterWindows.ShellExtension.dll",
            document.Descendants(com + "Class").Single().Attribute("Path")?.Value);
        Assert.Contains(
            document.Descendants(desktop4 + "Extension"),
            extension => extension.Attribute("Category")?.Value
                == "windows.fileExplorerContextMenus");
        Assert.Equal(
            new[] { "Directory", "Directory\\Background" },
            document.Descendants(desktop5 + "ItemType")
                .Select(item => item.Attribute("Type")!.Value)
                .ToArray());
        Assert.All(
            document.Descendants(desktop5 + "Verb"),
            verb => Assert.Equal(
                CommandClsid,
                verb.Attribute("Clsid")?.Value,
                ignoreCase: true));
        Assert.Equal(
            CommandClsid,
            document.Descendants(com + "Class").Single().Attribute("Id")?.Value,
            ignoreCase: true);
        Assert.Contains(
            document.Descendants(rescap + "Capability"),
            capability => capability.Attribute("Name")?.Value == "runFullTrust");
        Assert.Contains(
            document.Descendants(rescap + "Capability"),
            capability => capability.Attribute("Name")?.Value
                == "unvirtualizedResources");
    }

    [Fact]
    public void NativeCommand_ImplementsExplorerCommandAndLaunchesOnlyTheHostNoteRoute()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.ShellExtension",
            "ExplorerCommand.cpp"));
        var definition = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.ShellExtension",
            "LongBetterWindows.ShellExtension.def"));

        Assert.Contains("public IExplorerCommand", source);
        Assert.Contains("IID_IExplorerCommand", source);
        Assert.Contains("SIGDN_FILESYSPATH", source);
        Assert.Contains("SFGAO_FOLDER | SFGAO_FILESYSTEM", source);
        Assert.Contains("LongBetterWindows.Host.exe", source);
        Assert.Contains("arguments = L\"--note \" + QuoteCommandLineArgument(folderPath)", source);
        Assert.Contains("quoted.append(backslashes * 2, L'\\\\')", source);
        Assert.Contains("ShellExecuteExW", source);
        Assert.Contains("DllGetClassObject", definition);
        Assert.Contains("DllCanUnloadNow", definition);
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000000", source);
    }

    [Fact]
    public void BuildScript_ProducesUnsignedEvidenceWithoutInstallingOrInventingApproval()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "build-sparse-package.ps1"));

        Assert.Contains("makeappx.exe", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DllGetClassObject", script);
        Assert.Contains("tracked_source_clean", script);
        Assert.Contains("host_sha256", script);
        Assert.Contains("unsigned_sparse_package_build", script);
        Assert.Contains("signed = $false", script);
        Assert.Contains("installation_attempted = $false", script);
        Assert.Contains("machine_verified = 'x64'", script);
        Assert.DoesNotContain("Add-AppxPackage", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("New-SelfSignedCertificate", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HostManifest_ConnectsTheExecutableToTheSparsePackageIdentity()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "app.manifest"));
        XNamespace msix = "urn:schemas-microsoft-com:msix.v1";
        var identity = document.Descendants(msix + "msix").Single();

        Assert.Equal("CN=Long-Development", identity.Attribute("publisher")?.Value);
        Assert.Equal("Long.LongBetterWindows", identity.Attribute("packageName")?.Value);
        Assert.Equal("LongBetterWindows", identity.Attribute("applicationId")?.Value);
    }

    [Fact]
    public void SigningScript_RequiresAnExistingCodeSigningIdentityWithoutHandlingPasswords()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "sign-sparse-package.ps1"));

        Assert.Contains("CertificateThumbprint", script);
        Assert.Contains("certificate.HasPrivateKey", script);
        Assert.Contains("1.3.6.1.5.5.7.3.3", script);
        Assert.Contains("certificate.Subject", script);
        Assert.Contains("identity.Publisher", script);
        Assert.Contains("SignTool verification failed", script);
        Assert.Contains("TimestampUrl must be an absolute HTTPS URL", script);
        Assert.DoesNotContain("New-SelfSignedCertificate", script);
        Assert.DoesNotContain("Import-PfxCertificate", script);
        Assert.DoesNotContain("TrustedPeople", script);
        Assert.DoesNotContain("SecureString", script);
    }

    [Fact]
    public void ManagementScript_KeepsSparseAndLegacyMenuTransactionsIndependent()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(
            root,
            "manage-sparse-package.ps1"));
        var view = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "SystemIntegrationPageControl.xaml"));
        var zhResources = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "i18n",
            "zh-CN.json"));

        Assert.Contains("Get-AuthenticodeSignature", script);
        Assert.Contains("SignerCertificate.Subject", script);
        Assert.Contains("Add-AppxPackage", script);
        Assert.Contains("-ExternalLocation", script);
        Assert.Contains("Remove-AppxPackage", script);
        Assert.Contains("sparse-package.json", script);
        Assert.DoesNotContain(@"Software\Classes", script);
        Assert.Contains("i18n.system.sparse.title", view);
        Assert.Contains("i18n.system.legacy.title", view);
        Assert.Contains("Win11 一级右键菜单", zhResources);
        Assert.Contains("兼容旧右键菜单", zhResources);
        Assert.Contains("SparsePackageStatusText", view);
        Assert.Contains("ContextMenuStatusText", view);
    }

    [Fact]
    public void ExplorerEvidenceGate_RequiresCleanSignedChainAndAlwaysRemovesThePackage()
    {
        var root = FindRepositoryRoot();
        var capture = File.ReadAllText(Path.Combine(
            root,
            "capture-sparse-package-explorer-evidence.ps1"));
        var approval = File.ReadAllText(Path.Combine(
            root,
            "approve-sparse-package-explorer-evidence.ps1"));
        var verification = File.ReadAllText(Path.Combine(
            root,
            "verify-sparse-package-explorer-evidence.ps1"));

        Assert.Contains("ConfirmCleanUserEnvironment", capture);
        Assert.Contains("PreflightOnly", capture);
        Assert.Contains("package_registration_attempted = $false", capture);
        Assert.Contains("tracked_source_clean", capture);
        Assert.Contains("ExpectedSourceCommit", capture);
        Assert.Contains("ExpectedCertificateThumbprint", capture);
        Assert.Contains("TimeStamperCertificate", capture);
        Assert.Contains("selection-primary-menu.png", capture);
        Assert.Contains("background-primary-menu.png", capture);
        Assert.Contains("note-invocation.png", capture);
        Assert.Contains("Read-Host", capture);
        Assert.Contains("finally", capture);
        Assert.Contains("-Action Unregister", capture);
        Assert.Contains("legacy_menu_state_unchanged", capture);
        Assert.DoesNotContain("New-SelfSignedCertificate", capture);
        Assert.DoesNotContain("TrustedPeople", capture);

        Assert.Contains("Reviewer must differ", approval);
        Assert.Contains("Get-FileHash", approval);
        Assert.Contains("ConfirmUninstallRemovedMenu", approval);
        Assert.Contains("status = 'approved'", approval);

        Assert.Contains("human_review.status -ne 'approved'", verification);
        Assert.Contains("package_removed_after_capture", verification);
        Assert.Contains("legacy_menu_state_unchanged", verification);
        Assert.Contains("passed = $true", verification);
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

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
