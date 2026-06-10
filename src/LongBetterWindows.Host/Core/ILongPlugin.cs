namespace LongBetterWindows.Host.Core
{
    public interface ILongPlugin
    {
        string Id { get; }
        string Name { get; }
        string Version { get; }
        PluginState State { get; }

        Task<bool> InitializeAsync(IHostApi host);
        Task<bool> StartAsync();
        Task<bool> StopAsync();
    }
}
