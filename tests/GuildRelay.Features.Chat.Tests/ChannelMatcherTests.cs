using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.Core.Config;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class ChannelMatcherTests
{
    [Fact]
    public void MatchesChannelAndKeyword()
    {
        var rule = new StructuredChatRule("r1", "Game Events",
            new List<string> { "Game" },
            new List<string> { "Dire Wolf" },
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine("21:07:35", "Game",
            null, "You received a task to kill Dire Wolf (8)");

        var match = matcher.FindMatch(parsed);
        match.Should().NotBeNull();
        match!.Rule.Id.Should().Be("r1");
    }

    [Fact]
    public void DoesNotMatchWrongChannel()
    {
        var rule = new StructuredChatRule("r1", "Game Events",
            new List<string> { "Game" },
            new List<string> { "Dire Wolf" },
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, "Nave",
            "Stormbrew", "Dire Wolf spotted north");

        matcher.FindMatch(parsed).Should().BeNull();
    }

    [Fact]
    public void MatchesAnyKeywordInList()
    {
        var rule = new StructuredChatRule("r1", "Game Events",
            new List<string> { "Game" },
            new List<string> { "Sylvan Sanctum", "Dire Wolf" },
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, "Game",
            null, "A large band pillaging the Sylvan Sanctum!");

        matcher.FindMatch(parsed).Should().NotBeNull();
    }

    [Fact]
    public void EmptyKeywordsMatchesAllOnChannel()
    {
        var rule = new StructuredChatRule("r1", "Guild Relay",
            new List<string> { "Guild" },
            new List<string>(),
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, "Guild",
            "PlayerOne", "anything at all");

        matcher.FindMatch(parsed).Should().NotBeNull();
    }

    [Fact]
    public void RegexModeMatchesPattern()
    {
        var rule = new StructuredChatRule("r1", "Incoming",
            new List<string> { "Nave", "Yell" },
            new List<string> { "(inc|incoming|enemies)" },
            MatchMode.Regex);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, "Nave",
            "Stormbrew", "inc north gate");

        matcher.FindMatch(parsed).Should().NotBeNull();
    }

    [Fact]
    public void KeywordMatchIsCaseInsensitive()
    {
        var rule = new StructuredChatRule("r1", "Game Events",
            new List<string> { "Game" },
            new List<string> { "Dire Wolf" },
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, "Game",
            null, "you received a task to kill dire wolf");

        matcher.FindMatch(parsed).Should().NotBeNull();
    }

    [Fact]
    public void NullChannelDoesNotMatch()
    {
        var rule = new StructuredChatRule("r1", "Game",
            new List<string> { "Game" },
            new List<string>(),
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, null, null, "some unparseable text");

        matcher.FindMatch(parsed).Should().BeNull();
    }

    [Fact]
    public void EmptyChannelsRuleMatchesAnyChannel()
    {
        var rule = new StructuredChatRule("r1", "Wildcard",
            new List<string>(),                       // no channels = wildcard
            new List<string> { "hello" },
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, "Nave", "Tosh", "hello world");

        var match = matcher.FindMatch(parsed);
        match.Should().NotBeNull();
        match!.Rule.Id.Should().Be("r1");
    }

    [Fact]
    public void EmptyChannelsRuleStillRequiresKeywordMatch()
    {
        var rule = new StructuredChatRule("r1", "Wildcard",
            new List<string>(),
            new List<string> { "never" },
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, "Nave", "Tosh", "hello world");

        matcher.FindMatch(parsed).Should().BeNull();
    }

    [Fact]
    public void EmptyChannelsAndEmptyKeywordsMatchesEveryChannel()
    {
        var rule = new StructuredChatRule("r1", "MatchAll",
            new List<string>(),
            new List<string>(),
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, "Game", null, "anything goes here");

        matcher.FindMatch(parsed).Should().NotBeNull();
    }

    [Fact]
    public void ChannelSpecificRuleWinsOverWildcard()
    {
        var specific = new StructuredChatRule("specific", "NaveOnly",
            new List<string> { "Nave" },
            new List<string> { "x" },
            MatchMode.ContainsAny);
        var wildcard = new StructuredChatRule("wild", "Wildcard",
            new List<string>(),
            new List<string> { "x" },
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { specific, wildcard });

        var parsed = new ParsedChatLine(null, "Nave", "Tosh", "x");

        var match = matcher.FindMatch(parsed);
        match.Should().NotBeNull();
        match!.Rule.Id.Should().Be("specific");
    }
}
