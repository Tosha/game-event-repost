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
    public void StripsPipesAndCurlyBraces()
    {
        TextNormalizer.Normalize("he|lo {world}").Should().Be("helo world");
    }

    [Fact]
    public void PreservesSquareBrackets()
    {
        // Square brackets are meaningful in MO2 chat: [Game], [Nave], [20:27:33]
        TextNormalizer.Normalize("[Game] Dire Wolf").Should().Be("[game] dire wolf");
        TextNormalizer.Normalize("[20:27:33][Game] task").Should().Be("[20:27:33][game] task");
    }

    [Fact]
    public void ReplacesBulletCharactersWithSpaces()
    {
        // OCR frequently reads bullet dots between words in MO2 chat.
        // Replace with space (not strip) to preserve word boundaries.
        TextNormalizer.Normalize("of\u2022profiteers has").Should().Be("of profiteers has");
        TextNormalizer.Normalize("\u2022plains of\u2022meduli").Should().Be("plains of meduli");
    }

    [Fact]
    public void EmptyInputReturnsEmpty()
    {
        TextNormalizer.Normalize("").Should().BeEmpty();
        TextNormalizer.Normalize("   ").Should().BeEmpty();
    }
}
