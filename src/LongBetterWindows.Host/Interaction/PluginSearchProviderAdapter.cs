using LongBetterWindows.Host.Engine;
using Serilog;

namespace LongBetterWindows.Host.Interaction
{
    internal sealed class PluginSearchProviderAdapter : ISearchProvider
    {
        private readonly string _pluginId;
        private readonly ISearchProvider _inner;
        private readonly string? _iconPath;

        public PluginSearchProviderAdapter(
            string pluginId,
            ISearchProvider inner,
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
            IReadOnlyList<SearchResultItem> results;
            using (PluginAccessContext.Enter(_pluginId))
            {
                var ownPrefix = _pluginId + ":";
                results = await _inner.SearchAsync(
                    request with
                    {
                        Context = request.Context.MetadataOnly(),
                        PinnedResultIds = request.PinnedResultIds?
                            .Where(id => id.StartsWith(ownPrefix, StringComparison.OrdinalIgnoreCase))
                            .ToList(),
                        RecentResultIds = request.RecentResultIds?
                            .Where(id => id.StartsWith(ownPrefix, StringComparison.OrdinalIgnoreCase))
                            .ToList(),
                    },
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

                accepted.Add(item with
                {
                    Id = _pluginId + ":" + item.Id,
                    ProviderId = Id,
                    IconKind = SearchResultIconKind.Plugin,
                    IconPath = _iconPath,
                });
            }

            return accepted;
        }

        private static bool IsSafePluginAction(
            SearchResultAction action,
            string commandPrefix)
            => action.Kind switch
            {
                SearchActionKind.ExecuteCommand => action.Target.StartsWith(
                    commandPrefix, StringComparison.OrdinalIgnoreCase),
                SearchActionKind.ContinueSearch => action.Target.Length <= 512,
                _ => false,
            };
    }
}
