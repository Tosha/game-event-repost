using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.Core.Config;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class CounterRuleCompilerTests
{
    private static CounterRule TemplateRule(string pattern) => new(
        Id: "r1", Label: "Glory",
        Channels: new List<string> { "Game" },
        Pattern: pattern, MatchMode: CounterMatchMode.Template);

    [Fact]
    public void TemplateExtractsIntegerValue()
    {
        var compiled = CounterRuleCompiler.Compile(TemplateRule("You gained {value} Glory."));
        var match = compiled.Match("You gained 80 Glory.");
        match.Success.Should().BeTrue();
        match.Value.Should().Be(80);
    }

    [Fact]
    public void TemplateExtractsDecimalValue()
    {
        var compiled = CounterRuleCompiler.Compile(TemplateRule("Mana regen {value}."));
        var match = compiled.Match("Mana regen 1.5.");
        match.Success.Should().BeTrue();
        match.Value.Should().Be(1.5);
    }

    [Fact]
    public void TemplateExtractsNegativeValue()
    {
        var compiled = CounterRuleCompiler.Compile(TemplateRule("Standing {value}."));
        var match = compiled.Match("Standing -5.");
        match.Success.Should().BeTrue();
        match.Value.Should().Be(-5);
    }

    [Fact]
    public void TemplateEscapesRegexMetaChars()
    {
        // Literal '.', '(', ')' must be escaped.
        var compiled = CounterRuleCompiler.Compile(TemplateRule("Mana (HP) {value}."));
        var match = compiled.Match("Mana (HP) 42.");
        match.Success.Should().BeTrue();
        match.Value.Should().Be(42);
    }

    [Fact]
    public void TemplateIsAnchored()
    {
        // Pattern is `^…$` — body must match in full, not just contain the pattern.
        var compiled = CounterRuleCompiler.Compile(TemplateRule("You gained {value} Glory."));
        compiled.Match("blah You gained 80 Glory. blah").Success.Should().BeFalse();
    }

    [Fact]
    public void TemplateIsCaseInsensitive()
    {
        var compiled = CounterRuleCompiler.Compile(TemplateRule("You gained {value} Glory."));
        compiled.Match("you gained 80 glory.").Success.Should().BeTrue();
    }

    [Fact]
    public void TemplateWithoutPlaceholderIsCountOnly()
    {
        var compiled = CounterRuleCompiler.Compile(TemplateRule("You died."));
        var match = compiled.Match("You died.");
        match.Success.Should().BeTrue();
        match.Value.Should().Be(1);
    }

    private static CounterRule RegexRule(string pattern) => new(
        Id: "r2", Label: "Glory",
        Channels: new List<string> { "Game" },
        Pattern: pattern, MatchMode: CounterMatchMode.Regex);

    [Fact]
    public void RegexModeExtractsValueFromNamedGroup()
    {
        var compiled = CounterRuleCompiler.Compile(
            RegexRule(@"^You gained (?<value>\d+) Glory\.?$"));
        var match = compiled.Match("You gained 80 Glory");
        match.Success.Should().BeTrue();
        match.Value.Should().Be(80);
    }

    [Fact]
    public void RegexModeWithoutValueGroupIsCountOnly()
    {
        var compiled = CounterRuleCompiler.Compile(RegexRule(@"^You died\.?$"));
        var match = compiled.Match("You died.");
        match.Success.Should().BeTrue();
        match.Value.Should().Be(1);
    }
}
