using System.IO;
using System.Text.Json;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.Tests;

public sealed class PluginIsolationCompatibilityTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string MatrixPath = Path.Combine(
        RepositoryRoot, "docs", "plugin-isolation-compatibility-matrix.json");
    private static readonly string SchemaPath = Path.Combine(
        RepositoryRoot, "schemas", "plugin-isolation-compatibility.schema.json");

    [Fact]
    public void Schema_LocksProfilesReadinessAndAssessmentOnlyPolicy()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        var root = document.RootElement;
        var definitions = root.GetProperty("$defs");
        var profileProperties = definitions
            .GetProperty("profile")
            .GetProperty("properties");

        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            root.GetProperty("$schema").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.False(definitions.GetProperty("profile")
            .GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[]
            {
                "csharp_script",
                "native_dll",
                "webview",
                "webview_native_background",
            },
            ReadStrings(profileProperties.GetProperty("id").GetProperty("enum"))
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(
            new[] { "candidate_after_contract", "retain_current_boundary" },
            ReadStrings(profileProperties.GetProperty("readiness").GetProperty("enum"))
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(
            "assessment_only",
            definitions.GetProperty("policy")
                .GetProperty("properties")
                .GetProperty("decision")
                .GetProperty("const")
                .GetString());
        var experiment = definitions.GetProperty("experiment");
        var experimentProperties = experiment.GetProperty("properties");
        Assert.False(experiment.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("PX6B-3",
            experimentProperties.GetProperty("phase").GetProperty("const").GetString());
        Assert.Equal("read_only_capability_proxy_validated",
            experimentProperties.GetProperty("status").GetProperty("const").GetString());
        Assert.False(experimentProperties.GetProperty("production_enabled")
            .GetProperty("const").GetBoolean());
        Assert.Equal(0, experimentProperties.GetProperty("real_plugins_migrated")
            .GetProperty("const").GetInt32());
        var workerContract = definitions.GetProperty("workerContract");
        Assert.False(workerContract.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("host.capability.query",
            workerContract.GetProperty("properties")
                .GetProperty("host_proxy_methods")
                .GetProperty("items")
                .GetProperty("const")
                .GetString());
    }

    [Fact]
    public void Matrix_TracksAuthoritativeInventoryAndSourceEvidence()
    {
        using var matrix = ReadMatrix();
        var root = matrix.RootElement;
        var profiles = root.GetProperty("profiles").EnumerateArray().ToArray();
        var expectedInventory = ReadAuthoritativeInventory();

        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("PX6B-1", root.GetProperty("policy").GetProperty("phase").GetString());
        Assert.Equal(4, profiles.Length);
        Assert.Equal(4, profiles.Select(ProfileId).Distinct(StringComparer.Ordinal).Count());

        foreach (var profile in profiles)
        {
            var id = ProfileId(profile);
            Assert.Equal(
                expectedInventory[id],
                profile.GetProperty("current_inventory_count").GetInt32());
            Assert.NotEmpty(profile.GetProperty("blockers").EnumerateArray());

            foreach (var evidence in profile.GetProperty("evidence").EnumerateArray())
            {
                var relativePath = evidence.GetProperty("path").GetString()!;
                var fullPath = Path.GetFullPath(Path.Combine(
                    RepositoryRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));
                Assert.StartsWith(
                    RepositoryRoot + Path.DirectorySeparatorChar,
                    fullPath,
                    StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(fullPath), $"Isolation evidence is missing: {relativePath}");
                Assert.Contains(
                    evidence.GetProperty("contains").GetString()!,
                    File.ReadAllText(fullPath),
                    StringComparison.Ordinal);
            }
        }

        Assert.Equal(
            "retain_current_boundary",
            profiles.Single(profile => ProfileId(profile) == "webview")
                .GetProperty("readiness")
                .GetString());
        Assert.Equal(
            "synthetic_headless_native_command_worker",
            root.GetProperty("policy").GetProperty("first_experiment").GetString());
        var experiment = root.GetProperty("experiment");
        Assert.Equal("PX6B-3", experiment.GetProperty("phase").GetString());
        Assert.Equal("read_only_capability_proxy_validated", experiment.GetProperty("status").GetString());
        Assert.False(experiment.GetProperty("production_enabled").GetBoolean());
        Assert.Equal(0, experiment.GetProperty("real_plugins_migrated").GetInt32());
        Assert.Equal(15, experiment.GetProperty("validated_gates")
            .EnumerateArray().Select(item => item.GetString()).Distinct().Count());
    }

    [Fact]
    public void WorkerContract_ReusesFramingLimitsButHasDedicatedNamespace()
    {
        using var matrix = ReadMatrix();
        var contract = matrix.RootElement.GetProperty("shared_worker_contract");

        Assert.Equal("lpwp_length_prefixed_json_framing",
            contract.GetProperty("transport_reuse").GetString());
        Assert.NotEqual(IpcProtocol.Name, contract.GetProperty("protocol_namespace").GetString());
        Assert.Equal(IpcProtocol.MaximumFrameBytes,
            contract.GetProperty("maximum_frame_bytes").GetInt32());
        Assert.Equal(IpcProtocol.DefaultDeadlineMilliseconds,
            contract.GetProperty("default_deadline_ms").GetInt32());
        Assert.Equal(IpcProtocol.MinimumDeadlineMilliseconds,
            contract.GetProperty("minimum_deadline_ms").GetInt32());
        Assert.Equal(IpcProtocol.MaximumDeadlineMilliseconds,
            contract.GetProperty("maximum_deadline_ms").GetInt32());
        Assert.Equal(7, contract.GetProperty("lifecycle_methods")
            .EnumerateArray().Select(item => item.GetString()).Distinct().Count());
        Assert.Equal(
            "wpf_and_webview_ui_remain_in_host_process",
            contract.GetProperty("ui_rule").GetString());
        Assert.Equal(
            [PluginWorkerProtocol.HostCapabilityQuery],
            ReadStrings(contract.GetProperty("host_proxy_methods")));
        Assert.Equal(
            "session_owned_lifo_release_on_close_or_crash",
            contract.GetProperty("resource_lease_rule").GetString());
    }

    private static Dictionary<string, int> ReadAuthoritativeInventory()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["native_dll"] = 0,
            ["csharp_script"] = 0,
            ["webview"] = 0,
            ["webview_native_background"] = 0,
        };
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot, "catalog", "plugin-catalog.json")));
        foreach (var entry in catalog.RootElement.GetProperty("entries").EnumerateArray())
        {
            var manifestPath = entry.GetProperty("manifest").GetString()!;
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                RepositoryRoot,
                manifestPath.Replace('/', Path.DirectorySeparatorChar))));
            var manifestRoot = manifest.RootElement;
            var runtime = manifestRoot.TryGetProperty("runtime", out var runtimeElement)
                ? runtimeElement.GetString()
                : "native";
            var profileId = runtime switch
            {
                "csharp-script" => "csharp_script",
                "webview" when manifestRoot.TryGetProperty("background", out _) =>
                    "webview_native_background",
                "webview" => "webview",
                _ => "native_dll",
            };
            counts[profileId]++;
        }

        return counts;
    }

    private static JsonDocument ReadMatrix()
        => JsonDocument.Parse(File.ReadAllText(MatrixPath));

    private static string ProfileId(JsonElement profile)
        => profile.GetProperty("id").GetString()!;

    private static IEnumerable<string> ReadStrings(JsonElement element)
        => element.EnumerateArray().Select(item => item.GetString()!);

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
}
