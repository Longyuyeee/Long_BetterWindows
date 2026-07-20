namespace LongBetterWindows.Host.Core
{
    public enum PluginState
    {
        Loaded = 0,
        Running = 1,
        Error = 2,
        Stopped = 3,

        // v0.6 及更早插件的源码兼容别名；宿主新代码统一使用 Stopped。
        Disabled = Stopped,
        Background = 4,
    }
}
