using FluentAssertions;
using GuildRelay.Features.Status;
using Xunit;

namespace GuildRelay.Features.Status.Tests;

public class ConnectionStateMachineTests
{
    [Fact]
    public void InitialStateIsUnknown()
    {
        var sm = new ConnectionStateMachine(debounceSamples: 3);
        sm.State.Should().Be(ConnectionState.Unknown);
    }

    [Fact]
    public void TransitionFromUnknownToConnectedIsSilent()
    {
        var sm = new ConnectionStateMachine(debounceSamples: 1);
        var transition = sm.Observe(isDisconnected: false);
        transition.Should().BeNull("first-run transitions out of Unknown are silent");
        sm.State.Should().Be(ConnectionState.Connected);
    }

    [Fact]
    public void TransitionFromUnknownToDisconnectedIsSilent()
    {
        var sm = new ConnectionStateMachine(debounceSamples: 1);
        var transition = sm.Observe(isDisconnected: true);
        transition.Should().BeNull("first-run transitions out of Unknown are silent");
        sm.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public void ConnectedToDisconnectedEmitsTransition()
    {
        var sm = new ConnectionStateMachine(debounceSamples: 1);
        sm.Observe(isDisconnected: false); // Unknown → Connected (silent)

        var transition = sm.Observe(isDisconnected: true);
        transition.Should().NotBeNull();
        transition!.From.Should().Be(ConnectionState.Connected);
        transition.To.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public void DisconnectedToConnectedEmitsTransition()
    {
        var sm = new ConnectionStateMachine(debounceSamples: 1);
        sm.Observe(isDisconnected: true); // Unknown → Disconnected (silent)

        var transition = sm.Observe(isDisconnected: false);
        transition.Should().NotBeNull();
        transition!.From.Should().Be(ConnectionState.Disconnected);
        transition.To.Should().Be(ConnectionState.Connected);
    }

    [Fact]
    public void DebounceRequiresNConsecutiveConfirmations()
    {
        var sm = new ConnectionStateMachine(debounceSamples: 3);

        // Establish Connected state (3 observations needed)
        sm.Observe(isDisconnected: false).Should().BeNull(); // 1/3 → still Unknown
        sm.Observe(isDisconnected: false).Should().BeNull(); // 2/3
        sm.Observe(isDisconnected: false).Should().BeNull(); // 3/3 → Connected (silent)
        sm.State.Should().Be(ConnectionState.Connected);

        // Now try to transition to Disconnected — need 3 consecutive
        sm.Observe(isDisconnected: true).Should().BeNull();  // 1/3
        sm.Observe(isDisconnected: true).Should().BeNull();  // 2/3
        var t = sm.Observe(isDisconnected: true);            // 3/3 → fires!
        t.Should().NotBeNull();
        t!.To.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public void InterruptedDebounceResetsCounter()
    {
        var sm = new ConnectionStateMachine(debounceSamples: 3);

        // Establish Connected
        sm.Observe(isDisconnected: false);
        sm.Observe(isDisconnected: false);
        sm.Observe(isDisconnected: false);

        // Start disconnecting, then interrupt with a connected observation
        sm.Observe(isDisconnected: true);  // 1/3
        sm.Observe(isDisconnected: true);  // 2/3
        sm.Observe(isDisconnected: false); // interrupt! resets counter
        sm.State.Should().Be(ConnectionState.Connected);

        // Need full 3 again
        sm.Observe(isDisconnected: true).Should().BeNull();  // 1/3
        sm.Observe(isDisconnected: true).Should().BeNull();  // 2/3
        sm.Observe(isDisconnected: true).Should().NotBeNull(); // 3/3
    }

    [Fact]
    public void SameStateObservationsDoNotReEmit()
    {
        var sm = new ConnectionStateMachine(debounceSamples: 1);
        sm.Observe(isDisconnected: false); // Unknown → Connected (silent)

        sm.Observe(isDisconnected: false).Should().BeNull("already Connected, no transition");
        sm.Observe(isDisconnected: false).Should().BeNull();
    }
}
