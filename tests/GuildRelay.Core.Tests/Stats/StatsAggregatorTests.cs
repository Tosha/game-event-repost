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

    [Fact]
    public void Last60MinExcludesEventsOlderThan60Minutes()
    {
        var agg = new StatsAggregator();
        agg.Record("Glory", 100, T0);
        agg.Record("Glory", 50, T0.AddMinutes(30));

        var snap = agg.Snapshot(T0.AddMinutes(70));
        snap.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new CounterSnapshot("Glory", 150, 50));
    }

    [Fact]
    public void Last60MinExcludesEventsAtExactly60MinBoundary()
    {
        // strict > now - 60min. An event timestamped exactly 60 minutes ago is excluded.
        var agg = new StatsAggregator();
        agg.Record("Glory", 100, T0);

        var snap = agg.Snapshot(T0.AddMinutes(60));
        snap.Should().ContainSingle()
            .Which.Last60Min.Should().Be(0);
    }

    [Fact]
    public void ResetClearsTotalAndRollingHistoryForOneLabel()
    {
        var agg = new StatsAggregator();
        agg.Record("Glory", 80, T0);
        agg.Record("Standing", 5, T0);

        agg.Reset("Glory");

        var snap = agg.Snapshot(T0);
        snap.Should().HaveCount(2);
        var glory    = System.Linq.Enumerable.Single(snap, s => s.Label == "Glory");
        var standing = System.Linq.Enumerable.Single(snap, s => s.Label == "Standing");
        glory.Total.Should().Be(0);
        glory.Last60Min.Should().Be(0);
        standing.Total.Should().Be(5);
    }

    [Fact]
    public void ResetAllClearsAllCounters()
    {
        var agg = new StatsAggregator();
        agg.Record("Glory", 80, T0);
        agg.Record("Standing", 5, T0);

        agg.ResetAll();

        var snap = agg.Snapshot(T0);
        snap.Should().AllSatisfy(s =>
        {
            s.Total.Should().Be(0);
            s.Last60Min.Should().Be(0);
        });
    }

    [Fact]
    public async System.Threading.Tasks.Task ConcurrentRecordAndSnapshotIsSafe()
    {
        var agg = new StatsAggregator();
        var stop = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));

        var writer = System.Threading.Tasks.Task.Run(() =>
        {
            var t = T0;
            while (!stop.IsCancellationRequested)
            {
                agg.Record("Glory", 1, t);
                t = t.AddMilliseconds(1);
            }
        });

        var reader = System.Threading.Tasks.Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                _ = agg.Snapshot(T0.AddMinutes(30));
            }
        });

        await System.Threading.Tasks.Task.WhenAll(writer, reader);
        var final = agg.Snapshot(T0.AddMinutes(30));
        final.Should().ContainSingle().Which.Total.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SessionStartIsCapturedAtConstruction()
    {
        // Inject a fixed-time clock so the test is deterministic. The aggregator
        // should capture SessionStart at construction time.
        var t0 = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var agg = new StatsAggregator(() => t0);

        agg.SessionStart.Should().Be(t0);
    }

    [Fact]
    public void SessionStartIsCapturedAtConstructionWithDefaultClock()
    {
        // Default constructor uses DateTimeOffset.UtcNow. Confirm SessionStart
        // is set to roughly "now" — tolerance of a few seconds for slow CI hosts.
        var before = DateTimeOffset.UtcNow;
        var agg = new StatsAggregator();
        var after = DateTimeOffset.UtcNow;

        agg.SessionStart.Should().BeOnOrAfter(before);
        agg.SessionStart.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void RecordDoesNotChangeSessionStart()
    {
        var t0 = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var agg = new StatsAggregator(() => t0);

        agg.Record("Glory", 80, t0.AddSeconds(1));
        agg.Record("Glory", 20, t0.AddSeconds(2));

        agg.SessionStart.Should().Be(t0);
    }

    [Fact]
    public void ResetUpdatesSessionStart()
    {
        var t0 = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddMinutes(5);
        DateTimeOffset clockValue = t0;
        var agg = new StatsAggregator(() => clockValue);

        agg.Record("Glory", 80, t0);
        clockValue = t1;
        agg.Reset("Glory");

        agg.SessionStart.Should().Be(t1);
    }

    [Fact]
    public void ResetAllUpdatesSessionStart()
    {
        var t0 = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddMinutes(5);
        DateTimeOffset clockValue = t0;
        var agg = new StatsAggregator(() => clockValue);

        agg.Record("Glory", 80, t0);
        agg.Record("Standing", 5, t0);
        clockValue = t1;
        agg.ResetAll();

        agg.SessionStart.Should().Be(t1);
    }

    [Fact]
    public void ResetForUnknownLabelStillUpdatesSessionStart()
    {
        // Spec: "any reset drops the timer" — including a Reset on a counter
        // that has no events recorded yet (the dictionary lookup misses).
        // Otherwise, Reset on an empty row would be inconsistent with Reset
        // on a populated row.
        var t0 = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddSeconds(30);
        DateTimeOffset clockValue = t0;
        var agg = new StatsAggregator(() => clockValue);

        clockValue = t1;
        agg.Reset("NonexistentCounter");

        agg.SessionStart.Should().Be(t1);
    }
}
