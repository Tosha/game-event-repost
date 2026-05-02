using System;
using FluentAssertions;
using GuildRelay.Core.Stats;
using Xunit;

namespace GuildRelay.Core.Tests.Stats;

public class StatsAggregatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecordIncrementsTotal()
    {
        var agg = new StatsAggregator();
        agg.Record("Glory", 80, T0);

        var snap = agg.Snapshot(T0);
        snap.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new CounterSnapshot("Glory", 80, 80));
    }
}
