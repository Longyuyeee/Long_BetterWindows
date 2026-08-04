namespace LongBetterWindows.Host.Contracts;

public static class PluginUiKitVersion
{
    public const string Current = "1.3.0";

    public static Version CurrentVersion { get; } = Version.Parse(Current);
}
