using System.Windows.Input;

namespace LongBetterWindows.Host.Interaction
{
    internal static class SuperPanelKeyboardRouter
    {
        public static SuperPanelKeyboardCommand Resolve(
            Key key,
            ModifierKeys modifiers,
            SearchResultItem? selected,
            string activeGroupId)
        {
            if (key == Key.Enter
                && modifiers.HasFlag(ModifierKeys.Shift)
                && selected?.SecondaryActions.Count > 0)
                return SuperPanelKeyboardCommand.ExecuteSecondary;

            if (key == Key.Delete
                && selected is not null
                && SuperPanelGroupService.IsCustomGroupId(activeGroupId))
                return SuperPanelKeyboardCommand.RemoveFromGroup;

            if (key == Key.Enter && selected is not null)
                return SuperPanelKeyboardCommand.ExecutePrimary;

            if (key == Key.PageUp)
                return SuperPanelKeyboardCommand.PreviousPage;

            if (key == Key.PageDown)
                return SuperPanelKeyboardCommand.NextPage;

            return key == Key.Escape
                ? SuperPanelKeyboardCommand.Dismiss
                : SuperPanelKeyboardCommand.None;
        }
    }

    internal enum SuperPanelKeyboardCommand
    {
        None,
        ExecutePrimary,
        ExecuteSecondary,
        RemoveFromGroup,
        PreviousPage,
        NextPage,
        Dismiss,
    }
}
