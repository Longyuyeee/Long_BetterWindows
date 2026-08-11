using System.Diagnostics;
using System.Globalization;

namespace LongBetterWindows.Host.Interaction
{
    internal sealed class LauncherLatencySession
    {
        private readonly Func<long> _getTimestamp;
        private readonly Func<long, long, TimeSpan> _getElapsed;
        private long _invocationStarted;
        private long _queryStarted;

        internal LauncherLatencySession(
            Func<long>? getTimestamp = null,
            Func<long, long, TimeSpan>? getElapsed = null)
        {
            _getTimestamp = getTimestamp ?? Stopwatch.GetTimestamp;
            _getElapsed = getElapsed ?? ((start, end) =>
                Stopwatch.GetElapsedTime(start, end));
        }

        internal TimeSpan? FirstFrameElapsed { get; private set; }

        internal TimeSpan? FirstResultsElapsed { get; private set; }

        internal TimeSpan? QueryFirstResultsElapsed { get; private set; }

        internal void BeginInvocation(long? started = null)
        {
            _invocationStarted = started ?? _getTimestamp();
            FirstFrameElapsed = null;
            FirstResultsElapsed = null;
            BeginQuery();
        }

        internal void BeginQuery()
        {
            _queryStarted = _getTimestamp();
            QueryFirstResultsElapsed = null;
        }

        internal TimeSpan? MarkFirstFrame()
        {
            if (_invocationStarted == 0 || FirstFrameElapsed is not null)
                return null;

            FirstFrameElapsed = _getElapsed(
                _invocationStarted,
                _getTimestamp());
            return FirstFrameElapsed;
        }

        internal LauncherResultLatency? MarkFirstActionableResults(int count)
        {
            if (count <= 0 || _queryStarted == 0
                || QueryFirstResultsElapsed is not null)
            {
                return null;
            }

            var now = _getTimestamp();
            QueryFirstResultsElapsed = _getElapsed(_queryStarted, now);
            if (_invocationStarted != 0 && FirstResultsElapsed is null)
                FirstResultsElapsed = _getElapsed(_invocationStarted, now);

            return new LauncherResultLatency(
                FirstResultsElapsed,
                QueryFirstResultsElapsed.Value);
        }

        internal string ToAutomationStatus()
            => string.Join(
                ";",
                Format("first_frame_ms", FirstFrameElapsed),
                Format("first_results_ms", FirstResultsElapsed),
                Format("query_first_results_ms", QueryFirstResultsElapsed));

        private static string Format(string name, TimeSpan? value)
            => value is null
                ? $"{name}=pending"
                : $"{name}={value.Value.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}";
    }

    internal sealed record LauncherResultLatency(
        TimeSpan? InvocationElapsed,
        TimeSpan QueryElapsed);
}
