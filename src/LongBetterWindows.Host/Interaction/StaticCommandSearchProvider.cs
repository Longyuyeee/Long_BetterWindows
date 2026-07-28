namespace LongBetterWindows.Host.Interaction
{
    public sealed class StaticCommandSearchProvider : ISearchProvider
    {
        private readonly CommandRegistry _commands;
        private readonly Func<string, string?>? _pluginIconResolver;
        private readonly Dictionary<string, string?> _pluginIcons =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly object _iconLock = new();

        public StaticCommandSearchProvider(
            CommandRegistry commands,
            Func<string, string?>? pluginIconResolver = null)
        {
            _commands = commands;
            _pluginIconResolver = pluginIconResolver;
        }

        public string Id => "commands";
        public int Priority => 1000;

        public Task<IReadOnlyList<SearchResultItem>> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputs = string.IsNullOrWhiteSpace(request.Query)
                ? request.Context.AvailableInputTypes
                : Array.Empty<Contracts.AcceptedInputType>();
            var results = _commands.SearchWithInputs(
                    request.Query,
                    inputs,
                    request.MaxResults)
                .Select(result => CreateResult(result.Descriptor, result.Score))
                .ToList();

            if (string.IsNullOrWhiteSpace(request.Query))
            {
                var preferredIds = (request.PinnedResultIds ?? Array.Empty<string>())
                    .Concat(request.RecentResultIds ?? Array.Empty<string>())
                    .Concat(request.AdditionalPreferredResultIds ?? Array.Empty<string>());
                foreach (var resultId in preferredIds)
                {
                    const string prefix = "command:";
                    if (!resultId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var descriptor = _commands.Get(resultId[prefix.Length..]);
                    if (descriptor is null || results.Any(item =>
                            string.Equals(item.Id, resultId, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    results.Add(CreateResult(descriptor, 80));
                }
            }

            return Task.FromResult<IReadOnlyList<SearchResultItem>>(results);
        }

        private SearchResultItem CreateResult(CommandDescriptor descriptor, int score)
            => new()
            {
                Id = "command:" + descriptor.Key,
                ProviderId = Id,
                Title = descriptor.Title,
                Subtitle = descriptor.Description,
                Source = descriptor.PluginName,
                IconKind = SearchResultIconKind.Plugin,
                IconPath = ResolvePluginIcon(descriptor.PluginId),
                Score = score,
                Kind = SearchResultKind.Command,
                PrimaryAction = new SearchResultAction(
                    SearchActionKind.ExecuteCommand,
                    descriptor.Key),
                CanPin = true,
            };

        private string? ResolvePluginIcon(string pluginId)
        {
            if (_pluginIconResolver is null)
                return null;
            lock (_iconLock)
            {
                if (_pluginIcons.TryGetValue(pluginId, out var cached))
                    return cached;
                var resolved = _pluginIconResolver(pluginId);
                _pluginIcons[pluginId] = resolved;
                return resolved;
            }
        }
    }
}
