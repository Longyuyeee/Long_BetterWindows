namespace LongBetterWindows.Host.Interaction
{
    internal sealed class SuperPanelGroupCoordinator
    {
        private static readonly IReadOnlyList<SuperPanelGroupDefinition> BuiltInGroups =
        [
            new(SuperPanelGroupIds.Smart, "智能推荐", "按上下文、相关性与使用习惯排序"),
            new(SuperPanelGroupIds.Pinned, "已固定", "可拖拽调整固定操作顺序"),
            new(SuperPanelGroupIds.Recent, "最近使用", "按最后成功执行时间排序"),
        ];

        private readonly SearchPreferenceService _preferences;
        private readonly SuperPanelGroupService _groups;
        private IReadOnlyList<SearchResultItem> _results = Array.Empty<SearchResultItem>();
        private bool _searchCompleted;

        public SuperPanelGroupCoordinator(
            SearchPreferenceService preferences,
            SuperPanelGroupService groups)
        {
            _preferences = preferences;
            _groups = groups;
        }

        public string ActiveGroupId { get; private set; } = SuperPanelGroupIds.Smart;

        public SuperPanelCustomGroup? ActiveCustomGroup => _groups.GetGroups()
            .FirstOrDefault(group => string.Equals(
                group.Id, ActiveGroupId, StringComparison.OrdinalIgnoreCase));

        public void ResetResults()
        {
            _results = Array.Empty<SearchResultItem>();
            _searchCompleted = false;
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
                        ? "固定顺序已保存"
                        : "分组顺序已保存")
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
            return new SuperPanelGroupMutationResult(true, "已移动到分组");
        }

        public async Task<SuperPanelGroupMutationResult> SaveGroupAsync(
            string? groupId,
            string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return new SuperPanelGroupMutationResult(false, "请输入分组名称");

            if (groupId is null)
            {
                var group = await _groups.CreateAsync(title);
                if (group is null)
                    return new SuperPanelGroupMutationResult(false, "最多创建 8 个分组");
                SelectGroup(group.Id);
            }
            else if (!await _groups.RenameAsync(groupId, title))
            {
                return new SuperPanelGroupMutationResult(false, "分组重命名失败");
            }

            return new SuperPanelGroupMutationResult(true, "分组已保存");
        }

        public async Task<SuperPanelGroupMutationResult> DeleteActiveGroupAsync()
        {
            var group = ActiveCustomGroup;
            if (group is null || !await _groups.DeleteAsync(group.Id))
                return SuperPanelGroupMutationResult.Failed;

            SelectGroup(SuperPanelGroupIds.Pinned);
            return new SuperPanelGroupMutationResult(true, "分组已删除");
        }

        public async Task<SuperPanelGroupMutationResult> RemoveFromActiveGroupAsync(
            string resultId)
        {
            if (!SuperPanelGroupService.IsCustomGroupId(ActiveGroupId)
                || !await _groups.RemoveResultAsync(ActiveGroupId, resultId))
                return SuperPanelGroupMutationResult.Failed;

            return new SuperPanelGroupMutationResult(true, "已移出分组");
        }

        public SuperPanelViewState BuildView()
        {
            var pinnedIds = _preferences.GetPinnedResultIds();
            var recentIds = _preferences.GetRecentResultIds(24);
            var customGroups = _groups.GetGroups();
            var customGroup = customGroups.FirstOrDefault(group => string.Equals(
                group.Id, ActiveGroupId, StringComparison.OrdinalIgnoreCase));
            var visible = SuperPanelResultOrganizer.SelectGroup(
                _results,
                ActiveGroupId,
                pinnedIds,
                recentIds,
                customGroup?.ResultIds,
                maxResults: 6);
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
                    SuperPanelGroupIds.Pinned => "还没有固定操作",
                    SuperPanelGroupIds.Recent => "还没有最近使用记录",
                    _ when customGroup is not null => "把固定操作拖到这个分组",
                    _ => "当前上下文没有可用操作",
                },
                visible.Count > 0
                    ? $"{visible.Count} 个操作"
                    : _searchCompleted ? "当前分组为空" : "正在匹配...",
                ActiveGroupId switch
                {
                    SuperPanelGroupIds.Pinned => "拖拽排序或拖到文件夹 · 单击执行 · 滚轮切组",
                    _ when customGroup is not null => "拖拽排序 · Delete 移出分组 · 滚轮切组",
                    _ => "单击执行 · 滚轮切组 · Esc 返回原窗口",
                },
                customGroup is not null);
        }

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
                _ => Math.Min(6, _results.Count),
            };

        private List<SuperPanelGroupDefinition> GetGroupDefinitions()
            => GetGroupDefinitions(_groups.GetGroups());

        private static List<SuperPanelGroupDefinition> GetGroupDefinitions(
            IReadOnlyList<SuperPanelCustomGroup> customGroups)
            => BuiltInGroups.Concat(customGroups.Select(group =>
                    new SuperPanelGroupDefinition(
                        group.Id,
                        group.Title,
                        "自定义操作文件夹")))
                .ToList();
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
        bool ShowCustomGroupActions);

    internal sealed record SuperPanelGroupMutationResult(bool Success, string Message)
    {
        public static SuperPanelGroupMutationResult Failed { get; } = new(false, string.Empty);
    }
}
