using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LongBetterWindows.Host.Interaction
{
    public sealed class LocalFileSearchProvider : ISearchProvider
    {
        private const int MaximumIndexedEntries = 75000;
        private readonly IReadOnlyList<string> _roots;
        private readonly object _indexLock = new();
        private Task<IReadOnlyList<IndexedPath>>? _indexTask;
        private IReadOnlyList<IndexedPath> _snapshot = Array.Empty<IndexedPath>();

        public LocalFileSearchProvider(IEnumerable<string>? roots = null)
        {
            _roots = (roots ?? GetDefaultRoots())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public string Id => "local-files";
        public int Priority => 320;

        public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken = default)
        {
            var query = request.Query.Trim();
            if (query.Length < 2) return Array.Empty<SearchResultItem>();

            var indexTask = GetIndexAsync();
            var completed = await Task.WhenAny(
                indexTask,
                Task.Delay(TimeSpan.FromMilliseconds(600), cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            var index = completed == indexTask
                ? await indexTask
                : Volatile.Read(ref _snapshot);
            return index
                .Select(item => (item, score: Score(item, query)))
                .Where(match => match.score > 0)
                .OrderByDescending(match => match.score)
                .ThenBy(match => match.item.Name, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Min(8, request.MaxResults))
                .Select(match => CreateResult(match.item, match.score))
                .ToList();
        }

        private Task<IReadOnlyList<IndexedPath>> GetIndexAsync()
        {
            lock (_indexLock)
                return _indexTask ??= Task.Run(BuildIndex);
        }

        private IReadOnlyList<IndexedPath> BuildIndex()
        {
            var results = new List<IndexedPath>();
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
                ReturnSpecialDirectories = false,
            };

            foreach (var root in _roots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", options))
                    {
                        results.Add(new IndexedPath(
                            path,
                            Path.GetFileName(path),
                            Directory.Exists(path)));
                        if (results.Count % 256 == 0) PublishSnapshot(results);
                        if (results.Count >= MaximumIndexedEntries)
                        {
                            PublishSnapshot(results);
                            return results;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A single unavailable known folder must not fail the provider.
                }
            }

            PublishSnapshot(results);
            return results;
        }

        private void PublishSnapshot(List<IndexedPath> results)
            => Volatile.Write(ref _snapshot, results.ToArray());

        private static int Score(IndexedPath item, string query)
        {
            if (item.Name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 980;
            if (item.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 900;
            if (item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 760;
            return item.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ? 520 : 0;
        }

        private SearchResultItem CreateResult(IndexedPath item, int score)
            => new()
            {
                Id = "path:" + StableId(item.Path),
                ProviderId = Id,
                Title = item.Name,
                Subtitle = item.Path,
                Source = item.IsDirectory ? "本地文件夹" : "本地文件",
                Score = score,
                Kind = SearchResultKind.Data,
                PrimaryAction = new SearchResultAction(
                    SearchActionKind.OpenPath, item.Path, Label: "打开"),
                SecondaryActions = new[]
                {
                    new SearchResultAction(
                        SearchActionKind.OpenContainingFolder,
                        item.Path,
                        Label: "打开所在文件夹"),
                    new SearchResultAction(
                        SearchActionKind.CopyText,
                        item.Path,
                        Label: "复制路径"),
                },
                CanPin = true,
            };

        private static string StableId(string path)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()));
            return Convert.ToHexString(hash[..12]);
        }

        private static IEnumerable<string> GetDefaultRoots()
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrWhiteSpace(oneDrive)) yield return oneDrive;
        }

        private sealed record IndexedPath(string Path, string Name, bool IsDirectory);
    }
}
