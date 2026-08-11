namespace LongBetterWindows.Host.Interaction
{
    internal readonly record struct StableSelectionResolution(
        int Index,
        bool Preserved);

    internal static class StableSelectionResolver
    {
        public static StableSelectionResolution Resolve<T>(
            IReadOnlyList<T> items,
            string? preferredId,
            Func<T, string> idSelector)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(idSelector);

            if (items.Count == 0)
                return new StableSelectionResolution(-1, false);

            if (!string.IsNullOrWhiteSpace(preferredId))
            {
                for (var index = 0; index < items.Count; index++)
                {
                    if (string.Equals(
                            idSelector(items[index]),
                            preferredId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return new StableSelectionResolution(index, true);
                    }
                }
            }

            return new StableSelectionResolution(0, false);
        }
    }
}
