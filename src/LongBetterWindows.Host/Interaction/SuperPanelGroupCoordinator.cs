namespace LongBetterWindows.Host.Interaction
{
    internal sealed class SuperPanelGroupCoordinator
    {
        private readonly SearchPreferenceService _preferences;
        private readonly SuperPanelGroupService _groups;
        private readonly Func<string, string>? _localize;
        private IReadOnlyList<SearchResultItem> _results = Array.Empty<SearchResultItem>();
        private bool _searchCompleted;
        private bool _hasContext;
        private int _pageIndex;

        public SuperPanelGroupCoordinator(
            SearchPreferenceService preferences,
            SuperPanelGroupService groups,
            Func<string, string>? localize = null)
        {
            _preferences = preferences;
            _groups = groups;
            _localize = localize;
        }

        public string ActiveGroupId { get; private set; } = SuperPanelGroupIds.Smart;

        public SuperPanelCustomGroup? ActiveCustomGroup => _groups.GetGroups()
            .FirstOrDefault(group => string.Equals(
                group.Id, ActiveGroupId, StringComparison.OrdinalIgnoreCase));

        public void ResetResults()
        {
            _results = Array.Empty<SearchResultItem>();
            _searchCompleted = false;
            _pageIndex = 0;
        }

        public void SetContext(bool hasContext)
        {
            if (_hasContext == hasContext) return;
            _hasContext = hasContext;
            _pageIndex = 0;
        }

        public void SetResults(IReadOnlyList<SearchResultItem> results, bool completed)
        {
            _results = results;
            _searchCompleted = completed;
        }

        public bool SelectGroup(string groupId)
        {
            if (!GetGroupDefinitions().Any(group => string.Equals(
                    group.Id, groupId, StringComparison.OrdinalIgnoreCase)))
                return false;

            ActiveGroupId = groupId;
            _pageIndex = 0;
            return true;
        }

        public bool Cycle(int wheelDelta)
        {
            var groups = GetGroupDefinitions();
            if (groups.Count == 0) return false;
            var currentIndex = groups.FindIndex(group => string.Equals(
                group.Id, ActiveGroupId, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0) currentIndex = 0;
            var offset = wheelDelta < 0 ? 1 : -1;
            var nextIndex = (currentIndex + offset + groups.Count) % groups.Count;
            ActiveGroupId = groups[nextIndex].Id;
            _pageIndex = 0;
            return true;
        }

        public bool MovePage(int offset)
        {
            if (offset == 0) return false;
            var pageCount = GetPageCount();
            var next = Math.Clamp(_pageIndex + Math.Sign(offset), 0, pageCount - 1);
            if (next == _pageIndex) return false;
            _pageIndex = next;
            return true;
        }

        public Task TogglePinnedAsync(string resultId) =>
            _preferences.TogglePinnedAsync(resultId);

        public async Task<SuperPanelGroupMutationResult> ReorderActiveResultAsync(
            string resultId,
            int targetIndex)
        {
            if (targetIndex < 0)
                return SuperPanelGroupMutationResult.Failed;

            var moved = ActiveGroupId == SuperPanelGroupIds.Pinned
                ? await _preferences.MovePinnedAsync(resultId, targetIndex)
                : SuperPanelGroupService.IsCustomGroupId(ActiveGroupId)
                  && await _groups.MoveResultAsync(ActiveGroupId, resultId, targetIndex);
            return moved
                ? new SuperPanelGroupMutationResult(
                    true,
                    ActiveGroupId == SuperPanelGroupIds.Pinned
                        ? Text("superPanel.status.pinnedOrderSaved", "固定顺序已保存")
                        : Text("superPanel.status.groupOrderSaved", "分组顺序已保存"))
                : SuperPanelGroupMutationResult.Failed;
        }

        public async Task<SuperPanelGroupMutationResult> MoveResultToGroupAsync(
            string? sourceGroupId,
            string targetGroupId,
            string resultId)
        {
            if (!SuperPanelGroupService.IsCustomGroupId(targetGroupId)
                || !await _groups.AddResultAsync(targetGroupId, resultId))
                return SuperPanelGroupMutationResult.Failed;

            if (SuperPanelGroupService.IsCustomGroupId(sourceGroupId)
                && !string.Equals(sourceGroupId, targetGroupId, StringComparison.OrdinalIgnoreCase))
                await _groups.RemoveResultAsync(sourceGroupId!, resultId);

            SelectGroup(targetGroupId);
            return new SuperPanelGroupMutationResult(
                true,
                Text("superPanel.status.movedToGroup", "已移动到分组"));
        }

        public async Task<SuperPanelGroupMutationResult> SaveGroupAsync(
            string? groupId,
            string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return new SuperPanelGroupMutationResult(
                    false,
                    Text("superPanel.error.groupNameRequired", "请输入分组名称"));

            if (groupId is null)
            {
                var group = await _groups.CreateAsync(title);
                if (group is null)
                    return new SuperPanelGroupMutationResult(
                        false,
                        Text("superPanel.error.groupLimit", "最多创建 8 个分组"));
                SelectGroup(group.Id);
            }
            else if (!await _groups.RenameAsync(groupId, title))
            {
                return new SuperPanelGroupMutationResult(
                    false,
                    Text("superPanel.error.renameFailed", "分组重命名失败"));
            }

            return new SuperPanelGroupMutationResult(
                true,
                Text("superPanel.status.groupSaved", "分组已保存"));
        }

        public async Task<SuperPanelGroupMutationResult> DeleteActiveGroupAsync()
        {
            var group = ActiveCustomGroup;
            if (group is null || !await _groups.DeleteAsync(group.Id))
                return SuperPanelGroupMutationResult.Failed;

            SelectGroup(SuperPanelGroupIds.Pinned);
            return new SuperPanelGroupMutationResult(
                true,
                Text("superPanel.status.groupDeleted", "分组已删除"));
        }

        public async Task<SuperPanelGroupMutationResult> RemoveFromActiveGroupAsync(
            string resultId)
        {
            if (!SuperPanelGroupService.IsCustomGroupId(ActiveGroupId)
                || !await _groups.RemoveResultAsync(ActiveGroupId, resultId))
                return SuperPanelGroupMutationResult.Failed;

            return new SuperPanelGroupMutationResult(
                true,
                Text("superPanel.status.removedFromGroup", "已移出分组"));
        }

        public SuperPanelViewState BuildView()
        {
            var pinnedIds = _preferences.GetPinnedResultIds();
            var recentIds = _preferences.GetRecentResultIds(24);
            var customGroups = _groups.GetGroups();
            var customGroup = customGroups.FirstOrDefault(group => string.Equals(
                group.Id, ActiveGroupId, StringComparison.OrdinalIgnoreCase));
            var matching = SuperPanelResultOrganizer.SelectGroup(
                _results,
                ActiveGroupId,
                pinnedIds,
                recentIds,
                customGroup?.ResultIds,
                maxResults: 24);
            var mode = ResolvePresentationMode();
            var pageSize = GetPageSize(mode);
            var pageCount = Math.Max(1, (int)Math.Ceiling(
                matching.Count / (double)pageSize));
            _pageIndex = Math.Clamp(_pageIndex, 0, pageCount - 1);
            var visible = matching
                .Skip(_pageIndex * pageSize)
                .Take(pageSize)
                .ToList();
            var resultIds = _results.Select(result => result.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var groupViews = GetGroupDefinitions(customGroups)
                .Select(group => new SuperPanelGroupView(
                    group.Id,
                    group.Title,
                    group.Hint,
                    string.Equals(
                        group.Id, ActiveGroupId, StringComparison.OrdinalIgnoreCase),
                    GetVisibleCount(
                        group.Id, pinnedIds, recentIds, customGroups, resultIds),
                    SuperPanelGroupService.IsCustomGroupId(group.Id)))
                .ToList();

            return new SuperPanelViewState(
                visible,
                groupViews,
                _searchCompleted && visible.Count == 0,
                ActiveGroupId switch
                {
                    SuperPanelGroupIds.Pinned => Text(
                        "superPanel.empty.pinned",
                        "还没有固定操作"),
                    SuperPanelGroupIds.Recent => Text(
                        "superPanel.empty.recent",
                        "还没有最近使用记录"),
                    _ when customGroup is not null => Text(
                        "superPanel.empty.custom",
                        "把固定操作拖到这个分组"),
                    _ => Text(
                        "superPanel.empty.context",
                        "当前上下文没有可用操作"),
                },
                visible.Count > 0
                    ? string.Format(
                        Text("superPanel.status.actionCount", "{0} 个操作"),
                        visible.Count)
                    : _searchCompleted
                        ? Text("superPanel.status.groupEmpty", "当前分组为空")
                        : Text("superPanel.status.matching", "正在匹配..."),
                ActiveGroupId switch
                {
                    SuperPanelGroupIds.Pinned => Text(
                        "superPanel.hint.pinned",
                        "拖拽排序或拖到文件夹 · 单击执行 · 滚轮切组"),
                    _ when customGroup is not null => Text(
                        "superPanel.hint.custom",
                        "拖拽排序 · Delete 移出分组 · 滚轮切组"),
                    _ => Text(
                        "superPanel.hint.default",
                        "单击执行 · 滚轮切组 · Esc 返回原窗口"),
                },
                customGroup is not null,
                mode,
                new SuperPanelPageView(
                    _pageIndex,
                    pageCount,
                    _pageIndex > 0,
                    _pageIndex + 1 < pageCount,
                    $"{_pageIndex + 1}/{pageCount}"));
        }

        private SuperPanelPresentationMode ResolvePresentationMode()
            => _hasContext
                && string.Equals(
                    ActiveGroupId,
                    SuperPanelGroupIds.Smart,
                    StringComparison.OrdinalIgnoreCase)
                ? SuperPanelPresentationMode.ContextList
                : SuperPanelPresentationMode.CompactGrid;

        private int GetPageCount()
        {
            var pinnedIds = _preferences.GetPinnedResultIds();
            var recentIds = _preferences.GetRecentResultIds(24);
            var custom = ActiveCustomGroup;
            var count = SuperPanelResultOrganizer.SelectGroup(
                _results,
                ActiveGroupId,
                pinnedIds,
                recentIds,
                custom?.ResultIds,
                maxResults: 24).Count;
            return Math.Max(1, (int)Math.Ceiling(
                count / (double)GetPageSize(ResolvePresentationMode())));
        }

        private static int GetPageSize(SuperPanelPresentationMode mode)
            => mode == SuperPanelPresentationMode.CompactGrid ? 12 : 6;

        private int GetVisibleCount(
            string groupId,
            IReadOnlyList<string> pinnedIds,
            IReadOnlyList<string> recentIds,
            IReadOnlyList<SuperPanelCustomGroup> customGroups,
            IReadOnlySet<string> resultIds)
            => groupId switch
            {
                SuperPanelGroupIds.Pinned => pinnedIds.Count(resultIds.Contains),
                SuperPanelGroupIds.Recent => recentIds.Count(resultIds.Contains),
                _ when SuperPanelGroupService.IsCustomGroupId(groupId) => customGroups
                    .First(custom => string.Equals(
                        custom.Id, groupId, StringComparison.OrdinalIgnoreCase))
                    .ResultIds.Count(resultIds.Contains),
                _ => Math.Min(24, _results.Count),
            };

        private List<SuperPanelGroupDefinition> GetGroupDefinitions()
            => GetGroupDefinitions(_groups.GetGroups());

        private List<SuperPanelGroupDefinition> GetGroupDefinitions(
            IReadOnlyList<SuperPanelCustomGroup> customGroups)
            => new[]
                {
                    new SuperPanelGroupDefinition(
                        SuperPanelGroupIds.Smart,
                        Text("superPanel.group.smart", "智能推荐"),
                        Text(
                            "superPanel.group.smartHint",
                            "按上下文、相关性与使用习惯排序")),
                    new SuperPanelGroupDefinition(
                        SuperPanelGroupIds.Pinned,
                        Text("superPanel.group.pinned", "已固定"),
                        Text(
                            "superPanel.group.pinnedHint",
                            "可拖拽调整固定操作顺序")),
                    new SuperPanelGroupDefinition(
                        SuperPanelGroupIds.Recent,
                        Text("superPanel.group.recent", "最近使用"),
                        Text(
                            "superPanel.group.recentHint",
                            "按最后成功执行时间排序")),
                }
                .Concat(customGroups.Select(group =>
                    new SuperPanelGroupDefinition(
                        group.Id,
                        group.Title,
                        Text(
                            "superPanel.group.customHint",
                            "自定义操作文件夹"))))
                .ToList();

        internal string Text(string key, string fallback)
        {
            var value = _localize?.Invoke(key);
            return string.IsNullOrWhiteSpace(value) || value == key
                ? fallback
                : value;
        }
    }

    internal sealed record SuperPanelGroupDefinition(
        string Id,
        string Title,
        string Hint);

    internal sealed record SuperPanelGroupView(
        string Id,
        string Title,
        string Hint,
        bool IsActive,
        int Count,
        bool IsCustom);

    internal sealed record SuperPanelViewState(
        IReadOnlyList<SearchResultItem> VisibleResults,
        IReadOnlyList<SuperPanelGroupView> Groups,
        bool ShowEmptyState,
        string EmptyStateText,
        string StatusText,
        string InteractionHint,
        bool ShowCustomGroupActions,
        SuperPanelPresentationMode PresentationMode,
        SuperPanelPageView Page);

    internal enum SuperPanelPresentationMode
    {
        CompactGrid,
        ContextList,
    }

    internal sealed record SuperPanelPageView(
        int PageIndex,
        int PageCount,
        bool CanMovePrevious,
        bool CanMoveNext,
        string Label);

    internal sealed record SuperPanelGroupMutationResult(bool Success, string Message)
    {
        public static SuperPanelGroupMutationResult Failed { get; } = new(false, string.Empty);
    }
}
