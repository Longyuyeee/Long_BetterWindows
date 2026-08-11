using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;
using System.Collections.Concurrent;

namespace LongBetterWindows.Host.Services
{
    public sealed class PinyinService : IPinyinService
    {
        private const int MaximumCachedEntries = 4096;
        private const int MaximumCachedTextLength = 256;
        private readonly ConcurrentDictionary<string, SearchTextForms> _formsCache =
            new(StringComparer.Ordinal);
        private readonly object _cacheTrimLock = new();

        public Task<HostApiResponse<string>> GetPinyinAsync(string text)
            => Execute(() => GetForms(text).Pinyin);

        public Task<HostApiResponse<string>> GetPinyinInitialsAsync(string text)
            => Execute(() => GetForms(text).Initials);

        public Task<HostApiResponse<bool>> MatchAsync(string text, string query)
            => Execute(() => SearchTextMatcher.Match(
                query,
                GetForms(text)).IsMatch);

        public Task<HostApiResponse<List<PinyinMatchResult>>> FilterAsync(
            List<string> items,
            string query)
            => Execute(() => items
                .Select(item =>
                {
                    var forms = GetForms(item);
                    var match = SearchTextMatcher.Match(query, forms);
                    return new PinyinMatchResult
                    {
                        Text = item,
                        Pinyin = forms.Pinyin,
                        Initials = forms.Initials,
                        IsMatch = match.IsMatch,
                        Score = match.Score,
                    };
                })
                .Where(result => result.IsMatch)
                .OrderByDescending(result => result.Score)
                .ThenBy(result => result.Text, StringComparer.CurrentCultureIgnoreCase)
                .ToList());

        private SearchTextForms GetForms(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return SearchTextMatcher.CreateForms(text);
            if (text.Length > MaximumCachedTextLength)
                return SearchTextMatcher.CreateForms(text);
            if (_formsCache.TryGetValue(text, out var cached))
                return cached;

            if (_formsCache.Count >= MaximumCachedEntries)
            {
                lock (_cacheTrimLock)
                {
                    if (_formsCache.Count >= MaximumCachedEntries)
                        _formsCache.Clear();
                }
            }

            return _formsCache.GetOrAdd(text, SearchTextMatcher.CreateForms);
        }

        private static Task<HostApiResponse<T>> Execute<T>(Func<T> action)
        {
            try
            {
                return Task.FromResult(HostApiResponse<T>.Success(action()));
            }
            catch (Exception ex)
            {
                return Task.FromResult(HostApiResponse<T>.Failure(
                    ApiErrorCode.Unknown,
                    ex.Message));
            }
        }
    }
}
