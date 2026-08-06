using System.Security.Cryptography;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.PluginIpc.Client;

internal sealed record PluginWorkerWorkloadLaunchPolicy(
    string PackageRoot,
    string AssemblyPath,
    string ExpectedSha256,
    IReadOnlySet<string> AllowedHostMethods);

internal sealed record ValidatedPluginWorkerWorkloadLaunchPolicy(
    string AssemblyPath,
    string ExpectedSha256,
    IReadOnlyList<string> AllowedHostMethods);

internal static class PluginWorkerWorkloadPolicyValidator
{
    private const long MaximumAssemblyBytes = 64 * 1024 * 1024;

    internal static ValidatedPluginWorkerWorkloadLaunchPolicy Validate(
        PluginWorkerWorkloadLaunchPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy.PackageRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy.AssemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy.ExpectedSha256);
        ArgumentNullException.ThrowIfNull(policy.AllowedHostMethods);

        var packageRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(policy.PackageRoot));
        var assemblyPath = Path.GetFullPath(policy.AssemblyPath);
        var relative = Path.GetRelativePath(packageRoot, assemblyPath);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Plugin worker workload must remain inside its verified package root.");
        }
        if (!string.Equals(Path.GetExtension(assemblyPath), ".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Plugin worker workload must be a DLL assembly.");
        RejectReparsePoints(packageRoot, assemblyPath);

        byte[] expectedHash;
        try
        {
            expectedHash = Convert.FromHexString(policy.ExpectedSha256);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException(
                "Plugin worker workload SHA-256 is invalid.", ex);
        }
        if (expectedHash.Length != SHA256.HashSizeInBytes)
            throw new InvalidDataException("Plugin worker workload SHA-256 is invalid.");

        var actualHash = HashAssembly(assemblyPath);
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            throw new InvalidDataException("Plugin worker workload SHA-256 does not match policy.");

        var allowedHostMethods = policy.AllowedHostMethods
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (allowedHostMethods.Any(string.IsNullOrWhiteSpace)
            || allowedHostMethods.Any(method => !PluginWorkerProtocol.HostMethods.Contains(method)))
        {
            throw new InvalidDataException(
                "Plugin worker workload Host method policy is invalid.");
        }

        return new ValidatedPluginWorkerWorkloadLaunchPolicy(
            assemblyPath,
            Convert.ToHexString(expectedHash),
            allowedHostMethods);
    }

    private static byte[] HashAssembly(string assemblyPath)
    {
        using var stream = new FileStream(
            assemblyPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        if (stream.Length is <= 0 or > MaximumAssemblyBytes)
            throw new InvalidDataException("Plugin worker workload assembly size is invalid.");
        return SHA256.HashData(stream);
    }

    private static void RejectReparsePoints(string packageRoot, string assemblyPath)
    {
        for (var current = assemblyPath; ; current = Path.GetDirectoryName(current)!)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "Plugin worker workload package path cannot contain reparse points.");
            if (string.Equals(current, packageRoot, StringComparison.OrdinalIgnoreCase))
                return;
            if (Path.GetDirectoryName(current) is null)
                throw new InvalidDataException(
                    "Plugin worker workload package root could not be verified.");
        }
    }
}
