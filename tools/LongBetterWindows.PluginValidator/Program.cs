using System.Text.Json;
using System.Text.Json.Serialization;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

var options = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
};

if (args.Length is < 1 or > 3
    || (args.Length == 3
        && !string.Equals(args[1], "--installed", StringComparison.OrdinalIgnoreCase)))
{
    Write(new
    {
        schema_version = 1,
        classification = "long_plugin_validation",
        is_success = false,
        error = "Usage: LongBetterWindows.PluginValidator <plugin-directory|package.lpak> [--installed <plugin-directory>]",
    });
    return 2;
}

try
{
    var target = Path.GetFullPath(args[0]);
    PluginManifest? installedManifest = null;
    if (args.Length == 3)
    {
        var installedResult = await ManifestReader.ReadAsync(Path.GetFullPath(args[2]));
        if (!installedResult.IsSuccess)
        {
            Write(new
            {
                schema_version = 1,
                classification = "long_plugin_validation",
                target,
                is_success = false,
                error = $"已安装插件 Manifest 无效：{installedResult.Error}",
                manifest_failure_code = installedResult.ErrorCode,
                manifest_issues = installedResult.Issues,
            });
            return 1;
        }
        installedManifest = installedResult.Manifest;
    }

    var validator = new PluginPackageValidator();
    var targetType = Directory.Exists(target) ? "directory" : "package";
    var result = targetType == "directory"
        ? await validator.ValidateDirectoryAsync(target, installedManifest)
        : await validator.ValidateAsync(
            target,
            installedManifest: installedManifest);
    var distribution = PluginDistributionPolicy.Assess(result);

    Write(new
    {
        schema_version = 1,
        classification = "long_plugin_validation",
        target,
        target_type = targetType,
        is_success = result.IsSuccess,
        error = result.Error,
        sha256 = result.Sha256,
        trust_level = result.TrustLevel,
        requires_high_trust_warning = result.RequiresHighTrustWarning,
        manifest = result.Manifest is null
            ? null
            : new
            {
                result.Manifest.Id,
                result.Manifest.Version,
                result.Manifest.Name,
                result.Manifest.Description,
                result.Manifest.Runtime,
                result.Manifest.EntryPoint,
                capabilities = result.Manifest.Capabilities,
                command_count = result.Manifest.Commands.Count,
            },
        permission_diff = result.PermissionDiff,
        permission_summary = new
        {
            requested = result.Manifest?.Capabilities
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray()
                ?? Array.Empty<string>(),
            added = result.PermissionDiff.Added,
            removed = result.PermissionDiff.Removed,
            unchanged = result.PermissionDiff.Unchanged,
            has_elevated_changes = result.PermissionDiff.HasElevatedChanges,
        },
        distribution_eligibility = new
        {
            local_import = new
            {
                eligible = distribution.LocalImportEligible,
                trust_level = result.TrustLevel,
                requires_high_trust_warning =
                    result.RequiresHighTrustWarning,
            },
            remote_marketplace = new
            {
                package_eligible =
                    distribution.RemoteMarketplacePackageEligible,
                currently_trusted =
                    distribution.RemoteMarketplaceCurrentlyTrusted,
                requires_publisher_signature =
                    distribution.RemoteMarketplaceRequiresPublisherSignature,
                block_reason =
                    distribution.RemoteMarketplaceBlockReason,
            },
        },
        manifest_failure_code = result.ManifestFailureCode,
        manifest_issues = result.ManifestIssues,
    });
    return result.IsSuccess ? 0 : 1;
}
catch (Exception exception)
{
    Write(new
    {
        schema_version = 1,
        classification = "long_plugin_validation",
        is_success = false,
        error = $"插件验证失败：{exception.Message}",
    });
    return 1;
}

void Write<T>(T value)
    => Console.WriteLine(JsonSerializer.Serialize(value, options));
