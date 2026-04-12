using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Features;
using Xunit;

namespace GuildRelay.Core.Tests.Features;

public class WatchdogTaskTests
{
    [Fact]
    public async Task ThrowingBodyTransitionsToErrorAfterExhaustingBackoffs()
    {
        var attempts = 0;
        var watchdog = new WatchdogTask(
            name: "test",
            body: _ => { attempts++; throw new InvalidOperationException("boom"); },
            backoffs: new[] { TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero });

        await watchdog.StartAsync(CancellationToken.None);
        await watchdog.WaitForTerminalAsync(TimeSpan.FromSeconds(2));

        watchdog.State.Should().Be(WatchdogState.Error);
        attempts.Should().Be(3);
        watchdog.LastError.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task NormalCompletionTransitionsToStopped()
    {
        var watchdog = new WatchdogTask(
            name: "test",
            body: _ => Task.CompletedTask,
            backoffs: new[] { TimeSpan.Zero });

        await watchdog.StartAsync(CancellationToken.None);
        await watchdog.WaitForTerminalAsync(TimeSpan.FromSeconds(2));

        watchdog.State.Should().Be(WatchdogState.Stopped);
        watchdog.LastError.Should().BeNull();
    }

    [Fact]
    public async Task CancellationStopsGracefully()
    {
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource();
        var watchdog = new WatchdogTask(
            name: "test",
            body: async ct =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            },
            backoffs: new[] { TimeSpan.Zero });

        await watchdog.StartAsync(cts.Token);
        await entered.Task; // wait until body is running
        cts.Cancel();
        await watchdog.WaitForTerminalAsync(TimeSpan.FromSeconds(2));

        watchdog.State.Should().Be(WatchdogState.Stopped);
    }
}
