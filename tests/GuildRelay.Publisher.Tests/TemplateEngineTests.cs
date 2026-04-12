using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.Core.Events;
using GuildRelay.Publisher;
using Xunit;

namespace GuildRelay.Publisher.Tests;

public class TemplateEngineTests
{
    private static DetectionEvent Make(string label, string matched) => new(
        FeatureId: "chat", RuleLabel: label, MatchedContent: matched,
        TimestampUtc: System.DateTimeOffset.UtcNow, PlayerName: "Tosh",
        Extras: new Dictionary<string, string> { ["region"] = "chat_top" },
        ImageAttachment: null);

    [Fact]
    public void KnownPlaceholdersAreSubstituted()
    {
        var engine = new TemplateEngine();
        var evt = Make("Incoming", "inc north");

        var output = engine.Render("**{player}** saw [{rule_label}]: `{matched_text}`", evt);

        output.Should().Be("**Tosh** saw [Incoming]: `inc north`");
    }

    [Fact]
    public void MissingPlaceholderRendersAsEmpty()
    {
        var engine = new TemplateEngine();
        var evt = Make("Incoming", "inc north");

        var output = engine.Render("{nonexistent}{player}", evt);

        output.Should().Be("Tosh");
    }

    [Fact]
    public void ExtrasAreSubstitutable()
    {
        var engine = new TemplateEngine();
        var evt = Make("Incoming", "inc north");

        var output = engine.Render("{player} in {region}", evt);

        output.Should().Be("Tosh in chat_top");
    }
}
