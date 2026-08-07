namespace LongBetterWindows.Host.Interaction
{
    internal sealed record SettingsCategoryAccessibilityState(
        string Name,
        string ItemStatus,
        int PositionInSet,
        int SizeOfSet);

    internal static class SettingsCategoryAccessibilityProjection
    {
        public static SettingsCategoryAccessibilityState Build(
            string name,
            bool isSelected,
            int positionInSet,
            int sizeOfSet,
            Func<string, string> localize)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(localize);
            if (sizeOfSet <= 0)
                throw new ArgumentOutOfRangeException(nameof(sizeOfSet));
            if (positionInSet <= 0 || positionInSet > sizeOfSet)
                throw new ArgumentOutOfRangeException(nameof(positionInSet));

            return new SettingsCategoryAccessibilityState(
                name,
                string.Format(
                    localize(isSelected
                        ? "settings.category.state.selected"
                        : "settings.category.state.notSelected"),
                    positionInSet,
                    sizeOfSet),
                positionInSet,
                sizeOfSet);
        }
    }
}
