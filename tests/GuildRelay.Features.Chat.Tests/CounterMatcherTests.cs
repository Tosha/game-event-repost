using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.Core.Config;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class CounterMatcherTests
{
    private static CounterRule GloryRule() => new(
        Id: "g", Label: "Glory",
        Channels: new List<string> { "Game" },
        Pattern: "You gained {value} Glory.",
        MatchMode: CounterMatchMode.Template);

    [Fact]
    public void MatchesOnConfiguredChannel()
    {
        var matcher = new CounterMatcher(new[] { GloryRule() });
        var line = new ParsedChatLine("22:31:45", "Game", null, "You gained 80 Glory.");
        var result = matcher.Match(line);
        result.Should().NotBeNull();
        result!.Label.Should().Be("Glory");
        result.Value.Should().Be(80);
    }

    [Fact]
    public void DoesNotMatchOnOtherChannel()
    {
        var matcher = new CounterMatcher(new[] { GloryRule() });
        var line = new ParsedChatLine(null, "Say", "Bob", "You gained 80 Glory.");
        matcher.Match(line).Should().BeNull();
    }

    [Fact]
    public void ReturnsNullOnNoChannel()
    {
        var matcher = new CounterMatcher(new[] { GloryRule() });
        var line = new ParsedChatLine(null, null, null, "You gained 80 Glory.");
        matcher.Match(line).Should().BeNull();
    }
}
