using ToolGood.Words.Pinyin;

namespace LongBetterWindows.Host.Interaction
{
    internal enum SearchTextMatchKind
    {
        None,
        DirectExact,
        DirectPrefix,
        DirectContains,
        PinyinExact,
        PinyinPrefix,
        PinyinContains,
        InitialsExact,
        InitialsPrefix,
        InitialsContains,
        Fuzzy,
    }

    internal readonly record struct SearchTextForms(
        string Normalized,
        string Pinyin,
        string Initials);

    internal readonly record struct SearchTextMatch(
        SearchTextMatchKind Kind,
        int Score)
    {
        public bool IsMatch => Kind != SearchTextMatchKind.None;
    }

    internal static class SearchTextMatcher
    {
        public static SearchTextForms CreateForms(string? value)
        {
            var normalized = Normalize(value);
            if (normalized.Length == 0)
                return new SearchTextForms(string.Empty, string.Empty, string.Empty);

            return new SearchTextForms(
                normalized,
                Normalize(WordsHelper.GetPinyin(value!)),
                Normalize(WordsHelper.GetFirstPinyin(value!)));
        }

        public static SearchTextMatch Match(
            string? query,
            SearchTextForms candidate,
            bool allowFuzzy = true)
        {
            var normalizedQuery = Normalize(query);
            if (normalizedQuery.Length == 0 || candidate.Normalized.Length == 0)
                return default;

            if (candidate.Normalized == normalizedQuery)
                return new(SearchTextMatchKind.DirectExact, 1000);
            if (candidate.Normalized.StartsWith(normalizedQuery, StringComparison.Ordinal))
                return new(SearchTextMatchKind.DirectPrefix, 820);
            if (candidate.Normalized.Contains(normalizedQuery, StringComparison.Ordinal))
                return new(SearchTextMatchKind.DirectContains, 620);

            if (candidate.Pinyin == normalizedQuery)
                return new(SearchTextMatchKind.PinyinExact, 760);
            if (candidate.Initials == normalizedQuery)
                return new(SearchTextMatchKind.InitialsExact, 740);
            if (candidate.Pinyin.StartsWith(normalizedQuery, StringComparison.Ordinal))
                return new(SearchTextMatchKind.PinyinPrefix, 700);
            if (candidate.Initials.StartsWith(normalizedQuery, StringComparison.Ordinal))
                return new(SearchTextMatchKind.InitialsPrefix, 680);
            if (candidate.Pinyin.Contains(normalizedQuery, StringComparison.Ordinal))
                return new(SearchTextMatchKind.PinyinContains, 540);
            if (candidate.Initials.Contains(normalizedQuery, StringComparison.Ordinal))
                return new(SearchTextMatchKind.InitialsContains, 520);

            if (allowFuzzy
                && normalizedQuery.Length >= 4
                && (IsWithinSingleEdit(normalizedQuery, candidate.Normalized)
                    || IsWithinSingleEdit(normalizedQuery, candidate.Pinyin)))
            {
                return new(SearchTextMatchKind.Fuzzy, 480);
            }

            return default;
        }

        public static SearchTextMatch BestMatch(
            string? query,
            IEnumerable<SearchTextForms> candidates,
            bool allowFuzzy = true)
        {
            var best = default(SearchTextMatch);
            foreach (var candidate in candidates)
            {
                var match = Match(query, candidate, allowFuzzy);
                if (match.Score > best.Score)
                    best = match;
            }

            return best;
        }

        public static string Normalize(string? value)
            => (value ?? string.Empty).Trim().ToLowerInvariant();

        private static bool IsWithinSingleEdit(string query, string candidate)
        {
            if (Math.Abs(query.Length - candidate.Length) > 1)
                return false;

            var queryIndex = 0;
            var candidateIndex = 0;
            var edits = 0;
            while (queryIndex < query.Length && candidateIndex < candidate.Length)
            {
                if (query[queryIndex] == candidate[candidateIndex])
                {
                    queryIndex++;
                    candidateIndex++;
                    continue;
                }

                if (++edits > 1)
                    return false;

                if (query.Length > candidate.Length)
                    queryIndex++;
                else if (candidate.Length > query.Length)
                    candidateIndex++;
                else
                {
                    queryIndex++;
                    candidateIndex++;
                }
            }

            if (queryIndex < query.Length || candidateIndex < candidate.Length)
                edits++;

            return edits <= 1;
        }
    }
}
