using System.IO;
using System.Text;

namespace QuickLaunchPlugin;

public sealed record QuickLaunchDiskSearchResult(
    IReadOnlyList<SmartEntry> Entries,
    int InspectedCount,
    bool CandidateLimitReached);

public sealed class QuickLaunchDiskSearchEngine
{
    public const int DefaultMaximumFileCandidates = 5_000;
    public const int DefaultMaximumContentCandidates = 500;
    public const long DefaultMaximumContentFileBytes = 1_048_576;

    private static readonly HashSet<string> ContentExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".cs", ".json", ".xml", ".html",
            ".css", ".js", ".py", ".log", ".csv",
        };

    private readonly IReadOnlyList<string> _fileRoots;
    private readonly IReadOnlyList<string> _contentRoots;
    private readonly int _maximumFileCandidates;
    private readonly int _maximumContentCandidates;
    private readonly long _maximumContentFileBytes;

    public QuickLaunchDiskSearchEngine(
        IEnumerable<string>? fileRoots = null,
        IEnumerable<string>? contentRoots = null,
        int maximumFileCandidates = DefaultMaximumFileCandidates,
        int maximumContentCandidates = DefaultMaximumContentCandidates,
        long maximumContentFileBytes = DefaultMaximumContentFileBytes)
    {
        _fileRoots = NormalizeRoots(fileRoots ?? GetDefaultFileRoots());
        _contentRoots = NormalizeRoots(
            contentRoots
            ?? new[]
            {
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments),
            });
        _maximumFileCandidates = Math.Max(1, maximumFileCandidates);
        _maximumContentCandidates = Math.Max(1, maximumContentCandidates);
        _maximumContentFileBytes = Math.Max(1, maximumContentFileBytes);
    }

    public QuickLaunchDiskSearchResult SearchFiles(
        string query,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || maximumResults <= 0)
            return new QuickLaunchDiskSearchResult([], 0, false);

        var entries = new List<SmartEntry>();
        var inspected = 0;
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var candidates = new FairFileEnumerator(_fileRoots);
        while (inspected < _maximumFileCandidates
               && candidates.TryGetNext(out var path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seenPaths.Add(path))
                continue;

            inspected++;
            if (!Path.GetFileName(path).Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            if (QuickLaunchTargetPolicy.IsPotentiallyExecutablePath(path))
                continue;

            entries.Add(CreateFileEntry(
                path,
                FindContainingRoot(path, _fileRoots)));
            if (entries.Count >= maximumResults)
                return new QuickLaunchDiskSearchResult(
                    entries,
                    inspected,
                    inspected >= _maximumFileCandidates
                    && candidates.HasRemaining);
        }

        return new QuickLaunchDiskSearchResult(
            entries,
            inspected,
            inspected >= _maximumFileCandidates && candidates.HasRemaining);
    }

    public QuickLaunchDiskSearchResult SearchContent(
        string query,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || maximumResults <= 0)
            return new QuickLaunchDiskSearchResult([], 0, false);

        var entries = new List<SmartEntry>();
        var inspected = 0;
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var candidates = new FairFileEnumerator(_contentRoots);
        while (inspected < _maximumContentCandidates
               && candidates.TryGetNext(out var path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seenPaths.Add(path))
                continue;

            inspected++;
            if (!ContentExtensions.Contains(Path.GetExtension(path)))
                continue;
            if (QuickLaunchTargetPolicy.IsPotentiallyExecutablePath(path))
                continue;

            try
            {
                var info = new FileInfo(path);
                if (info.Length > _maximumContentFileBytes)
                    continue;
                var content = ReadText(path, cancellationToken);
                var match = content.IndexOf(
                    query,
                    StringComparison.OrdinalIgnoreCase);
                if (match < 0)
                    continue;

                entries.Add(CreateContentEntry(path, content, match));
                if (entries.Count >= maximumResults)
                    return new QuickLaunchDiskSearchResult(
                        entries,
                        inspected,
                        inspected >= _maximumContentCandidates
                        && candidates.HasRemaining);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException)
            {
                // A single changing file must not fail the whole query.
            }
        }

        return new QuickLaunchDiskSearchResult(
            entries,
            inspected,
            inspected >= _maximumContentCandidates && candidates.HasRemaining);
    }

    private static string ReadText(
        string path,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4_096,
            FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        var content = new StringBuilder();
        var buffer = new char[4_096];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = reader.Read(buffer, 0, buffer.Length);
            if (read == 0)
                return content.ToString();
            content.Append(buffer, 0, read);
        }
    }

    private static string FindContainingRoot(
        string path,
        IReadOnlyList<string> roots)
        => roots.FirstOrDefault(root =>
               path.StartsWith(
                   Path.TrimEndingDirectorySeparator(root)
                       + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase))
           ?? Path.GetDirectoryName(path)
           ?? string.Empty;

    private static IEnumerable<string> EnumerateFiles(string root)
        => Directory.EnumerateFiles(
            root,
            "*",
            new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip =
                    FileAttributes.System | FileAttributes.ReparsePoint,
                ReturnSpecialDirectories = false,
            });

    private sealed class FairFileEnumerator : IDisposable
    {
        private readonly Queue<IEnumerator<string>> _enumerators = new();

        public FairFileEnumerator(IEnumerable<string> roots)
        {
            foreach (var root in roots.Where(Directory.Exists))
            {
                try
                {
                    _enumerators.Enqueue(EnumerateFiles(root).GetEnumerator());
                }
                catch (Exception exception) when (
                    exception is IOException
                        or UnauthorizedAccessException
                        or DirectoryNotFoundException)
                {
                    // A root can disappear or become inaccessible while searching.
                }
            }
        }

        public bool HasRemaining => _enumerators.Count > 0;

        public bool TryGetNext(out string path)
        {
            while (_enumerators.TryDequeue(out var enumerator))
            {
                try
                {
                    if (enumerator.MoveNext())
                    {
                        path = enumerator.Current;
                        _enumerators.Enqueue(enumerator);
                        return true;
                    }
                }
                catch (Exception exception) when (
                    exception is IOException
                        or UnauthorizedAccessException
                        or DirectoryNotFoundException)
                {
                    // Continue with the remaining roots.
                }

                enumerator.Dispose();
            }

            path = string.Empty;
            return false;
        }

        public void Dispose()
        {
            while (_enumerators.TryDequeue(out var enumerator))
                enumerator.Dispose();
        }
    }

    private static SmartEntry CreateFileEntry(string path, string root)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var icon = extension switch
        {
            ".pdf" => "📕",
            ".doc" or ".docx" => "📝",
            ".xls" or ".xlsx" => "📊",
            ".png" or ".jpg" or ".jpeg" or ".gif" => "🖼",
            ".txt" or ".md" => "📄",
            ".zip" or ".rar" or ".7z" => "📦",
            _ => "📁",
        };
        return new SmartEntry
        {
            Name = Path.GetFileName(path),
            Path = path,
            Icon = icon,
            Category = "file",
            Subtitle = root,
        };
    }

    private static SmartEntry CreateContentEntry(
        string path,
        string content,
        int match)
    {
        var start = Math.Max(0, match - 20);
        var length = Math.Min(80, content.Length - start);
        var preview = content
            .Substring(start, length)
            .Replace("\n", " ")
            .Replace("\r", "");
        return new SmartEntry
        {
            Name = Path.GetFileName(path),
            Path = path,
            Icon = "🔍",
            Category = "content",
            Subtitle = "..." + preview + "...",
        };
    }

    private static IReadOnlyList<string> NormalizeRoots(
        IEnumerable<string> roots)
        => roots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string> GetDefaultFileRoots()
    {
        yield return Environment.GetFolderPath(
            Environment.SpecialFolder.Desktop);
        yield return Environment.GetFolderPath(
            Environment.SpecialFolder.MyDocuments);
        yield return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            "Downloads");
    }
}
