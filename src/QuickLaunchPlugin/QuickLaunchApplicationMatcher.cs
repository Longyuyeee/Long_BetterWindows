using LongBetterWindows.Host.Capabilities;

namespace QuickLaunchPlugin;

internal sealed class QuickLaunchApplicationMatcher
{
    private readonly IPinyinService? _pinyin;

    public QuickLaunchApplicationMatcher(IPinyinService? pinyin)
        => _pinyin = pinyin;

    public Task<IReadOnlyDictionary<string, int>> ScoreAsync(
        IEnumerable<string> candidateNames,
        string query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var names = candidateNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.Run(
            () => ScoreCoreAsync(names, query, cancellationToken),
            cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, int>> ScoreCoreAsync(
        List<string> names,
        string query,
        CancellationToken cancellationToken)
    {
        if (_pinyin is not null)
        {
            try
            {
                var response = await _pinyin
                    .FilterAsync(names, query)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (response.IsSuccess && response.Data is not null)
                {
                    return response.Data
                        .GroupBy(result => result.Text, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            group => group.Key,
                            group => group.Max(result => result.Score),
                            StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Direct matching remains available if the optional capability fails.
            }
        }

        return names
            .Select(name => (name, score: DirectScore(name, query)))
            .Where(match => match.score > 0)
            .ToDictionary(
                match => match.name,
                match => match.score,
                StringComparer.OrdinalIgnoreCase);
    }

    private static int DirectScore(string candidate, string query)
    {
        if (candidate.Equals(query, StringComparison.OrdinalIgnoreCase)) return 1000;
        if (candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 820;
        return candidate.Contains(query, StringComparison.OrdinalIgnoreCase) ? 620 : 0;
    }
}
