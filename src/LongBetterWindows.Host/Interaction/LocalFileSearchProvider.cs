using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LongBetterWindows.Host.Interaction
{
    public sealed class LocalFileSearchProvider : ISearchProvider
    {
        private const int MaximumIndexedEntries = 30000;
        private const int SnapshotBatchSize = 2048;
        private readonly IReadOnlyList<string> _roots;
        private readonly object _indexLock = new();
        private readonly Func<string, string>? _localize;
        private Task<IReadOnlyList<IndexedPath>>? _indexTask;
        private IReadOnlyList<IndexedPath> _snapshot = Array.Empty<IndexedPath>();

        public LocalFileSearchProvider(
            IEnumerable<string>? roots = null,
            Func<string, string>? localize = null)
        {
            _roots = (roots ?? GetDefaultRoots())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _localize = localize;
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
            return FindBestMatches(
                    index,
                    query,
                    Math.Min(8, request.MaxResults),
                    cancellationToken)
                .OrderByDescending(match => match.score)
                .ThenBy(match => match.item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(match => CreateResult(match.item, match.score))
                .ToList();
        }

        private Task<IReadOnlyList<IndexedPath>> GetIndexAsync()
        {
            lock (_indexLock)
                return _indexTask ??= Task.Factory.StartNew(
                    () =>
                    {
                        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                        return BuildIndex();
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
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
                        if (results.Count % SnapshotBatchSize == 0)
                            PublishSnapshot(results);
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

        private static IReadOnlyList<(IndexedPath item, int score)> FindBestMatches(
            IReadOnlyList<IndexedPath> index,
            string query,
            int maximumResults,
            CancellationToken cancellationToken)
        {
            if (maximumResults <= 0)
                return Array.Empty<(IndexedPath item, int score)>();

            var matches = new List<(IndexedPath item, int score)>(maximumResults + 1);
            for (var indexPosition = 0; indexPosition < index.Count; indexPosition++)
            {
                if ((indexPosition & 255) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                var item = index[indexPosition];
                var score = Score(item, query);
                if (score <= 0)
                    continue;

                matches.Add((item, score));
                if (matches.Count <= maximumResults)
                    continue;

                var worst = 0;
                for (var candidate = 1; candidate < matches.Count; candidate++)
                {
                    if (matches[candidate].score < matches[worst].score
                        || matches[candidate].score == matches[worst].score
                        && string.Compare(
                            matches[candidate].item.Name,
                            matches[worst].item.Name,
                            StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        worst = candidate;
                    }
                }
                matches.RemoveAt(worst);
            }

            return matches;
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
                Source = item.IsDirectory
                    ? Text("search.local.directory", "本地文件夹")
                    : Text("search.local.file", "本地文件"),
                Score = score,
                Kind = SearchResultKind.Data,
                PrimaryAction = new SearchResultAction(
                    SearchActionKind.OpenPath,
                    item.Path,
                    Label: Text("search.action.open", "打开")),
                SecondaryActions = new[]
                {
                    new SearchResultAction(
                        SearchActionKind.OpenContainingFolder,
                        item.Path,
                        Label: Text(
                            "search.action.openContainingFolder",
                            "打开所在文件夹")),
                    new SearchResultAction(
                        SearchActionKind.CopyText,
                        item.Path,
                        Label: Text("search.action.copyPath", "复制路径")),
                },
                CanPin = true,
            };

        private string Text(string key, string fallback)
        {
            var value = _localize?.Invoke(key);
            return string.IsNullOrWhiteSpace(value) || value == key
                ? fallback
                : value;
        }

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
