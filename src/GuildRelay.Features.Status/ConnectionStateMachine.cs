namespace GuildRelay.Features.Status;

public enum ConnectionState { Unknown, Connected, Disconnected }

public sealed record StateTransition(ConnectionState From, ConnectionState To);

/// <summary>
/// Debounced state machine tracking connected/disconnected state.
/// Requires N consecutive confirming observations before transitioning.
/// Transitions out of Unknown are always silent (first-run).
/// </summary>
public sealed class ConnectionStateMachine
{
    private readonly int _debounceSamples;
    private int _pendingCount;
    private bool _pendingIsDisconnected;

    public ConnectionStateMachine(int debounceSamples)
    {
        _debounceSamples = debounceSamples;
    }

    public ConnectionState State { get; private set; } = ConnectionState.Unknown;

    /// <summary>
    /// Feed one observation. Returns a <see cref="StateTransition"/> if a
    /// confirmed state change occurred, or null if no transition.
    /// </summary>
    public StateTransition? Observe(bool isDisconnected)
    {
        var targetState = isDisconnected ? ConnectionState.Disconnected : ConnectionState.Connected;

        // If we're already in the target state, nothing to do
        if (State == targetState)
        {
            _pendingCount = 0;
            return null;
        }

        // Check if this observation continues the pending direction
        if (_pendingCount > 0 && _pendingIsDisconnected == isDisconnected)
        {
            _pendingCount++;
        }
        else
        {
            // New direction or first observation
            _pendingCount = 1;
            _pendingIsDisconnected = isDisconnected;
        }

        if (_pendingCount >= _debounceSamples)
        {
            var previous = State;
            State = targetState;
            _pendingCount = 0;

            // Transitions out of Unknown are silent (first-run)
            if (previous == ConnectionState.Unknown)
                return null;

            return new StateTransition(previous, targetState);
        }

        return null;
    }
}
