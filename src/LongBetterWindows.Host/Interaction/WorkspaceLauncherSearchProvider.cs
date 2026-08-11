using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    internal sealed class WorkspaceLauncherSearchProvider : ISearchProvider
    {
        private const string ResultPrefix = "workspace:";
        private readonly PluginRegistry _plugins;
        private readonly Func<string, string>? _localize;

        public WorkspaceLauncherSearchProvider(
            PluginRegistry plugins,
            Func<string, string>? localize = null)
        {
            _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
            _localize = localize;
        }

        public string Id => "workspace";
        public int Priority => 1100;

        public Task<IReadOnlyList<SearchResultItem>> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.MaxResults <= 0)
                return Task.FromResult<IReadOnlyList<SearchResultItem>>([]);

            var query = Normalize(request.Query);
            var preferred = (request.PinnedResultIds ?? [])
                .Concat(request.RecentResultIds ?? [])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var results = new List<SearchResultItem>();

            AddDestination(
                results,
                request,
                query,
                "management:root",
                Text("page.overview.title", "管理中心"),
                Text("page.overview.subtitle", "平台状态、关键能力和下一步操作"),
                Text("launcher.source.workspace", "工作区"),
                SearchResultIconKind.Management,
                aliases: ["管理", "管理中心", "overview", "management"]);
            AddDestination(
                results,
                request,
                query,
                "marketplace:catalog",
                Text("page.market.title", "插件市场"),
                Text("page.market.subtitle", "发现、审查并安装可信插件"),
                Text("launcher.source.marketplace", "市场"),
                SearchResultIconKind.Marketplace,
                aliases: ["市场", "插件市场", "market", "marketplace"]);
            AddDestination(
                results,
                request,
                query,
                "widgets:root",
                Text("page.widgets.title", "桌面组件"),
                Text("page.widgets.subtitle", "组合来自插件的常驻信息与快捷操作"),
                Text("launcher.source.workspace", "工作区"),
                SearchResultIconKind.Plugin,
                aliases: ["组件", "桌面组件", "widget", "widgets"]);
            AddDestination(
                results,
                request,
                query,
                "settings:root",
                Text("page.settings.title", "设置"),
                Text("page.settings.subtitle", "外观、语言、本地数据与隐私"),
                Text("launcher.source.workspace", "工作区"),
                SearchResultIconKind.Settings,
                aliases: ["设置", "偏好", "settings", "preferences"]);

            foreach (var plugin in _plugins.GetAll())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = $"plugin-settings:{plugin.Id}";
                var resultId = ResultPrefix + target;
                if (query.Length == 0 && !preferred.Contains(resultId))
                    continue;
                if (query.Length > 0
                    && !Matches(
                        query,
                        plugin.DisplayName,
                        plugin.Id,
                        Text("plugins.settings", "插件设置")))
                {
                    continue;
                }

                results.Add(new SearchResultItem
                {
                    Id = resultId,
                    ProviderId = Id,
                    Title = plugin.DisplayName,
                    Subtitle = string.Format(
                        Text("plugins.module.subtitle", "插件详情与设置 · v{0}"),
                        plugin.Manifest.Version),
                    Source = Text("launcher.source.installedPlugin", "已安装插件"),
                    IconKind = SearchResultIconKind.Plugin,
                    IconPath = InstalledPluginRailProjection.FindIconPath(
                        plugin.Directory),
                    Score = query.Length == 0 ? 260 : Score(
                        query,
                        plugin.DisplayName,
                        plugin.Id),
                    Kind = SearchResultKind.Data,
                    PrimaryAction = new SearchResultAction(
                        SearchActionKind.OpenWorkspaceModule,
                        target),
                    CanPin = true,
                });
            }

            return Task.FromResult<IReadOnlyList<SearchResultItem>>(
                results
                    .OrderByDescending(item => item.Score)
                    .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
                    .Take(request.MaxResults)
                    .ToArray());
        }

        private void AddDestination(
            ICollection<SearchResultItem> results,
            SearchRequest request,
            string query,
            string target,
            string title,
            string subtitle,
            string source,
            SearchResultIconKind iconKind,
            IReadOnlyList<string> aliases)
        {
            if (query.Length > 0 && !Matches(query, aliases.Prepend(title).ToArray()))
                return;

            results.Add(new SearchResultItem
            {
                Id = ResultPrefix + target,
                ProviderId = Id,
                Title = title,
                Subtitle = subtitle,
                Source = source,
                IconKind = iconKind,
                Score = query.Length == 0
                    ? target.StartsWith("management:", StringComparison.Ordinal) ? 520
                    : target.StartsWith("marketplace:", StringComparison.Ordinal) ? 440
                    : target.StartsWith("widgets:", StringComparison.Ordinal) ? 400
                    : 360
                    : Score(query, aliases.Prepend(title).ToArray()),
                Kind = SearchResultKind.Data,
                PrimaryAction = new SearchResultAction(
                    SearchActionKind.OpenWorkspaceModule,
                    target),
                CanPin = true,
            });
        }

        private static bool Matches(string query, params string[] candidates)
            => SearchTextMatcher.BestMatch(
                query,
                candidates.Select(SearchTextMatcher.CreateForms)).IsMatch;

        private static int Score(string query, params string[] candidates)
            => SearchTextMatcher.BestMatch(
                query,
                candidates.Select(SearchTextMatcher.CreateForms)).Score;

        private static string Normalize(string? value)
            => (value ?? string.Empty).Trim().ToLowerInvariant();

        private string Text(string key, string fallback)
        {
            var value = _localize?.Invoke(key);
            return string.IsNullOrWhiteSpace(value) || value == key
                ? fallback
                : value;
        }
    }
}
