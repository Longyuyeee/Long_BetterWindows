using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.PluginSdk.Testing;

namespace LongBetterWindows.Tests;

public sealed class PluginTestingSdkTests
{
    [Fact]
    public void TestingSdk_DoesNotReferenceWpfHost()
    {
        var references = typeof(PluginTestHost).Assembly.GetReferencedAssemblies();

        Assert.Contains(
            references,
            reference => reference.Name == "LongBetterWindows.PluginSdk");
        Assert.DoesNotContain(
            references,
            reference => reference.Name == "LongBetterWindows.Host");
    }

    [Fact]
    public void CapabilityCatalog_CoversEveryHostServiceProperty()
    {
        var serviceProperties = typeof(IHostApi)
            .GetProperties()
            .Where(property => property.Name != nameof(IHostApi.LastAccessError))
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.Equal(serviceProperties.Length, HostCapabilityCatalog.ServiceMap.Count);
        Assert.All(
            serviceProperties,
            serviceType => Assert.False(
                string.IsNullOrWhiteSpace(
                    HostCapabilityCatalog.ForService(serviceType))));
        Assert.Equal(27, HostCapabilityCatalog.ServiceMap.Values.Distinct().Count());
    }

    [Fact]
    public async Task Harness_VerifiesHeadlessLifecycle()
    {
        var plugin = new ContractFixturePlugin();

        var result = await PluginContractHarness.VerifyLifecycleAsync(
            plugin,
            new PluginTestHost());

        Assert.True(result.Initialized);
        Assert.True(result.Started);
        Assert.True(result.Stopped);
        Assert.Equal(PluginState.Loaded, result.InitialState);
        Assert.Equal(PluginState.Stopped, result.FinalState);
    }

    [Fact]
    public void Harness_VerifiesManifestIdentity()
    {
        var plugin = new ContractFixturePlugin();
        var manifest = CreateManifest();

        PluginContractHarness.VerifyIdentity(plugin, manifest);

        Assert.Throws<PluginContractException>(
            () => PluginContractHarness.VerifyIdentity(
                plugin,
                new PluginManifest
                {
                    Id = manifest.Id,
                    Name = manifest.Name,
                    Version = "2.0.0",
                    EntryPoint = manifest.EntryPoint,
                }));
    }

    [Fact]
    public void TestHost_ReportsProductionCapabilityDenial()
    {
        var host = new PluginTestHost();

        PluginContractHarness.VerifyCapabilityDenied(
            host,
            api => api.HotKey,
            "system.hotkey");

        Assert.Contains("system.hotkey", host.LastAccessError);
    }

    [Fact]
    public async Task TestHost_GrantsMappedCapabilityWithRegisteredDouble()
    {
        var notification = new NotificationStub();
        var host = new PluginTestHost()
            .Grant<INotificationService>(notification);

        var response = await host.Notification.ShowAsync("title", "body");

        Assert.True(response.IsSuccess);
        Assert.True(host.HasCapability("system.notification"));
        Assert.Same(notification, host.Notification);
    }

    [Fact]
    public void TestHost_GrantsScreenColorSamplerWithScreenshotCapability()
    {
        var host = new PluginTestHost()
            .Grant<IScreenColorSampler>(new ScreenColorSamplerStub());

        var result = host.ScreenColorSampler.Sample(10, 20);

        Assert.True(result.IsSuccess);
        Assert.Equal("#0A141E", result.Data?.Hex);
        Assert.True(host.HasCapability("system.screenshot"));
    }

    [Fact]
    public async Task Harness_CoversCommandLanguageAndRepeatableRelease()
    {
        var plugin = new ContractFixturePlugin();
        var command = await PluginContractHarness.ExecuteCommandAsync(
            plugin,
            new PluginCommandInvocation
            {
                CommandId = "fixture.echo",
                Text = "hello",
            });
        await PluginContractHarness.ApplyLanguageAsync(
            plugin,
            new PluginLanguageContext(
                "zh-CN",
                "zh-CN",
                new Dictionary<string, string> { ["name"] = "测试插件" }));
        await PluginContractHarness.VerifyResourceReleaseIsRepeatableAsync(plugin);

        Assert.True(command.IsSuccess);
        Assert.Equal("hello", command.Message);
        Assert.Equal("测试插件", plugin.LocalizedName);
        Assert.Equal(2, plugin.ReleaseCount);
    }

    [Fact]
    public void NativeTemplates_ShipRunnableContractTestsAndExplicitDependencies()
    {
        var root = FindRepositoryRoot();
        foreach (var template in new[] { "empty-plugin", "hotkey-plugin", "full-plugin" })
        {
            var directory = Path.Combine(root, "src", "Templates", template);
            var pluginProject = Directory
                .EnumerateFiles(directory, "*.csproj")
                .Single();
            var testProject = Directory
                .EnumerateFiles(Path.Combine(directory, "tests"), "*.Tests.csproj")
                .Single();
            var pluginContent = File.ReadAllText(pluginProject);
            var testContent = File.ReadAllText(testProject);

            Assert.Contains("PackageReference Include=\"Serilog\"", pluginContent);
            Assert.Contains("Compile Remove=\"tests\\**\\*.cs\"", pluginContent);
            Assert.Contains("LongBetterWindows.PluginSdk.Testing.csproj", testContent);
            Assert.True(File.Exists(
                Path.Combine(directory, "tests", "PluginContractTests.cs")));
        }
    }

