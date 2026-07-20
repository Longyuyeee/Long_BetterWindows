using System.Windows;
using System.Windows.Input;

namespace LongBetterWindows.Host.Interaction
{
    internal sealed class SuperPanelDragSession
    {
        private Point _start;
        private SearchResultItem? _candidate;
        private bool _suppressClick;

        public string? SourceGroupId { get; private set; }

        public bool TryBegin(
            string activeGroupId,
            SearchResultItem result,
            Point start,
            bool originInsideButton)
        {
            Reset();
            if (originInsideButton
                || (activeGroupId != SuperPanelGroupIds.Pinned
                    && !SuperPanelGroupService.IsCustomGroupId(activeGroupId))
                || (activeGroupId == SuperPanelGroupIds.Pinned && !result.IsPinned))
                return false;

            _start = start;
            _candidate = result;
            SourceGroupId = activeGroupId;
            return true;
        }

        public bool TryStartDrag(
            Point current,
            MouseButtonState leftButton,
            double minimumHorizontalDistance,
            double minimumVerticalDistance,
            out string? resultId)
        {
            resultId = null;
            if (_candidate is null || leftButton != MouseButtonState.Pressed)
                return false;
            if (Math.Abs(current.X - _start.X) < minimumHorizontalDistance
                && Math.Abs(current.Y - _start.Y) < minimumVerticalDistance)
                return false;

            resultId = _candidate.Id;
            _candidate = null;
            _suppressClick = true;
            return true;
        }

        public bool ConsumeClickSuppression()
        {
            _candidate = null;
            if (!_suppressClick) return false;
            _suppressClick = false;
            return true;
        }

        public void CompleteDrop() => SourceGroupId = null;

        public void Reset()
        {
            _candidate = null;
            SourceGroupId = null;
            _suppressClick = false;
        }

        public static bool CanDropOnResults(string activeGroupId) =>
            activeGroupId == SuperPanelGroupIds.Pinned
            || SuperPanelGroupService.IsCustomGroupId(activeGroupId);

        public static bool CanDropOnGroup(string groupId) =>
            SuperPanelGroupService.IsCustomGroupId(groupId);
    }
}
