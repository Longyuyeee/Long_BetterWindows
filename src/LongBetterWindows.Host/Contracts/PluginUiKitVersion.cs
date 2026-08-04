namespace LongBetterWindows.Host.Contracts;

public static class PluginUiKitVersion
{
    public const string Current = "1.2.0";

    public static Version CurrentVersion { get; } = Version.Parse(Current);
}
