using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Host.Services
{
    public sealed class PinyinService : IPinyinService
    {
        public Task<HostApiResponse<string>> GetPinyinAsync(string text)
            => Execute(() => SearchTextMatcher.CreateForms(text).Pinyin);

        public Task<HostApiResponse<string>> GetPinyinInitialsAsync(string text)
            => Execute(() => SearchTextMatcher.CreateForms(text).Initials);

        public Task<HostApiResponse<bool>> MatchAsync(string text, string query)
            => Execute(() => SearchTextMatcher.Match(
                query,
                SearchTextMatcher.CreateForms(text)).IsMatch);

        public Task<HostApiResponse<List<PinyinMatchResult>>> FilterAsync(
            List<string> items,
            string query)
            => Execute(() => items
                .Select(item =>
                {
                    var forms = SearchTextMatcher.CreateForms(item);
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
