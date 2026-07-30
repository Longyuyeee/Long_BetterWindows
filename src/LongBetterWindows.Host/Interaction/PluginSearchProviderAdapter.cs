using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using Serilog;

namespace LongBetterWindows.Host.Interaction
{
    internal sealed class PluginSearchProviderAdapter : ISearchProvider
    {
        private readonly string _pluginId;
        private readonly IPluginSearchProvider _inner;
        private readonly string? _iconPath;

        public PluginSearchProviderAdapter(
            string pluginId,
            IPluginSearchProvider inner,
            string? iconPath = null)
        {
            _pluginId = pluginId;
            _inner = inner;
            _iconPath = iconPath;
        }

        public string Id => "plugin:" + _pluginId;
        public int Priority => Math.Clamp(_inner.Priority, -100, 500);

        public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PluginSearchResult> results;
            using (PluginAccessContext.Enter(_pluginId))
            {
                var ownPrefix = _pluginId + ":";
                results = await _inner.SearchAsync(
                    new PluginSearchRequest(
                        request.Query,
                        request.MaxResults,
                        SelectOwnedIds(request.PinnedResultIds, ownPrefix),
                        SelectOwnedIds(request.RecentResultIds, ownPrefix),
                        SelectOwnedIds(
                            request.AdditionalPreferredResultIds,
                            ownPrefix)),
                    cancellationToken);
            }

            var commandPrefix = _pluginId + ":";
            var accepted = new List<SearchResultItem>();
            foreach (var item in results)
            {
                var actions = new[] { item.PrimaryAction }
                    .Concat(item.SecondaryActions);
                if (actions.Any(action => !IsSafePluginAction(action, commandPrefix)))
                {
                    Log.Debug("插件搜索结果动作被拒绝: {PluginId}", _pluginId);
                    continue;
                }

                accepted.Add(new SearchResultItem
                {
                    Id = _pluginId + ":" + item.Id,
                    ProviderId = Id,
                    Title = item.Title,
                    Subtitle = item.Subtitle,
                    Source = item.Source,
                    IconKind = SearchResultIconKind.Plugin,
                    IconPath = _iconPath,
                    Score = item.Score,
                    Kind = MapResultKind(item.Kind),
                    PrimaryAction = MapAction(item.PrimaryAction),
                    SecondaryActions = item.SecondaryActions
                        .Select(MapAction)
                        .ToList(),
                    CanPin = item.CanPin,
                    ContinuationToken = item.ContinuationToken,
                });
            }

            return accepted;
        }

        private static IReadOnlyList<string>? SelectOwnedIds(
            IReadOnlyList<string>? ids,
            string ownPrefix)
            => ids?
                .Where(id => id.StartsWith(
                    ownPrefix,
                    StringComparison.OrdinalIgnoreCase))
                .Select(id => id[ownPrefix.Length..])
                .ToList();

        private static SearchResultKind MapResultKind(
            PluginSearchResultKind kind)
            => kind switch
            {
                PluginSearchResultKind.Data => SearchResultKind.Data,
                PluginSearchResultKind.Continuation =>
                    SearchResultKind.Continuation,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };

        private static SearchResultAction MapAction(PluginSearchAction action)
            => new(
                action.Kind switch
                {
                    PluginSearchActionKind.ExecuteCommand =>
                        SearchActionKind.ExecuteCommand,
                    PluginSearchActionKind.ContinueSearch =>
                        SearchActionKind.ContinueSearch,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(action.Kind)),
                },
                action.Target,
                action.Invocation,
                action.Label);

        private static bool IsSafePluginAction(
            PluginSearchAction action,
            string commandPrefix)
            => action.Kind switch
            {
                PluginSearchActionKind.ExecuteCommand =>
                    action.Target.StartsWith(
                    commandPrefix, StringComparison.OrdinalIgnoreCase),
                PluginSearchActionKind.ContinueSearch =>
                    action.Target.Length <= 512,
                _ => false,
            };
    }
}
