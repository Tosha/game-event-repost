using System;
using FluentAssertions;
using GuildRelay.Features.Audio;
using Xunit;

namespace GuildRelay.Features.Audio.Tests;

public class CooldownTrackerTests
{
    [Fact]
    public void FirstFireIsAlwaysAllowed()
    {
        var tracker = new CooldownTracker();
        tracker.TryFire("rule1", TimeSpan.FromSeconds(15)).Should().BeTrue();
    }

    [Fact]
    public void SecondFireWithinCooldownIsBlocked()
    {
        var tracker = new CooldownTracker();
        tracker.TryFire("rule1", TimeSpan.FromSeconds(15));
        tracker.TryFire("rule1", TimeSpan.FromSeconds(15)).Should().BeFalse();
    }

    [Fact]
    public void DifferentRulesHaveIndependentCooldowns()
    {
        var tracker = new CooldownTracker();
        tracker.TryFire("rule1", TimeSpan.FromSeconds(15));
        tracker.TryFire("rule2", TimeSpan.FromSeconds(15)).Should().BeTrue();
    }

    [Fact]
    public void FireIsAllowedAfterCooldownExpires()
    {
        var start = DateTimeOffset.UtcNow;
        var time = start;
        var tracker = new CooldownTracker(timeProvider: () => time);

        tracker.TryFire("rule1", TimeSpan.FromSeconds(1)).Should().BeTrue();
        tracker.TryFire("rule1", TimeSpan.FromSeconds(1)).Should().BeFalse();

        time = start.AddSeconds(2);
        tracker.TryFire("rule1", TimeSpan.FromSeconds(1)).Should().BeTrue();
    }

    [Fact]
    public void ResetClearsAllCooldowns()
    {
        var tracker = new CooldownTracker();
        tracker.TryFire("rule1", TimeSpan.FromSeconds(15));
        tracker.Reset();
        tracker.TryFire("rule1", TimeSpan.FromSeconds(15)).Should().BeTrue();
    }
}
