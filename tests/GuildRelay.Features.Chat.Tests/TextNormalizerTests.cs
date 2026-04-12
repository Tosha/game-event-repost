using FluentAssertions;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class TextNormalizerTests
{
    [Fact]
    public void TrimsAndCollapsesWhitespace()
    {
        TextNormalizer.Normalize("  hello   world  ").Should().Be("hello world");
    }

    [Fact]
    public void LowercasesText()
    {
        TextNormalizer.Normalize("HELLO World").Should().Be("hello world");
    }

    [Fact]
    public void StripsCommonOcrNoiseCharacters()
    {
        TextNormalizer.Normalize("he|lo [world]").Should().Be("helo world");
    }

    [Fact]
    public void EmptyInputReturnsEmpty()
    {
        TextNormalizer.Normalize("").Should().BeEmpty();
        TextNormalizer.Normalize("   ").Should().BeEmpty();
    }
}
