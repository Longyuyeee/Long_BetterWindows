namespace LongBetterWindows.Host.Interaction
{
    internal static class ContextMetadataProjection
    {
        public static string Project(ContextSnapshot snapshot, bool isLoading = false)
        {
            if (isLoading)
                return "context:loading";

            var inputs = snapshot.AvailableInputTypes.Count == 0
                ? "none"
                : string.Join(",", snapshot.AvailableInputTypes.Select(input =>
                    input.ToString().ToLowerInvariant()));
            return $"context-items:{snapshot.Items.Count};inputs:{inputs}";
        }
    }
}
