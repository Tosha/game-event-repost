using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.Core.Config;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class RuleEditorLogicTests
{
    [Fact]
    public void BuildRuleTrimsAndDefaultsLabel()
    {
        var rule = RuleEditorLogic.BuildRule(
            label: "   ",
            selectedChannels: new[] { "Nave" },
            keywordsText: "x",
            matchMode: MatchMode.ContainsAny,
            cooldownSec: 60);

        rule.Label.Should().Be("Untitled");
        rule.Id.Should().Be("untitled");
    }

    [Fact]
    public void BuildRuleSplitsCommaSeparatedKeywordsForContainsAnyMode()
    {
        var rule = RuleEditorLogic.BuildRule(
            label: "Incoming",
            selectedChannels: new[] { "Nave" },
            keywordsText: "inc, incoming, enemies ",
            matchMode: MatchMode.ContainsAny,
            cooldownSec: 60);

        rule.Keywords.Should().Equal("inc", "incoming", "enemies");
    }

    [Fact]
    public void BuildRuleKeepsEntireTextAsSingleRegex()
    {
        var rule = RuleEditorLogic.BuildRule(
            label: "RegexRule",
            selectedChannels: new[] { "Game" },
            keywordsText: "(plains|sylvan), of meduli",
            matchMode: MatchMode.Regex,
            cooldownSec: 60);

        rule.Keywords.Should().ContainSingle()
            .Which.Should().Be("(plains|sylvan), of meduli");
    }

    [Fact]
    public void BuildRuleProducesIdFromLabel()
    {
        var rule = RuleEditorLogic.BuildRule(
            label: "My Rule",
            selectedChannels: new[] { "Nave" },
            keywordsText: "x",
            matchMode: MatchMode.ContainsAny,
            cooldownSec: 60);

        rule.Id.Should().Be("my_rule");
    }

    [Fact]
    public void BuildRulePreservesEmptyChannels()
    {
        var rule = RuleEditorLogic.BuildRule(
            label: "Wildcard",
            selectedChannels: System.Array.Empty<string>(),
            keywordsText: "hello",
            matchMode: MatchMode.ContainsAny,
            cooldownSec: 60);

        rule.Channels.Should().BeEmpty();
        rule.Keywords.Should().Equal("hello");
    }

    [Fact]
    public void BuildRuleEmptyKeywordsTextProducesEmptyList()
    {
        var rule = RuleEditorLogic.BuildRule(
            label: "MatchAll",
            selectedChannels: System.Array.Empty<string>(),
            keywordsText: "   ",
            matchMode: MatchMode.ContainsAny,
            cooldownSec: 60);

        rule.Keywords.Should().BeEmpty();
    }

    [Fact]
    public void BuildRulePassesThroughCooldown()
    {
        var rule = RuleEditorLogic.BuildRule(
            label: "x",
            selectedChannels: new[] { "Nave" },
            keywordsText: "x",
            matchMode: MatchMode.ContainsAny,
            cooldownSec: 42);

        rule.CooldownSec.Should().Be(42);
    }
}
