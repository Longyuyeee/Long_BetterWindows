using System.IO;
using System.Text.Json;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.Tests;

public sealed class PluginWorkerMigrationReadinessTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string MatrixPath = Path.Combine(
        RepositoryRoot, "docs", "plugin-worker-migration-readiness.json");
    private static readonly string SchemaPath = Path.Combine(
        RepositoryRoot, "schemas", "plugin-worker-migration-readiness.schema.json");

    [Fact]
    public void Schema_LocksRejectedCandidateDecisionAndAcceptanceNext()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        var root = document.RootElement;
        var properties = root.GetProperty("properties");

        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("PX6B-8",
            properties.GetProperty("phase").GetProperty("const").GetString());
        Assert.Equal("sample_contract_split_rejected_worker_migration_paused",
            properties.GetProperty("decision").GetProperty("const").GetString());
        Assert.Equal(8, properties.GetProperty("assessed_existing_candidates")
            .GetProperty("const").GetInt32());
        Assert.Equal(0, properties.GetProperty("eligible_existing_candidates")
            .GetProperty("const").GetInt32());
        Assert.False(properties.GetProperty("production_enabled")
            .GetProperty("const").GetBoolean());
        Assert.Equal(0, properties.GetProperty("real_plugins_migrated")
            .GetProperty("const").GetInt32());
        Assert.Equal("unsigned_rc_acceptance_and_manual_plugin_validation",
            properties.GetProperty("recommended_next").GetProperty("const").GetString());
    }

    [Fact]
    public void Matrix_AssessesEveryNativeOrHybridComponentAgainstSourceFacts()
    {
        using var matrix = JsonDocument.Parse(File.ReadAllText(MatrixPath));
        var root = matrix.RootElement;
        var candidates = root.GetProperty("candidates").EnumerateArray().ToArray();
        var expected = ReadCandidateManifests();

        Assert.Equal("PX6B-8", root.GetProperty("phase").GetString());
        Assert.Equal(8, root.GetProperty("assessed_existing_candidates").GetInt32());
        Assert.Equal(0, root.GetProperty("eligible_existing_candidates").GetInt32());
        Assert.False(root.GetProperty("production_enabled").GetBoolean());
        Assert.Equal(0, root.GetProperty("real_plugins_migrated").GetInt32());
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal),
            candidates.Select(CandidateId).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(candidates,
            candidate => candidate.GetProperty("decision").GetString() == "eligible");

        foreach (var candidate in candidates)
        {
            var id = CandidateId(candidate);
            var expectedManifest = expected[id];
            Assert.Equal(expectedManifest.Path,
                candidate.GetProperty("manifest").GetString());
            Assert.Equal(expectedManifest.Capabilities.Order(StringComparer.Ordinal),
                ReadStrings(candidate.GetProperty("capabilities")).Order(StringComparer.Ordinal));

            var projectPath = FullPath(candidate.GetProperty("component_project").GetString()!);
            Assert.True(File.Exists(projectPath), $"Candidate project is missing: {projectPath}");
            var source = ReadProjectSource(Path.GetDirectoryName(projectPath)!);
            Assert.Equal(source.Contains("IHasMainUI", StringComparison.Ordinal),
                candidate.GetProperty("has_host_ui").GetBoolean());
            Assert.Equal(source.Contains("IHostApi", StringComparison.Ordinal),
                candidate.GetProperty("direct_ihostapi_object").GetBoolean());

            Assert.NotEmpty(candidate.GetProperty("blockers").EnumerateArray());
            Assert.NotEmpty(candidate.GetProperty("required_proxy_methods").EnumerateArray());
            foreach (var evidence in candidate.GetProperty("evidence").EnumerateArray())
            {
                var path = FullPath(evidence.GetProperty("path").GetString()!);
                Assert.True(File.Exists(path), $"Readiness evidence is missing: {path}");
                Assert.Contains(
                    evidence.GetProperty("contains").GetString()!,
                    File.ReadAllText(path),
                    StringComparison.Ordinal);
            }
        }

        var reference = Assert.Single(candidates, candidate =>
            candidate.GetProperty("decision").GetString() == "reference_split_only");
        Assert.Equal("com.long.sample", CandidateId(reference));
        Assert.False(reference.GetProperty("headless_component").GetBoolean());
    }

    [Fact]
    public void Matrix_RecordsSeparateNonCatalogReferenceBeforeProductionMigration()
    {
        using var matrix = JsonDocument.Parse(File.ReadAllText(MatrixPath));
        var root = matrix.RootElement;
        Assert.Equal("unsigned_rc_acceptance_and_manual_plugin_validation",
            root.GetProperty("recommended_next").GetString());
        Assert.Equal(8, root.GetProperty("exit_criteria")
            .EnumerateArray().Select(item => item.GetString()).Distinct().Count());

        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot, "catalog", "plugin-catalog.json")));
        Assert.DoesNotContain(
            catalog.RootElement.GetProperty("entries").EnumerateArray(),
            entry => entry.GetProperty("manifest").GetString()!
                .Contains("PluginWorker", StringComparison.OrdinalIgnoreCase));

        var reference = root.GetProperty("reference_workload");
        Assert.Equal("reference.headless.native",
            reference.GetProperty("plugin_id").GetString());
        Assert.False(reference.GetProperty("cataloged").GetBoolean());
        Assert.False(reference.GetProperty("host_or_wpf_reference").GetBoolean());
        Assert.False(reference.GetProperty("ambient_user_data").GetBoolean());
        Assert.False(reference.GetProperty("system_side_effects").GetBoolean());

        var policy = root.GetProperty("worker_policy");
        Assert.Equal("verified_package_file_manifest_required",
            policy.GetProperty("package_hash_source").GetString());
        Assert.True(policy.GetProperty("trusted_descriptor_bridge").GetBoolean());
        Assert.True(policy.GetProperty("sealed_file_evidence").GetBoolean());
        Assert.True(policy.GetProperty("post_extraction_file_reverification").GetBoolean());
        Assert.True(policy.GetProperty("production_release_gate_closed").GetBoolean());
        Assert.True(policy.GetProperty("host_preflight_sha256").GetBoolean());
        Assert.True(policy.GetProperty("worker_post_handshake_sha256").GetBoolean());
        Assert.True(policy.GetProperty("load_verified_bytes").GetBoolean());
        Assert.True(policy.GetProperty("reject_reparse_points").GetBoolean());
        Assert.Equal(64 * 1024 * 1024,
            policy.GetProperty("maximum_assembly_bytes").GetInt32());
        Assert.Equal([PluginWorkerProtocol.HostCapabilityQuery],
            ReadStrings(policy.GetProperty("approved_host_methods")));
        Assert.Empty(policy.GetProperty("reference_allowed_host_methods").EnumerateArray());
    }

    [Fact]
    public void Matrix_RejectsSampleSplitFromCurrentSourceFacts()
    {
        using var matrix = JsonDocument.Parse(File.ReadAllText(MatrixPath));
        var audit = matrix.RootElement.GetProperty("first_candidate_audit");
        Assert.Equal("com.long.sample", audit.GetProperty("plugin_id").GetString());
        Assert.Equal("sample.hello", audit.GetProperty("command_id").GetString());
        Assert.Equal("rejected_no_meaningful_worker_contract",
            audit.GetProperty("verdict").GetString());
        Assert.Empty(audit.GetProperty("pure_worker_operations").EnumerateArray());
        Assert.Equal(
            ["localized_ui_presentation", "notification.show"],
            ReadStrings(audit.GetProperty("host_owned_operations"))
                .Order(StringComparer.Ordinal));
        Assert.Equal(["system.notification"],
            ReadStrings(audit.GetProperty("source_capabilities")));
        Assert.False(audit.GetProperty("production_component_created").GetBoolean());
        Assert.False(audit.GetProperty("production_route_changed").GetBoolean());
        Assert.True(audit.GetProperty("blockers").GetArrayLength() >= 2);

        var source = File.ReadAllText(FullPath("src/SamplePlugin/HelloPlugin.cs"));
        Assert.Contains("IHasMainUI", source, StringComparison.Ordinal);
        Assert.Contains("_notification = host.Notification", source, StringComparison.Ordinal);
        Assert.Contains("_notification.ShowAsync", source, StringComparison.Ordinal);
        Assert.Contains("ShowMainUI()", source, StringComparison.Ordinal);
        Assert.Contains("ShowReadyAsync()", source, StringComparison.Ordinal);

        using var manifest = JsonDocument.Parse(File.ReadAllText(
            FullPath("src/SamplePlugin/manifest.json")));
        Assert.Equal(["system.notification"],
            ReadStrings(manifest.RootElement.GetProperty("capabilities")));
        Assert.Contains(
            manifest.RootElement.GetProperty("commands").EnumerateArray(),
            command => command.GetProperty("id").GetString() == "sample.hello");
        Assert.False(Directory.Exists(FullPath("src/SamplePlugin.Worker")));
    }

    private static Dictionary<string, ManifestFact> ReadCandidateManifests()
    {
        var result = new Dictionary<string, ManifestFact>(StringComparer.Ordinal);
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot, "catalog", "plugin-catalog.json")));
        foreach (var entry in catalog.RootElement.GetProperty("entries").EnumerateArray())
        {
            var path = entry.GetProperty("manifest").GetString()!;
            using var manifest = JsonDocument.Parse(File.ReadAllText(FullPath(path)));
            var root = manifest.RootElement;
            var runtime = root.TryGetProperty("runtime", out var runtimeElement)
                ? runtimeElement.GetString()
                : "native";
            var isHybrid = runtime == "webview" && root.TryGetProperty("background", out _);
            if (runtime == "webview" && !isHybrid) continue;
            result.Add(
                root.GetProperty("id").GetString()!,
                new ManifestFact(
                    path,
                    root.TryGetProperty("capabilities", out var capabilities)
                        ? ReadStrings(capabilities).ToArray()
                        : []));
        }
        return result;
    }

    private static string CandidateId(JsonElement candidate)
        => candidate.GetProperty("plugin_id").GetString()!;

    private static string ReadProjectSource(string projectDirectory)
        => string.Join('\n', Directory.EnumerateFiles(
                projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase)
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText));

    private static IEnumerable<string> ReadStrings(JsonElement element)
        => element.EnumerateArray().Select(item => item.GetString()!);

    private static string FullPath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(
            RepositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.StartsWith(
            RepositoryRoot + Path.DirectorySeparatorChar,
            fullPath,
            StringComparison.OrdinalIgnoreCase);
        return fullPath;
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

    private sealed record ManifestFact(string Path, IReadOnlyList<string> Capabilities);
}
