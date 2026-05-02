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
}
