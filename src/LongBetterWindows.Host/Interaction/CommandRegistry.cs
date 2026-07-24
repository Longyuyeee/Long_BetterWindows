using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Interaction
{
    /// <summary>已注册插件命令的只读描述。</summary>
    public sealed record CommandDescriptor(
        string Key,
        string PluginId,
        string PluginName,
        PluginCommand Command)
    {
        public string ManifestPluginName { get; init; } = PluginName;
        public string Title { get; init; } = Command.Title;
        public string Description { get; init; } = Command.Description ?? string.Empty;
    }

    /// <summary>统一入口的搜索结果及其可解释得分。</summary>
    public sealed record CommandSearchResult(CommandDescriptor Descriptor, int Score);

    /// <summary>
    /// 维护插件命令索引并提供确定性的基础搜索排序。
    /// 拼音、历史权重和上下文服务将在后续迭代作为独立评分器接入。
    /// </summary>
    public sealed class CommandRegistry
    {
        private readonly Dictionary<string, CommandDescriptor> _commands =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public int Count
        {
            get { lock (_lock) return _commands.Count; }
        }

        public void RegisterManifest(PluginManifest manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest);

            lock (_lock)
            {
                RemovePluginCommands(manifest.Id);
                foreach (var command in manifest.Commands)
                {
                    var key = BuildKey(manifest.Id, command.Id);
                    _commands[key] = new CommandDescriptor(
                        key, manifest.Id, manifest.Name, command);
                }
            }
        }

        public void UnregisterPlugin(string pluginId)
        {
            if (string.IsNullOrWhiteSpace(pluginId)) return;
            lock (_lock) RemovePluginCommands(pluginId);
        }

        internal void ApplyLocalization(
            string pluginId,
            PluginLanguageContext context)
        {
            lock (_lock)
            {
                foreach (var key in _commands.Keys
                             .Where(key => key.StartsWith(
                                 pluginId + ":",
                                 StringComparison.OrdinalIgnoreCase))
                             .ToArray())
                {
                    var descriptor = _commands[key];
                    _commands[key] = descriptor with
                    {
                        PluginName = GetResource(
                            context,
                            "plugin.name",
                            descriptor.ManifestPluginName),
                        Title = GetResource(
                            context,
                            $"commands.{descriptor.Command.Id}.title",
                            descriptor.Command.Title),
                        Description = GetResource(
                            context,
                            $"commands.{descriptor.Command.Id}.description",
                            descriptor.Command.Description),
                    };
                }
            }
        }

        public IReadOnlyList<CommandDescriptor> GetAll()
        {
            lock (_lock)
            {
                return _commands.Values
                    .OrderBy(x => x.PluginName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        public CommandDescriptor? Get(string commandKey)
        {
            if (string.IsNullOrWhiteSpace(commandKey)) return null;
            lock (_lock)
            {
                _commands.TryGetValue(commandKey, out var command);
                return command;
            }
        }

        public IReadOnlyList<CommandSearchResult> Search(
            string query,
            AcceptedInputType? inputType = null,
            int maxResults = 20)
            => SearchCore(
                query,
                inputType.HasValue ? new[] { inputType.Value } : Array.Empty<AcceptedInputType>(),
                maxResults);

        public IReadOnlyList<CommandSearchResult> SearchWithInputs(
            string query,
            IReadOnlyCollection<AcceptedInputType> inputTypes,
            int maxResults = 20)
            => SearchCore(query, inputTypes, maxResults);

        private IReadOnlyList<CommandSearchResult> SearchCore(
            string query,
            IReadOnlyCollection<AcceptedInputType> inputTypes,
            int maxResults)
        {
            if (maxResults <= 0) return Array.Empty<CommandSearchResult>();
            var normalized = Normalize(query);

            List<CommandDescriptor> snapshot;
            lock (_lock) snapshot = _commands.Values.ToList();

            return snapshot
                .Select(x => new CommandSearchResult(x, Score(x, normalized, inputTypes)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Descriptor.Title, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .ToList();
        }

        private static int Score(
            CommandDescriptor descriptor,
            string query,
            IReadOnlyCollection<AcceptedInputType> inputTypes)
        {
            var command = descriptor.Command;
            var title = Normalize(descriptor.Title);
            var aliases = command.Aliases.Select(Normalize).Where(x => x.Length > 0).ToList();
            var score = 0;

            if (query.Length == 0)
            {
                if (inputTypes.Count > 0 && command.AcceptedInputs.Any(inputTypes.Contains))
                    score = 180;
                else if (command.AcceptedInputs.Contains(AcceptedInputType.None))
                    score = 100;
            }
            else if (title == query) score = 1000;
            else if (aliases.Contains(query)) score = 950;
            else if (title.StartsWith(query, StringComparison.Ordinal)) score = 820;
            else if (aliases.Any(x => x.StartsWith(query, StringComparison.Ordinal))) score = 780;
            else if (title.Contains(query, StringComparison.Ordinal)) score = 620;
            else if (aliases.Any(x => x.Contains(query, StringComparison.Ordinal))) score = 580;
            else if (Normalize(descriptor.PluginName).Contains(query, StringComparison.Ordinal)) score = 440;

            if (score == 0) return 0;

            if (inputTypes.Count > 0)
            {
                if (command.AcceptedInputs.Any(inputTypes.Contains)) score += 240;
                else if (!command.AcceptedInputs.Contains(AcceptedInputType.None)) return 0;
            }

            return score + Math.Clamp(command.Priority, -100, 100);
        }

        private void RemovePluginCommands(string pluginId)
        {
            var prefix = pluginId + ":";
            foreach (var key in _commands.Keys
                         .Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                _commands.Remove(key);
            }
        }

        private static string BuildKey(string pluginId, string commandId)
            => pluginId + ":" + commandId;

        private static string Normalize(string value)
            => value.Trim().ToLowerInvariant();

        private static string GetResource(
            PluginLanguageContext context,
            string key,
            string? fallback)
            => context.Resources.TryGetValue(key, out var value)
                && !string.IsNullOrWhiteSpace(value)
                    ? value
                    : fallback ?? string.Empty;
    }
}
