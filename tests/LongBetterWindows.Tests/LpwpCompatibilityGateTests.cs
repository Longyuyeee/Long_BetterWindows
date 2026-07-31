using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.Tests;

public sealed class LpwpCompatibilityGateTests
{
    [Fact]
    public void Machine_baseline_matches_runtime_contracts_and_fixtures()
    {
        using var baseline = JsonDocument.Parse(File.ReadAllText(Path(
            "docs", "protocol", "lpwp-compatibility-baseline.json")));
        var root = baseline.RootElement;
        Assert.Equal(IpcProtocol.Name, root.GetProperty("protocol").GetString());
        Assert.Equal("v1.1.0", ApiVersion.Current.ToString());
        Assert.Equal("1.1.0", root.GetProperty("plugin_api_current").GetString());
        Assert.Equal(IpcProtocol.MaximumFrameBytes, root.GetProperty("maximum_frame_bytes").GetInt32());
        var deadline = root.GetProperty("deadline_ms");
        Assert.Equal(IpcProtocol.MinimumDeadlineMilliseconds, deadline.GetProperty("minimum").GetInt32());
        Assert.Equal(IpcProtocol.DefaultDeadlineMilliseconds, deadline.GetProperty("default").GetInt32());
        Assert.Equal(IpcProtocol.MaximumDeadlineMilliseconds, deadline.GetProperty("maximum").GetInt32());

        var expectedMethods = typeof(BrokerMethods)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var baselineMethods = root.GetProperty("required_methods")
            .EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedMethods, baselineMethods);

        var expectedFixtures = root.GetProperty("fixture_files")
            .EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal).ToArray();
        var actualFixtures = Directory.GetFiles(Path("tests", "fixtures", "ipc"), "*.json")
            .Select(System.IO.Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedFixtures, actualFixtures);
    }

    [Fact]
    public void Security_baseline_is_backed_by_source_invariants()
    {
        using var baseline = JsonDocument.Parse(File.ReadAllText(Path(
            "docs", "protocol", "lpwp-compatibility-baseline.json")));
        foreach (var invariant in baseline.RootElement.GetProperty("security_invariants").EnumerateObject())
        {
            Assert.Equal(
                invariant.Name == "long_grid_loads_native_plugin_code" ? false : true,
                invariant.Value.GetBoolean());
        }

        var service = Read("src", "LongBetterWindows.Host", "Broker", "LongPluginBrokerService.cs");
        var authentication = Read("src", "LongBetterWindows.Host", "Broker", "BrokerClientAuthentication.cs");
        var commands = Read("src", "LongBetterWindows.Host", "Broker", "PluginCommandEndpoint.cs");
        var catalog = Read("src", "LongBetterWindows.Host", "Broker", "PluginCatalogProjection.cs");
        var diagnostics = Read("src", "LongBetterWindows.Host", "Broker", "BrokerDiagnostics.cs");
        Assert.Contains("PipeOptions.CurrentUserOnly", service);
        Assert.Contains("server.Sid", authentication);
        Assert.Contains("server.SessionId", authentication);
        Assert.Contains("server.IntegrityLevel", authentication);
        Assert.Contains("MaximumConcurrentCommandsPerPlugin = 4", commands);
        Assert.Contains("new CommandExecutor(registry)", commands);
        Assert.DoesNotContain("EntryPoint", catalog);
        Assert.DoesNotContain("DefaultSettings", catalog);
        Assert.DoesNotContain("Capabilities", catalog);
        Assert.DoesNotContain("Directory", catalog);
        Assert.DoesNotContain("payload", diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sid", diagnostics, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Threat_model_and_one_click_gate_cover_release_boundaries()
    {
        var threatModel = Read("docs", "protocol", "LPWP_SECURITY_THREAT_MODEL.md");
        Assert.Contains("## 2. 资产与信任边界", threatModel);
        Assert.Contains("## 3. 攻击与控制", threatModel);
        Assert.Contains("## 4. 隐私与日志", threatModel);
        Assert.Contains("## 5. 密钥与发布", threatModel);
        Assert.Contains("## 6. 残余风险与发布条件", threatModel);
        var gate = Read("verify-lpwp-compatibility.ps1");
        Assert.Contains("lpwp-compatibility-baseline.json", gate);
        Assert.Contains("LongBetterWindows.PluginIpc.csproj", gate);
        Assert.Contains("verify-plugin-runtime-matrix.ps1", gate);
        Assert.Contains("build-reference-widget.ps1", gate);
        Assert.Contains("lpwp-compatibility-report.json", gate);
    }

    [Fact]
    public void Signed_reference_workflow_requires_existing_ignored_key_and_independent_root_identity()
    {
        var publisher = Read("publish-signed-reference-widget.ps1");
        var verifier = Read("verify-marketplace-bundle.ps1");
        Assert.Contains("check-ignore", publisher);
        Assert.Contains("--untracked-files=no", publisher);
        Assert.Contains("PrivateKeyPath", publisher);
        Assert.Contains("ExpectedPublicKeyFingerprint", publisher);
        Assert.Contains("release_eligible = $false", publisher);
        Assert.DoesNotContain("RSA]::Create", publisher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--fingerprint", verifier);
        Assert.Contains("--key-id", verifier);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path(parts));

    private static string Path(params string[] parts)
        => System.IO.Path.Combine(new[] { FindRepositoryRoot() }.Concat(parts).ToArray());

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(System.IO.Path.Combine(current.FullName, "LongBetterWindows.sln")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
