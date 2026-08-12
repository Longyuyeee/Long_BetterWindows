namespace LongBetterWindows.Host.Services
{
    internal static class WebViewLifecycleGrowthPolicy
    {
        internal const int MaximumHandleGrowth = 64;
        internal const int MaximumThreadGrowth = 8;
        internal const long MaximumPrivateMemoryGrowthBytes =
            64L * 1024 * 1024;

        internal static WebViewLifecycleGrowthResult Evaluate(
            int warmHandleCount,
            int finalHandleCount,
            int warmThreadCount,
            int finalThreadCount,
            long warmPrivateMemoryBytes,
            long finalPrivateMemoryBytes)
        {
            var handleGrowth = finalHandleCount - warmHandleCount;
            var threadGrowth = finalThreadCount - warmThreadCount;
            var privateMemoryGrowth =
                finalPrivateMemoryBytes - warmPrivateMemoryBytes;
            return new WebViewLifecycleGrowthResult(
                handleGrowth,
                threadGrowth,
                privateMemoryGrowth,
                handleGrowth <= MaximumHandleGrowth
                    && threadGrowth <= MaximumThreadGrowth
                    && privateMemoryGrowth
                        <= MaximumPrivateMemoryGrowthBytes);
        }
    }

    internal sealed record WebViewLifecycleGrowthResult(
        int HandleCount,
        int ThreadCount,
        long PrivateMemoryBytes,
        bool Passed);
}
