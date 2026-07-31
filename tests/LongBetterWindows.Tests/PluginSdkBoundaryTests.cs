using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using ClipboardHistoryBackground;
using ColorPickerPlugin;
using FolderNotePlugin;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;
using LongBetterWindows.PluginSdk.Wpf;
using MacroPlugin;
using QuickLaunchPlugin;
using ScreenshotPlugin;
using WindowManagerPlugin;

namespace LongBetterWindows.Tests;

public sealed class PluginSdkBoundaryTests
{
    [Fact]
    public void StableContracts_AreOwnedByPluginSdkAssembly()
    {
        var sdkAssembly = typeof(ILongPlugin).Assembly;

        Assert.Equal("LongBetterWindows.PluginSdk", sdkAssembly.GetName().Name);
        Assert.Same(sdkAssembly, typeof(PluginManifest).Assembly);
        Assert.Equal(
            ApiVersion.Current.ToString().TrimStart('v'),
            sdkAssembly.GetName().Version?.ToString(3));
        Assert.DoesNotContain(
            sdkAssembly.GetReferencedAssemblies(),
            reference => reference.Name == "LongBetterWindows.Host");
    }

    [Fact]
    public void Host_DependsOnSdk_InOneDirection()
    {
        var hostReferences = typeof(ServicesInitializer)
            .Assembly
            .GetReferencedAssemblies();

        Assert.Contains(
            hostReferences,
            reference => reference.Name == "LongBetterWindows.PluginSdk");
    }

    [Fact]
    public void WpfUiKit_DependsOnSdkButNotHost()
    {
        var assembly = typeof(HotkeySettingsControl).Assembly;
        var references = assembly.GetReferencedAssemblies();

        Assert.Equal("LongBetterWindows.PluginSdk.Wpf", assembly.GetName().Name);
        Assert.Contains(
            references,
            reference => reference.Name == "LongBetterWindows.PluginSdk");
        Assert.DoesNotContain(
            references,
            reference => reference.Name == "LongBetterWindows.Host");
    }

