using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Events;
using Xunit;

namespace GuildRelay.Core.Tests.Events;

public class EventBusTests
{
    private static DetectionEvent Make(string label) => new(
        FeatureId: "chat", RuleLabel: label, MatchedContent: label,
        TimestampUtc: System.DateTimeOffset.UtcNow, PlayerName: "Tosh",
        Extras: new Dictionary<string, string>(), ImageAttachment: null);

    [Fact]
    public async Task PublishedEventCanBeConsumed()
    {
        var bus = new EventBus(capacity: 8);
        await bus.PublishAsync(Make("a"), CancellationToken.None);
        bus.Complete();

        var received = new List<string>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            received.Add(e.RuleLabel);

        received.Should().Equal("a");
    }

    [Fact]
    public async Task OverCapacityDropsNewestKeepsOldest()
    {
        // With DropWrite mode, TryWrite "completes" even when the item is
        // dropped (the .NET runtime treats the drop as a successful no-op,
        // not a rejection). So we verify overflow behavior by checking what
        // actually comes out, not by inspecting the return value.
        var bus = new EventBus(capacity: 2);
        await bus.PublishAsync(Make("a"), CancellationToken.None);
        await bus.PublishAsync(Make("b"), CancellationToken.None);
        await bus.PublishAsync(Make("c"), CancellationToken.None); // should be silently dropped

        bus.Complete();

        var received = new List<string>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            received.Add(e.RuleLabel);

        received.Should().Equal("a", "b");
    }
}
