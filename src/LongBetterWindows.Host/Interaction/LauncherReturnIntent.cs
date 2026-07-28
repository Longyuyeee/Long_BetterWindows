using System.Text.Json.Serialization;

namespace LongBetterWindows.Host.Interaction
{
    internal enum LauncherReturnMode
    {
        RestoreLauncher,
        ClearLauncher,
        RestoreOriginWindow,
    }

    internal enum LauncherFocusTarget
    {
        LauncherInput,
        OriginWindow,
    }

    internal sealed class LauncherReturnIntent
    {
        private readonly object _stateLock = new();
        private nint _originWindowHandle;
        private string _query;
        private ContextSnapshot _context;
        private bool _consumed;

        public LauncherReturnIntent(
            nint originWindowHandle,
            string? query,
            ContextSnapshot context,
            LauncherReturnMode mode,
            DateTimeOffset capturedAt)
        {
            ArgumentNullException.ThrowIfNull(context);
            _originWindowHandle = originWindowHandle;
            _query = query ?? string.Empty;
            _context = context;
            Mode = mode;
            CapturedAt = capturedAt;
            ContextItemCount = context.Items.Count;
            HasQuery = _query.Length > 0;
        }

        public LauncherReturnMode Mode { get; }
        public DateTimeOffset CapturedAt { get; }
        public int ContextItemCount { get; }
        public bool HasQuery { get; }
        public bool IsConsumed
        {
            get
            {
                lock (_stateLock)
                    return _consumed;
            }
        }

        [JsonIgnore]
        public nint OriginWindowHandle
        {
            get
            {
                lock (_stateLock)
                    return _originWindowHandle;
            }
        }

        [JsonIgnore]
        public string Query
        {
            get
            {
                lock (_stateLock)
                    return _query;
            }
        }

        [JsonIgnore]
        public ContextSnapshot Context
        {
            get
            {
                lock (_stateLock)
                    return _context;
            }
        }

        public LauncherReturnState Consume(bool originWindowIsAvailable)
        {
            lock (_stateLock)
            {
                if (_consumed)
                    throw new InvalidOperationException(
                        "The launcher return intent has already been consumed.");

                var restoreOrigin = Mode == LauncherReturnMode.RestoreOriginWindow
                    && _originWindowHandle != nint.Zero
                    && originWindowIsAvailable;
                var restoreLauncherState = Mode == LauncherReturnMode.RestoreLauncher;
                var availableOrigin = _originWindowHandle != nint.Zero
                    && originWindowIsAvailable
                        ? _originWindowHandle
                        : nint.Zero;
                var state = new LauncherReturnState(
                    availableOrigin,
                    restoreLauncherState ? _query : string.Empty,
                    restoreLauncherState ? _context : ContextSnapshot.Empty,
                    restoreOrigin
                        ? LauncherFocusTarget.OriginWindow
                        : LauncherFocusTarget.LauncherInput);

                ClearSensitiveState();
                _consumed = true;
                return state;
            }
        }

        public void Discard()
        {
            lock (_stateLock)
            {
                if (_consumed)
                    return;
                ClearSensitiveState();
                _consumed = true;
            }
        }

        public override string ToString()
            => $"LauncherReturnIntent(Mode={Mode}, ContextItems={ContextItemCount}, HasQuery={HasQuery}, Consumed={IsConsumed})";

        private void ClearSensitiveState()
        {
            _originWindowHandle = nint.Zero;
            _query = string.Empty;
            _context = ContextSnapshot.Empty;
        }
    }

    internal sealed record LauncherReturnState(
        [property: JsonIgnore] nint OriginWindowHandle,
        [property: JsonIgnore] string Query,
        [property: JsonIgnore] ContextSnapshot Context,
        LauncherFocusTarget FocusTarget);
}
