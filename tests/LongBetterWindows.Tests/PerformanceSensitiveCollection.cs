namespace LongBetterWindows.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PerformanceSensitiveCollection
{
    public const string Name = "Performance sensitive";
}
