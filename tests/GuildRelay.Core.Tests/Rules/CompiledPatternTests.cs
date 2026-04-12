using FluentAssertions;
using GuildRelay.Core.Rules;
using Xunit;

namespace GuildRelay.Core.Tests.Rules;

public class CompiledPatternTests
{
    [Fact]
    public void LiteralPatternMatchesCaseInsensitive()
    {
        var pattern = CompiledPattern.Create("zerg", isRegex: false);
        pattern.IsMatch("I saw a ZERG coming").Should().BeTrue();
    }

    [Fact]
    public void LiteralPatternDoesNotMatchAbsentText()
    {
        var pattern = CompiledPattern.Create("zerg", isRegex: false);
        pattern.IsMatch("everything is fine").Should().BeFalse();
    }

    [Fact]
    public void RegexPatternMatchesGroups()
    {
        var pattern = CompiledPattern.Create("(inc|incoming|enemies)", isRegex: true);
        pattern.IsMatch("inc north gate").Should().BeTrue();
        pattern.IsMatch("incoming from east").Should().BeTrue();
        pattern.IsMatch("all clear").Should().BeFalse();
    }

    [Fact]
    public void RegexPatternIsCaseInsensitive()
    {
        var pattern = CompiledPattern.Create("zerg", isRegex: true);
        pattern.IsMatch("ZERG spotted").Should().BeTrue();
    }
}
