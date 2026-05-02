using System;
using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.App.Stats;
using GuildRelay.Core.Config;
using GuildRelay.Core.Stats;
using Xunit;

namespace GuildRelay.App.Tests.Stats;

public class StatsViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private static CounterRule Glory() => new(
        Id: "g", Label: "Glory",
        Channels: new List<string> { "Game" },
        Pattern: "You gained {value} Glory.",
        MatchMode: CounterMatchMode.Template);

    [Fact]
    public void RefreshBuildsRowsFromAggregatorSnapshot()
    {
        var agg = new StatsAggregator();
        agg.Record("Glory", 80, T0);

        var vm = new StatsViewModel(agg, () => T0, () => new[] { Glory() }, () => true);
        vm.Refresh();

        vm.Rows.Should().ContainSingle();
        vm.Rows[0].Label.Should().Be("Glory");
        vm.Rows[0].Total.Should().Be(80);
        vm.Rows[0].Last60Min.Should().Be(80);
    }

    [Fact]
    public void BadgeReflectsStatsEnabledFlag()
    {
        var agg = new StatsAggregator();
        var enabled = true;
        var vm = new StatsViewModel(agg, () => T0, () => System.Array.Empty<CounterRule>(), () => enabled);

        vm.Refresh();
        vm.BadgeState.Should().Be("Stats: ON");

        enabled = false;
        vm.Refresh();
        vm.BadgeState.Should().Be("Stats: OFF");
    }

    [Fact]
    public void RulesWithoutEventsRenderAsZeroRows()
    {
        var agg = new StatsAggregator();
        var vm = new StatsViewModel(agg, () => T0, () => new[] { Glory() }, () => true);

        vm.Refresh();

        vm.Rows.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new CounterRowVm("Glory", 0, 0));
        vm.HasNoRules.Should().BeFalse();
    }

    [Fact]
    public void NoRulesAndNoEventsSetsHasNoRules()
    {
        var agg = new StatsAggregator();
        var vm = new StatsViewModel(agg, () => T0, () => System.Array.Empty<CounterRule>(), () => true);

        vm.Refresh();

        vm.Rows.Should().BeEmpty();
        vm.HasNoRules.Should().BeTrue();
    }

    [Fact]
    public void ResetCounterCallsAggregator()
    {
        var agg = new StatsAggregator();
        agg.Record("Glory", 80, T0);

        var vm = new StatsViewModel(agg, () => T0, () => new[] { Glory() }, () => true);
        vm.ResetCounter("Glory");
        vm.Refresh();

        vm.Rows[0].Total.Should().Be(0);
    }

    [Fact]
    public void ResetAllClearsAllCounters()
    {
        var agg = new StatsAggregator();
        agg.Record("Glory", 80, T0);
        agg.Record("Standing", 5, T0);

        var vm = new StatsViewModel(agg, () => T0, () => new[] { Glory() }, () => true);
        vm.ResetAll();
        vm.Refresh();

        vm.Rows.Should().AllSatisfy(r => r.Total.Should().Be(0));
    }

    [Fact]
    public void OrphanCounterWithDataAppearsAfterRuleIsRemoved()
    {
        var agg = new StatsAggregator();
        agg.Record("Glory", 80, T0);

        // Rules list is now empty — simulates the user deleting the rule after data was recorded.
        var vm = new StatsViewModel(agg, () => T0, () => System.Array.Empty<CounterRule>(), () => true);
        vm.Refresh();

        vm.Rows.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new CounterRowVm("Glory", 80, 80));
        vm.HasNoRules.Should().BeFalse();
    }

    [Fact]
    public void SessionElapsedTextIsZeroAtConstruction()
    {
        var clockValue = T0;
        var agg = new StatsAggregator(() => clockValue);
        var vm = new StatsViewModel(agg, () => clockValue, () => System.Array.Empty<CounterRule>(), () => true);

        vm.Refresh();

        vm.SessionElapsedText.Should().Be("0:00");
    }

    [Fact]
    public void SessionElapsedTextFormatsSubHourAsMinutesSeconds()
    {
        var clockValue = T0;
        var agg = new StatsAggregator(() => clockValue);
        clockValue = T0.AddMinutes(5).AddSeconds(23);
        var vm = new StatsViewModel(agg, () => clockValue, () => System.Array.Empty<CounterRule>(), () => true);

        vm.Refresh();

        vm.SessionElapsedText.Should().Be("5:23");
    }

    [Fact]
    public void SessionElapsedTextFormatsOverHourAsHoursMinutesSeconds()
    {
        var clockValue = T0;
        var agg = new StatsAggregator(() => clockValue);
        clockValue = T0.AddHours(1).AddMinutes(5).AddSeconds(23);
        var vm = new StatsViewModel(agg, () => clockValue, () => System.Array.Empty<CounterRule>(), () => true);

        vm.Refresh();

        vm.SessionElapsedText.Should().Be("1:05:23");
    }

    [Fact]
    public void SessionElapsedTextClampsToZeroWhenNegative()
    {
        // Pathological: the VM clock is somehow earlier than the aggregator's
        // SessionStart (e.g., NTP jump backward). Should not display a negative
        // duration; clamp to 0:00 for sanity.
        var clockValue = T0;
        var agg = new StatsAggregator(() => clockValue);
        clockValue = T0.AddSeconds(-5);
        var vm = new StatsViewModel(agg, () => clockValue, () => System.Array.Empty<CounterRule>(), () => true);

        vm.Refresh();

        vm.SessionElapsedText.Should().Be("0:00");
    }

    [Fact]
    public void SessionElapsedTextResetsAfterResetAll()
    {
        DateTimeOffset clockValue = T0;
        var agg = new StatsAggregator(() => clockValue);

        // Five minutes of session.
        clockValue = T0.AddMinutes(5);
        var vm = new StatsViewModel(agg, () => clockValue, () => System.Array.Empty<CounterRule>(), () => true);
        vm.Refresh();
        vm.SessionElapsedText.Should().Be("5:00");

        // ResetAll under a clock that's now 10 minutes after T0. SessionStart
        // jumps to 10:00. Refresh under same clock → elapsed = 0.
        clockValue = T0.AddMinutes(10);
        vm.ResetAll();
        vm.Refresh();

        vm.SessionElapsedText.Should().Be("0:00");
    }
}
