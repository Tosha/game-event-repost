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
    public void HandlesOcrGarbledTimestamp()
    {
        // OCR misreads first digit of timestamp as apostrophe: '16:33:35 instead of 16:33:35
        var result = ChatLineParser.Parse("['16:33:35] [game] yourdrunkpapa has come");
        result.Channel.Should().Be("Game");
        result.Body.Should().Be("yourdrunkpapa has come");
    }

    [Fact]
    public void HandlesTimestampWithExtraChars()
    {
        // OCR reads various garbage in the timestamp field
        var result = ChatLineParser.Parse("[l6:33:35] [game] some event");
        result.Channel.Should().Be("Game");
        result.Body.Should().Be("some event");
    }

    [Fact]
    public void TwoUnknownTagsReturnsPlainText()
    {
        // Neither tag is a known channel
        var result = ChatLineParser.Parse("[foo] [bar] some text");
        result.Channel.Should().BeNull();
        result.Body.Should().Be("[foo] [bar] some text");
    }

    [Fact]
    public void HandlesNormalizedLowercaseInput()
    {
        var result = ChatLineParser.Parse("[nave] [stormbrew] theyl just remake");
        result.Channel.Should().Be("Nave");
        result.PlayerName.Should().Be("stormbrew");
        result.Body.Should().Be("theyl just remake");
    }

    [Fact]
    public void IsHeaderRecognizesChannelOnly()
    {
        ChatLineParser.IsHeader("[Nave] [Stormbrew] inc north").Should().BeTrue();
    }

    [Fact]
    public void IsHeaderRecognizesTimestampPlusChannel()
    {
        ChatLineParser.IsHeader("[21:02:15][Game] A large band of Profiteers").Should().BeTrue();
    }

    [Fact]
    public void IsHeaderRejectsPlainContinuation()
    {
        ChatLineParser.IsHeader("Plains of Meduli!").Should().BeFalse();
    }

    [Fact]
    public void IsHeaderRejectsUnknownBracketTag()
    {
        ChatLineParser.IsHeader("[Unknown] some text").Should().BeFalse();
    }
}
