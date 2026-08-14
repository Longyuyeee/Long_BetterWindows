namespace LongBetterWindows.Host.Interaction
{
    internal sealed record LauncherResultViewItem(
        SearchResultItem Result,
        string SectionKey,
        string SectionTitle,
        int SectionOrder)
    {
        public string Id => Result.Id;
        public string Title => Result.Title;
        public string Subtitle => Result.Subtitle;
        public string Source => Result.Source;
        public SearchResultIconKind IconKind => Result.IconKind;
        public string? IconPath => Result.IconPath;
        public bool HasIconPath => !string.IsNullOrWhiteSpace(IconPath);
        public string IconLabel => Result.IconLabel;
        public bool HasIconLabel => Result.HasIconLabel;
        public bool HasSecondaryActions => Result.HasSecondaryActions;
        public bool CanPin => Result.CanPin;
        public bool IsPinned => Result.IsPinned;
    }

    internal static class LauncherResultProjection
    {
        public static IReadOnlyList<LauncherResultViewItem> Build(
            IReadOnlyList<SearchResultItem> results,
            string? query,
            ContextSnapshot context,
            IReadOnlyCollection<string> recentResultIds,
            Func<string, string>? localize = null)
        {
            ArgumentNullException.ThrowIfNull(results);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(recentResultIds);
            var recent = recentResultIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hasQuery = !string.IsNullOrWhiteSpace(query);

            return results
                .Select((result, index) =>
                {
                    var section = ResolveSection(
                        result,
                        hasQuery,
                        context.Items.Count > 0,
                        recent.Contains(result.Id));
                    return new
                    {
                        Item = new LauncherResultViewItem(
                            result,
                            section.Key,
                            Text(localize, section.ResourceKey, section.Fallback),
                            section.Order),
                        OriginalIndex = index,
                    };
                })
                .OrderBy(item => item.Item.SectionOrder)
                .ThenBy(item => item.OriginalIndex)
                .Select(item => item.Item)
                .ToArray();
        }

        private static Section ResolveSection(
            SearchResultItem result,
            bool hasQuery,
            bool hasContext,
            bool isRecent)
        {
            if (hasQuery)
                return new("results", "launcher.section.results", "搜索结果", 0);
            if (result.PrimaryAction is
                {
                    Kind: SearchActionKind.OpenWorkspaceModule,
                    Target: "management:root",
                })
            {
                return new(
                    "management",
                    "launcher.section.management",
                    "管理中心",
                    0);
            }
            if (result.IsPinned)
                return new("pinned", "launcher.section.pinned", "固定", 1);
            if (isRecent)
                return new("recent", "launcher.section.recent", "最近使用", 2);
            if (hasContext && result.Kind == SearchResultKind.Command)
            {
                return new(
                    "context",
                    "launcher.section.context",
                    "适用于当前内容",
                    3);
            }
            if (result.PrimaryAction is
                {
                    Kind: SearchActionKind.OpenWorkspaceModule,
                    Target: "marketplace:catalog",
                })
            {
                return new(
                    "marketplace",
                    "launcher.section.marketplace",
                    "插件市场",
                    4);
            }
            return new(
                "suggested",
                "launcher.section.suggested",
                "常用操作",
                5);
        }

        private static string Text(
            Func<string, string>? localize,
            string key,
            string fallback)
        {
            var value = localize?.Invoke(key);
            return string.IsNullOrWhiteSpace(value) || value == key
                ? fallback
                : value;
        }

        private readonly record struct Section(
            string Key,
            string ResourceKey,
            string Fallback,
            int Order);
    }
}
