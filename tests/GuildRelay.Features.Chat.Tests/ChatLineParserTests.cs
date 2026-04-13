using FluentAssertions;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class ChatLineParserTests
{
    [Fact]
    public void ParsesChannelWithTimestamp()
    {
        var result = ChatLineParser.Parse("[21:02:15][Game] A large band of Profiteers");
        result.Timestamp.Should().Be("21:02:15");
        result.Channel.Should().Be("Game");
        result.PlayerName.Should().BeNull();
        result.Body.Should().Be("A large band of Profiteers");
    }

    [Fact]
    public void ParsesChannelWithoutTimestamp()
    {
        var result = ChatLineParser.Parse("[Nave] [Stormbrew] they should just add it");
        result.Timestamp.Should().BeNull();
        result.Channel.Should().Be("Nave");
        result.PlayerName.Should().Be("Stormbrew");
        result.Body.Should().Be("they should just add it");
    }

    [Fact]
    public void ParsesChannelCaseInsensitive()
    {
        var result = ChatLineParser.Parse("[game] You received a task");
        result.Channel.Should().Be("Game");
        result.Body.Should().Be("You received a task");
    }

    [Fact]
    public void HandlesOcrArtifactDotBeforeChannel()
    {
        var result = ChatLineParser.Parse("[.Game] YourdrunkPapa has come online.");
        result.Channel.Should().Be("Game");
    }

    [Fact]
    public void ReturnsNullChannelForUnrecognizedTag()
    {
        var result = ChatLineParser.Parse("[Unknown] some text");
        result.Channel.Should().BeNull();
        result.Body.Should().Be("[Unknown] some text");
    }

    [Fact]
    public void ReturnsNullChannelForPlainText()
    {
        var result = ChatLineParser.Parse("just some text without brackets");
        result.Channel.Should().BeNull();
        result.Body.Should().Be("just some text without brackets");
    }

    [Fact]
    public void ParsesGuildChannelWithPlayer()
    {
        var result = ChatLineParser.Parse("[Guild] [PlayerOne] heading to dungeon");
        result.Channel.Should().Be("Guild");
        result.PlayerName.Should().Be("PlayerOne");
        result.Body.Should().Be("heading to dungeon");
    }

    [Fact]
    public void ParsesTimestampChannelPlayer()
    {
        var result = ChatLineParser.Parse("[21:03:40][Game] YourdrunkPapa has come online.");
        result.Timestamp.Should().Be("21:03:40");
        result.Channel.Should().Be("Game");
        result.Body.Should().Be("YourdrunkPapa has come online.");
    }

    [Fact]
    public void HandlesNormalizedLowercaseInput()
    {
        var result = ChatLineParser.Parse("[nave] [stormbrew] theyl just remake");
        result.Channel.Should().Be("Nave");
        result.PlayerName.Should().Be("stormbrew");
        result.Body.Should().Be("theyl just remake");
    }
}
