namespace LongBetterWindows.Host.Interaction
{
    public static class SuperPanelGroupIds
    {
        public const string Smart = "smart";
        public const string Pinned = "pinned";
        public const string Recent = "recent";
    }

    public static class SuperPanelResultOrganizer
    {
        public static IReadOnlyList<SearchResultItem> SelectGroup(
            IReadOnlyList<SearchResultItem> results,
            string groupId,
            IReadOnlyList<string> pinnedResultIds,
            IReadOnlyList<string> recentResultIds,
            IReadOnlyList<string>? customResultIds = null,
            int maxResults = 6)
        {
            if (maxResults <= 0)
                return Array.Empty<SearchResultItem>();

            if (string.Equals(groupId, SuperPanelGroupIds.Pinned,
                    StringComparison.OrdinalIgnoreCase))
                return OrderByIds(results, pinnedResultIds, maxResults);

            if (string.Equals(groupId, SuperPanelGroupIds.Recent,
                    StringComparison.OrdinalIgnoreCase))
                return OrderByIds(results, recentResultIds, maxResults);

            if (SuperPanelGroupService.IsCustomGroupId(groupId))
                return OrderByIds(
                    results,
                    customResultIds ?? Array.Empty<string>(),
                    maxResults);

            return results.Take(maxResults).ToList();
        }

        private static IReadOnlyList<SearchResultItem> OrderByIds(
            IReadOnlyList<SearchResultItem> results,
            IReadOnlyList<string> orderedIds,
            int maxResults)
        {
            var byId = results.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            return orderedIds
                .Where(byId.ContainsKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .Select(id => byId[id])
                .ToList();
        }
    }
}