    [Fact]
    public void Scaffold_AddsAndRunsGeneratedContractTestProject()
    {
        var scaffold = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "new-plugin.ps1"));

        Assert.Contains(
            "Move-Item $testProjectOld $testProjectNew",
            scaffold);
        Assert.Contains(
            "sln $slnFile add $csprojNew $testProjectNew",
            scaffold);
        Assert.Contains(
            "dotnet test $testProjectNew",
            scaffold);
    }

    [Fact]
    public void CiTemplates_CoverNativeScriptAndWebValidation()
    {
        var directory = Path.Combine(FindRepositoryRoot(), "sdk", "ci");
        var native = File.ReadAllText(Path.Combine(directory, "native-plugin.yml"));
        var script = File.ReadAllText(Path.Combine(directory, "script-plugin.yml"));
        var web = File.ReadAllText(Path.Combine(directory, "web-plugin.yml"));

        Assert.Contains("dotnet test $env:PLUGIN_TEST_PROJECT", native);
        Assert.Contains("validate-plugin.ps1", native);
        Assert.Contains("validate-plugin.ps1", script);
        Assert.Contains("pack-plugin.ps1", script);
        Assert.Contains("npm test", web);
        Assert.Contains("validate-plugin.ps1", web);
        Assert.Contains("pack-plugin.ps1", web);
    }

    [Fact]
    public void ProductionHost_ConsumesSharedCapabilityCatalog()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "LongBetterWindows.Host",
            "Engine",
            "HostProvider.cs"));

        Assert.Contains("HostCapabilityCatalog.ForService<T>()", source);
        Assert.DoesNotContain("GetCapabilityForService", source);
    }

    [Fact]
    public void NativeBoundaryAudit_MatchesActualHostProjectReferences()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "docs",
            "native-plugin-boundary-audit.json")));
        var audited = document.RootElement
            .GetProperty("host_coupled_projects")
            .EnumerateArray()
            .Select(item => item.GetProperty("project").GetString()!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var actual = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}Templates{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains(
                @"LongBetterWindows.Host\LongBetterWindows.Host.csproj",
                StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(audited, actual);
        Assert.Equal(
            "long_native_plugin_boundary_audit",
            document.RootElement.GetProperty("schema").GetString());
        Assert.All(
            document.RootElement
                .GetProperty("host_coupled_projects")
                .EnumerateArray(),
            item => Assert.NotEmpty(
                item.GetProperty("dependencies").EnumerateArray()));
    }

    private static PluginManifest CreateManifest() => new()
    {
        Id = "com.long.contract-fixture",
        Name = "Contract fixture",
        Version = "1.0.0",
        EntryPoint = "ContractFixture.dll",
    };

    private sealed class ScreenColorSamplerStub : IScreenColorSampler
    {
        public HostApiResponse<ScreenColorSample> Sample(
            int physicalX,
            int physicalY)
            => HostApiResponse<ScreenColorSample>.Success(new(
                (byte)physicalX,
                (byte)physicalY,
                30));
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

    private sealed class ContractFixturePlugin :
        ILongPlugin,
        IPluginCommandHandler,
        IPluginLanguageLifecycle,
        IPluginResourceLifecycle
    {
        public string Id => "com.long.contract-fixture";
        public string Name => LocalizedName ?? "Contract fixture";
        public string Version => "1.0.0";
        public PluginState State { get; private set; } = PluginState.Loaded;
        public string? LocalizedName { get; private set; }
        public int ReleaseCount { get; private set; }

        public Task<bool> InitializeAsync(IHostApi host) =>
            Task.FromResult(true);

        public Task<bool> StartAsync()
        {
            State = PluginState.Running;
            return Task.FromResult(true);
        }

        public Task<bool> StopAsync()
        {
            State = PluginState.Stopped;
            return Task.FromResult(true);
        }

        public Task<PluginCommandResult> ExecuteCommandAsync(
            PluginCommandInvocation invocation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PluginCommandResult.Success(invocation.Text));

        public Task OnLanguageChangedAsync(
            PluginLanguageContext context,
            CancellationToken cancellationToken = default)
        {
            LocalizedName = context.Resources.GetValueOrDefault("name");
            return Task.CompletedTask;
        }

        public Task ReleaseResourcesAsync()
        {
            ReleaseCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class NotificationStub : INotificationService
    {
        public Task<HostApiResponse> ShowAsync(string title, string body) =>
            Task.FromResult(HostApiResponse.Success());
    }
}