    [Fact]
    public void NativeLoader_SharesOnlyProductionSdkAssemblies()
    {
        var sharedNames = PluginLoadContext.SharedSdkAssemblies
            .Select(assembly => assembly.GetName().Name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "LongBetterWindows.PluginSdk",
                "LongBetterWindows.PluginSdk.Wpf",
            ],
            sharedNames);
        Assert.DoesNotContain(
            "LongBetterWindows.PluginSdk.Testing",
            sharedNames);
    }

    [Fact]
    public void SdkVersionsAndDependencies_MatchApiVersion()
    {
        var root = FindRepositoryRoot();
        var expectedVersion = ApiVersion.Current.ToString().TrimStart('v');
        var expectedAssemblyVersion = expectedVersion + ".0";
        var projects = new[]
        {
            (
                Path.Combine(
                    root,
                    "src",
                    "LongBetterWindows.PluginSdk",
                    "LongBetterWindows.PluginSdk.csproj"),
                "LongBetterWindows.PluginSdk",
                Array.Empty<string>()),
            (
                Path.Combine(
                    root,
                    "src",
                    "LongBetterWindows.PluginSdk.Wpf",
                    "LongBetterWindows.PluginSdk.Wpf.csproj"),
                "LongBetterWindows.PluginSdk.Wpf",
                new[] { "LongBetterWindows.PluginSdk.csproj" }),
            (
                Path.Combine(
                    root,
                    "src",
                    "LongBetterWindows.PluginIpc",
                    "LongBetterWindows.PluginIpc.csproj"),
                "LongBetterWindows.PluginIpc",
                Array.Empty<string>()),
            (
                Path.Combine(
                    root,
                    "sdk",
                    "dotnet",
                    "LongBetterWindows.PluginSdk.Testing",
                    "LongBetterWindows.PluginSdk.Testing.csproj"),
                "LongBetterWindows.PluginSdk.Testing",
                new[] { "LongBetterWindows.PluginSdk.csproj" }),
        };

        foreach (var (path, packageId, expectedReferences) in projects)
        {
            var project = XDocument.Load(path);
            Assert.Equal(expectedVersion, ProjectValue(project, "Version"));
            Assert.Equal(
                expectedAssemblyVersion,
                ProjectValue(project, "AssemblyVersion"));
            Assert.Equal(packageId, ProjectValue(project, "PackageId"));
            Assert.Equal(
                expectedReferences,
                project
                    .Descendants("ProjectReference")
                    .Select(element => Path.GetFileName(
                        element.Attribute("Include")!.Value))
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray());
        }

        using var webPackage = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "sdk",
            "web",
            "package.json")));
        using var webLock = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "sdk",
            "web",
            "package-lock.json")));
        using var boundaryAudit = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "docs",
            "native-plugin-boundary-audit.json")));

        Assert.Equal(
            expectedVersion,
            webPackage.RootElement.GetProperty("version").GetString());
        Assert.Equal(
            expectedVersion,
            webLock.RootElement.GetProperty("version").GetString());
        Assert.Equal(
            expectedVersion,
            boundaryAudit.RootElement.GetProperty("sdk_version").GetString());
    }

    [Fact]
    public void AsyncDeliveryBoundary_IsOwnedByStableSdk()
    {
        Assert.Same(typeof(ILongPlugin).Assembly, typeof(AsyncDeliveryBoundary).Assembly);
    }

    [Fact]
    public void PluginSearchContracts_AreOwnedByStableSdk()
    {
        var sdkAssembly = typeof(ILongPlugin).Assembly;

        Assert.Same(sdkAssembly, typeof(IPluginSearchProvider).Assembly);
        Assert.Same(sdkAssembly, typeof(PluginSearchRequest).Assembly);
        Assert.Same(sdkAssembly, typeof(PluginSearchResult).Assembly);
        Assert.Same(sdkAssembly, typeof(PluginSearchAction).Assembly);
        Assert.DoesNotContain(
            typeof(PluginSearchRequest).GetProperties(),
            property => property.Name.Contains(
                "Context",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MigratedInteractivePlugins_DoNotReferenceHostAssembly()
    {
        var assemblies = new[]
        {
            typeof(ColorPickerPluginImpl).Assembly,
            typeof(FolderNotePluginImpl).Assembly,
            typeof(MacroPluginImpl).Assembly,
            typeof(QuickLaunchPluginImpl).Assembly,
            typeof(ScreenshotPluginImpl).Assembly,
            typeof(WindowManagerPluginImpl).Assembly,
        };

        Assert.All(assemblies, assembly =>
        {
            Assert.Contains(
                assembly.GetReferencedAssemblies(),
                reference => reference.Name == "LongBetterWindows.PluginSdk");
            Assert.DoesNotContain(
                assembly.GetReferencedAssemblies(),
                reference => reference.Name == "LongBetterWindows.Host");
        });
    }

    [Fact]
    public void ScreenColorSamplerContract_IsOwnedBySdkAndUsesScreenshotCapability()
    {
        Assert.Same(typeof(ILongPlugin).Assembly, typeof(IScreenColorSampler).Assembly);
        Assert.Equal(
            "system.screenshot",
            HostCapabilityCatalog.ForService<IScreenColorSampler>());
    }

    [Fact]
    public void MigratedBackgroundPlugin_DoesNotReferenceHostAssembly()
    {
        var references = typeof(ClipboardHistoryBackgroundPlugin)
            .Assembly
            .GetReferencedAssemblies();

        Assert.Contains(
            references,
            reference => reference.Name == "LongBetterWindows.PluginSdk");
        Assert.DoesNotContain(
            references,
            reference => reference.Name == "LongBetterWindows.Host");
    }

    [Fact]
    public async Task NativeLoader_ReusesHostSdkWhenPluginDirectoryContainsPrivateCopy()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"long-sdk-boundary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var pluginAssembly = typeof(ClipboardHistoryBackgroundPlugin).Assembly;
            var pluginPath = Path.Combine(
                directory,
                Path.GetFileName(pluginAssembly.Location));
            File.Copy(pluginAssembly.Location, pluginPath);
            File.Copy(
                typeof(ILongPlugin).Assembly.Location,
                Path.Combine(directory, "LongBetterWindows.PluginSdk.dll"));

            await LoadAndUnloadAsync(directory, Path.GetFileName(pluginPath));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (UnauthorizedAccessException)
            {
                // A collectible AssemblyLoadContext can release its Windows file
                // handles after the assertion scope; delayed temp cleanup is benign.
            }
            catch (IOException)
            {
                // See above. The operating-system temp directory owns final cleanup.
            }
        }
    }

    [Fact]
    public async Task NativeLoader_ReusesHostWpfUiKitWithoutPluginPrivateCopy()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"long-wpf-sdk-boundary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var pluginAssembly = typeof(MacroPluginImpl).Assembly;
            var pluginPath = Path.Combine(
                directory,
                Path.GetFileName(pluginAssembly.Location));
            File.Copy(pluginAssembly.Location, pluginPath);

            var loader = new PluginLoader();
            var result = await loader.LoadAsync(
                directory,
                new PluginManifest
                {
                    Id = "com.long.macro",
                    Name = "Macro",
                    Version = "1.1.2",
                    EntryPoint = Path.GetFileName(pluginPath),
                });

            Assert.True(result.IsSuccess, result.Error);
            Assert.IsAssignableFrom<ILongPlugin>(result.Instance);
            loader.Unload(result.Context!);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task LoadAndUnloadAsync(
        string directory,
        string entryPoint)
    {
        var loader = new PluginLoader();
        var result = await loader.LoadAsync(
            directory,
            new PluginManifest
            {
                Id = "com.long.clipboardhistory",
                Name = "Clipboard History",
                Version = "1.1.0",
                EntryPoint = entryPoint,
            });

        Assert.True(result.IsSuccess, result.Error);
        Assert.IsAssignableFrom<ILongPlugin>(result.Instance);
        loader.Unload(result.Context!);
    }

    [Fact]
    public void NativeTemplates_ReferenceSdkInsteadOfHost()
    {
        var root = FindRepositoryRoot();
        foreach (var template in new[] { "empty-plugin", "hotkey-plugin", "full-plugin" })
        {
            var project = Directory
                .EnumerateFiles(
                    Path.Combine(root, "src", "Templates", template),
                    "*.csproj")
                .Single();
            var content = File.ReadAllText(project);

            Assert.Contains(
                @"LongBetterWindows.PluginSdk\LongBetterWindows.PluginSdk.csproj",
                content,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                @"LongBetterWindows.Host\LongBetterWindows.Host.csproj",
                content,
                StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static string ProjectValue(XDocument project, string name)
        => project
            .Descendants(name)
            .Select(element => element.Value)
            .Single();
}
