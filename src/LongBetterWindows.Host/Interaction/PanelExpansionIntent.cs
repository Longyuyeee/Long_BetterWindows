using System.Text.Json.Serialization;

namespace LongBetterWindows.Host.Interaction
{
    internal sealed class PanelExpansionIntent
    {
        private readonly object _stateLock = new();
        private nint _originWindowHandle;
        private string _query;
        private ContextSnapshot _context;
        private string? _selectedResultId;
        private bool _consumed;

        public PanelExpansionIntent(
            nint originWindowHandle,
            string? query,
            ContextSnapshot context,
            string? selectedResultId,
            DateTimeOffset capturedAt)
        {
            ArgumentNullException.ThrowIfNull(context);
            _originWindowHandle = originWindowHandle;
            _query = query ?? string.Empty;
            _context = context;
            _selectedResultId = string.IsNullOrWhiteSpace(selectedResultId)
                ? null
                : selectedResultId;
            CapturedAt = capturedAt;
            ContextItemCount = context.Items.Count;
            HasQuery = _query.Length > 0;
            HasSelection = _selectedResultId is not null;
        }

        public DateTimeOffset CapturedAt { get; }
        public int ContextItemCount { get; }
        public bool HasQuery { get; }
        public bool HasSelection { get; }
        public bool IsConsumed
        {
            get
            {
                lock (_stateLock)
                    return _consumed;
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

        public PanelExpansionState Consume()
        {
            lock (_stateLock)
            {
                if (_consumed)
                    throw new InvalidOperationException(
                        "The panel expansion intent has already been consumed.");

                var state = new PanelExpansionState(
                    _originWindowHandle,
                    _query,
                    _context,
                    _selectedResultId);
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
            => $"PanelExpansionIntent(ContextItems={ContextItemCount}, HasQuery={HasQuery}, HasSelection={HasSelection}, Consumed={IsConsumed})";

        private void ClearSensitiveState()
        {
            _originWindowHandle = nint.Zero;
            _query = string.Empty;
            _context = ContextSnapshot.Empty;
            _selectedResultId = null;
        }
    }

    internal sealed record PanelExpansionState(
        [property: JsonIgnore] nint OriginWindowHandle,
        [property: JsonIgnore] string Query,
        [property: JsonIgnore] ContextSnapshot Context,
        [property: JsonIgnore] string? SelectedResultId);
}
