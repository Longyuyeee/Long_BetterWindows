namespace LongBetterWindows.Host.Engine
{
    public static class PluginAccessContext
    {
        private static readonly AsyncLocal<string?> _currentPluginId = new();

        public static string? CurrentPluginId
        {
            get => _currentPluginId.Value;
            set => _currentPluginId.Value = value;
        }

        public static IDisposable Enter(string pluginId)
        {
            var previous = CurrentPluginId;
            CurrentPluginId = pluginId;
            return new ContextScope(() => CurrentPluginId = previous);
        }

        private sealed class ContextScope : IDisposable
        {
            private readonly Action _onDispose;

            public ContextScope(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                _onDispose();
            }
        }
    }
}
