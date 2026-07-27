using System.IO;

namespace QuickLaunchPlugin;

public sealed record QuickLaunchTargetValidation(
    bool IsValid,
    string? NormalizedTarget,
    string? Error);

public sealed class QuickLaunchTargetPolicy
{
    private static readonly HashSet<string> PotentiallyExecutableExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".com", ".bat", ".cmd", ".ps1", ".msi", ".msp",
            ".scr", ".lnk", ".url", ".reg", ".js", ".jse", ".vbs",
            ".vbe", ".wsf", ".wsh", ".hta", ".cpl",
        };

    private readonly IReadOnlyList<string> _applicationRoots;

    public QuickLaunchTargetPolicy(
        IEnumerable<string>? applicationRoots = null)
    {
        _applicationRoots = (applicationRoots ?? GetApplicationRoots())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public QuickLaunchTargetValidation Validate(
        string category,
        string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return Invalid("Target is required.");

        switch (category)
        {
            case "calculation":
                return target.Length <= 4_096
                    ? Valid(target)
                    : Invalid("Calculation result is too large.");

            case "link":
                return Uri.TryCreate(target, UriKind.Absolute, out var uri)
                    && (uri.Scheme.Equals(
                            Uri.UriSchemeHttp,
                            StringComparison.OrdinalIgnoreCase)
                        || uri.Scheme.Equals(
                            Uri.UriSchemeHttps,
                            StringComparison.OrdinalIgnoreCase))
                    ? Valid(uri.AbsoluteUri)
                    : Invalid("Only HTTP and HTTPS links can be opened.");

            case "file":
            case "content":
                return ValidateExistingFile(target, requireShortcut: false);

            case "application":
                var application = ValidateExistingFile(
                    target,
                    requireShortcut: true);
                if (!application.IsValid)
                    return application;
                return _applicationRoots.Any(root =>
                        IsWithinRoot(application.NormalizedTarget!, root))
                    ? application
                    : Invalid(
                        "Application shortcuts must come from a Start Menu root.");

            default:
                return Invalid("Unsupported quick-launch target category.");
        }
    }

    private static QuickLaunchTargetValidation ValidateExistingFile(
        string target,
        bool requireShortcut)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(target);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return Invalid("Target path is invalid.");
        }

        if (!File.Exists(fullPath))
            return Invalid("Target file does not exist.");
        if (requireShortcut
            && !Path.GetExtension(fullPath).Equals(
                ".lnk",
                StringComparison.OrdinalIgnoreCase))
            return Invalid("Application target must be a Start Menu shortcut.");
        try
        {
            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                return Invalid("Reparse-point targets are not allowed.");
            if (!requireShortcut && IsPotentiallyExecutablePath(fullPath))
                return Invalid(
                    "Executable and script files must be launched through a trusted application shortcut.");
            return Valid(fullPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Invalid("Target file is no longer accessible.");
        }
    }

    public static bool IsPotentiallyExecutablePath(string path)
        => PotentiallyExecutableExtensions.Contains(Path.GetExtension(path));

    private static bool IsWithinRoot(string path, string root)
        => path.Equals(root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(
                root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

    private static QuickLaunchTargetValidation Valid(string target)
        => new(true, target, null);

    private static QuickLaunchTargetValidation Invalid(string error)
        => new(false, null, error);

    private static IEnumerable<string> GetApplicationRoots()
    {
        yield return Environment.GetFolderPath(
            Environment.SpecialFolder.CommonStartMenu);
        yield return Environment.GetFolderPath(
            Environment.SpecialFolder.StartMenu);
    }
}

public sealed class QuickLaunchQueryGeneration
{
    private long _current;

    public long Begin() => Interlocked.Increment(ref _current);

    public bool IsCurrent(long generation)
        => Volatile.Read(ref _current) == generation;

    public void Invalidate() => Interlocked.Increment(ref _current);
}
