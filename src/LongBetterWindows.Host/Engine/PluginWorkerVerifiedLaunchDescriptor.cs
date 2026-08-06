using System.IO;
using LongBetterWindows.PluginIpc.Client;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.Host.Engine;

internal sealed class PluginWorkerVerifiedLaunchDescriptor
{
    private PluginWorkerVerifiedLaunchDescriptor(
        string pluginId,
        PluginWorkerWorkloadLaunchPolicy launchPolicy)
    {
        PluginId = pluginId;
        LaunchPolicy = launchPolicy;
    }

    internal string PluginId { get; }
    internal PluginWorkerWorkloadLaunchPolicy LaunchPolicy { get; }

    internal static PluginWorkerVerifiedLaunchDescriptor CreateCandidate(
        PackageValidationResult validation,
        string installedPackageRoot,
        IReadOnlySet<string>? allowedHostMethods = null)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentException.ThrowIfNullOrWhiteSpace(installedPackageRoot);
        if (!validation.IsSuccess || validation.Manifest is null)
            throw new InvalidDataException("A successful package validation is required.");
        if (!PluginPackageValidator.TryGetVerifiedPackageFiles(validation, out var verifiedFiles))
        {
            throw new InvalidDataException(
                "Worker candidates require sealed package file evidence.");
        }
        if (!PluginPackageValidator.VerifyExtractedPackageFiles(
            validation,
            installedPackageRoot,
            out var verificationError))
        {
            throw new InvalidDataException(verificationError);
        }

        var entryPoint = validation.Manifest.EntryPoint.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(entryPoint)
            || !verifiedFiles.TryGetValue(entryPoint, out var entryPointEvidence))
        {
            throw new InvalidDataException(
                "Plugin entry point is not covered by verified package file evidence.");
        }

        var hostMethods = allowedHostMethods
            ?? new HashSet<string>(StringComparer.Ordinal);
        if (hostMethods.Any(string.IsNullOrWhiteSpace)
            || hostMethods.Any(method => !PluginWorkerProtocol.HostMethods.Contains(method)))
        {
            throw new InvalidDataException("Worker candidate Host method policy is invalid.");
        }

        var packageRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(installedPackageRoot));
        var assemblyPath = Path.GetFullPath(Path.Combine(
            packageRoot,
            entryPoint.Replace('/', Path.DirectorySeparatorChar)));
        var launchPolicy = new PluginWorkerWorkloadLaunchPolicy(
            packageRoot,
            assemblyPath,
            entryPointEvidence.Sha256,
            new HashSet<string>(hostMethods, StringComparer.Ordinal));
        PluginWorkerWorkloadPolicyValidator.Validate(launchPolicy);

        return new PluginWorkerVerifiedLaunchDescriptor(
            validation.Manifest.Id,
            launchPolicy);
    }
}

internal static class PluginWorkerProductionReleaseGate
{
    internal const bool ProductionEnabled = false;

    internal static PluginWorkerWorkloadLaunchPolicy Approve(
        PluginWorkerVerifiedLaunchDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!IsProductionEnabled())
        {
            throw new InvalidOperationException(
                "Production plugin worker release is disabled.");
        }

        return descriptor.LaunchPolicy;
    }

    private static bool IsProductionEnabled() => ProductionEnabled;
}
