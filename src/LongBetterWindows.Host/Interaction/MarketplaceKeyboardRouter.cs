using System.Windows.Input;

namespace LongBetterWindows.Host.Interaction
{
    internal enum MarketplaceKeyboardAction
    {
        None,
        OpenSelectedDetail,
        NavigateBack,
        DismissConfirmation,
    }

    internal static class MarketplaceKeyboardRouter
    {
        public static Key NormalizeKey(Key key, Key systemKey)
        {
            return key == Key.System ? systemKey : key;
        }

        public static MarketplaceKeyboardAction Resolve(
            Key key,
            ModifierKeys modifiers,
            bool compactLayout,
            bool listFocused,
            bool hasSelection,
            bool confirmationVisible,
            bool confirmationBusy,
            bool detailOpen)
        {
            if (confirmationVisible)
            {
                return key == Key.Escape && !confirmationBusy
                    ? MarketplaceKeyboardAction.DismissConfirmation
                    : MarketplaceKeyboardAction.None;
            }

            if (key == Key.Left
                && modifiers == ModifierKeys.Alt
                && detailOpen)
            {
                return MarketplaceKeyboardAction.NavigateBack;
            }

            return compactLayout
                && listFocused
                && hasSelection
                && modifiers == ModifierKeys.None
                && key is Key.Enter or Key.Space
                    ? MarketplaceKeyboardAction.OpenSelectedDetail
                    : MarketplaceKeyboardAction.None;
        }
    }
}
