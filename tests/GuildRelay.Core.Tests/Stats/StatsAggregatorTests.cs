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

    [Fact]
    public void RecordsToSameLabelAggregate()
    {
        var agg = new StatsAggregator();
        agg.Record("Glory", 80, T0);
        agg.Record("Glory", 20, T0.AddSeconds(1));

        var snap = agg.Snapshot(T0.AddSeconds(2));
        snap.Should().ContainSingle()
            .Which.Total.Should().Be(100);
    }

    [Fact]
    public void RecordsToDifferentLabelsAreSeparate()
    {
        var agg = new StatsAggregator();
        agg.Record("Glory", 80, T0);
        agg.Record("Standing", 5, T0);

        var snap = agg.Snapshot(T0);
        snap.Should().HaveCount(2);
    }

    [Fact]
    public void AggregationKeyIsCaseInsensitiveAndTrimmed()
    {
        var agg = new StatsAggregator();
        agg.Record("Glory", 80, T0);
        agg.Record("glory ", 20, T0.AddSeconds(1));
        agg.Record(" GLORY", 10, T0.AddSeconds(2));

        var snap = agg.Snapshot(T0.AddSeconds(3));
        snap.Should().ContainSingle()
            .Which.Total.Should().Be(110);
    }
}
