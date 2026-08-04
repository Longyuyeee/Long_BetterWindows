namespace LongBetterWindows.Host.Engine
{
    public enum PluginRuntimeHealthState
    {
        Idle,
        Healthy,
        Degraded,
        Unhealthy,
    }

    public enum PluginRuntimeFailureKind
    {
        None,
        StartFailed,
        CommandFailed,
        UnhandledException,
    }

    public sealed record PluginRuntimeHealthSnapshot(
        string PluginId,
        PluginRuntimeHealthState State,
        long ExecutionCount,
        long SuccessCount,
        long FailureCount,
        long CancellationCount,
        long ExceptionCount,
        long ConsecutiveFailureCount,
        double LastDurationMilliseconds,
        double MaximumDurationMilliseconds,
        PluginRuntimeFailureKind LastFailureKind,
        DateTimeOffset? LastObservedAt);

    /// <summary>
    /// Keeps compact, privacy-safe runtime health counters. It never stores command inputs,
    /// outputs, exception messages, or stack traces.
    /// </summary>
    public sealed class PluginRuntimeHealthMonitor
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, MutableHealth> _health =
            new(StringComparer.OrdinalIgnoreCase);

        public PluginRuntimeHealthSnapshot GetSnapshot(string pluginId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
            lock (_sync)
            {
                return _health.TryGetValue(pluginId, out var health)
                    ? CreateSnapshot(pluginId, health)
                    : Empty(pluginId);
            }
        }

        public IReadOnlyList<PluginRuntimeHealthSnapshot> GetAllSnapshots()
        {
            lock (_sync)
            {
                return _health
                    .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => CreateSnapshot(item.Key, item.Value))
                    .ToArray();
            }
        }

        public void RecordSuccess(string pluginId, TimeSpan duration)
            => Record(pluginId, duration, RuntimeOutcome.Success);

        public void RecordFailure(
            string pluginId,
            TimeSpan duration,
            PluginRuntimeFailureKind kind = PluginRuntimeFailureKind.CommandFailed)
        {
            if (kind is PluginRuntimeFailureKind.None
                or PluginRuntimeFailureKind.UnhandledException)
                throw new ArgumentOutOfRangeException(nameof(kind));
            Record(pluginId, duration, RuntimeOutcome.Failure, kind);
        }

        public void RecordCancellation(string pluginId, TimeSpan duration)
            => Record(pluginId, duration, RuntimeOutcome.Cancellation);

        public void RecordException(string pluginId, TimeSpan duration)
            => Record(
                pluginId,
                duration,
                RuntimeOutcome.Exception,
                PluginRuntimeFailureKind.UnhandledException);

        private void Record(
            string pluginId,
            TimeSpan duration,
            RuntimeOutcome outcome,
            PluginRuntimeFailureKind failureKind = PluginRuntimeFailureKind.None)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
            if (duration < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(duration));

            lock (_sync)
            {
                if (!_health.TryGetValue(pluginId, out var health))
                {
                    health = new MutableHealth();
                    _health.Add(pluginId, health);
                }

                health.ExecutionCount++;
                health.LastDurationMilliseconds = duration.TotalMilliseconds;
                health.MaximumDurationMilliseconds = Math.Max(
                    health.MaximumDurationMilliseconds,
                    duration.TotalMilliseconds);
                health.LastObservedAt = DateTimeOffset.UtcNow;
                switch (outcome)
                {
                    case RuntimeOutcome.Success:
                        health.SuccessCount++;
                        health.ConsecutiveFailureCount = 0;
                        health.LastFailureKind = PluginRuntimeFailureKind.None;
                        break;
                    case RuntimeOutcome.Failure:
                        health.FailureCount++;
                        health.ConsecutiveFailureCount++;
                        health.LastFailureKind = failureKind;
                        break;
                    case RuntimeOutcome.Cancellation:
                        health.CancellationCount++;
                        break;
                    case RuntimeOutcome.Exception:
                        health.FailureCount++;
                        health.ExceptionCount++;
                        health.ConsecutiveFailureCount++;
                        health.LastFailureKind = failureKind;
                        break;
                }
            }
        }

        private static PluginRuntimeHealthSnapshot CreateSnapshot(
            string pluginId,
            MutableHealth health)
            => new(
                pluginId,
                ResolveState(health),
                health.ExecutionCount,
                health.SuccessCount,
                health.FailureCount,
                health.CancellationCount,
                health.ExceptionCount,
                health.ConsecutiveFailureCount,
                Math.Round(health.LastDurationMilliseconds, 3),
                Math.Round(health.MaximumDurationMilliseconds, 3),
                health.LastFailureKind,
                health.LastObservedAt);

        private static PluginRuntimeHealthState ResolveState(MutableHealth health)
        {
            if (health.ExecutionCount == 0) return PluginRuntimeHealthState.Idle;
            if (health.LastFailureKind == PluginRuntimeFailureKind.UnhandledException
                || health.ConsecutiveFailureCount >= 3)
                return PluginRuntimeHealthState.Unhealthy;
            if (health.ConsecutiveFailureCount > 0)
                return PluginRuntimeHealthState.Degraded;
            return PluginRuntimeHealthState.Healthy;
        }

        private static PluginRuntimeHealthSnapshot Empty(string pluginId)
            => new(
                pluginId,
                PluginRuntimeHealthState.Idle,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                PluginRuntimeFailureKind.None,
                null);

        private enum RuntimeOutcome
        {
            Success,
            Failure,
            Cancellation,
            Exception,
        }

        private sealed class MutableHealth
        {
            public long ExecutionCount { get; set; }
            public long SuccessCount { get; set; }
            public long FailureCount { get; set; }
            public long CancellationCount { get; set; }
            public long ExceptionCount { get; set; }
            public long ConsecutiveFailureCount { get; set; }
            public double LastDurationMilliseconds { get; set; }
            public double MaximumDurationMilliseconds { get; set; }
            public PluginRuntimeFailureKind LastFailureKind { get; set; }
            public DateTimeOffset? LastObservedAt { get; set; }
        }
    }
}
