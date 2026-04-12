using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Events;
using GuildRelay.Logging;
using Xunit;

namespace GuildRelay.Logging.Tests;

public class EventLogTests
{
    private static DetectionEvent Sample(string label) => new(
        FeatureId: "chat", RuleLabel: label, MatchedContent: "inc north",
        TimestampUtc: System.DateTimeOffset.UtcNow, PlayerName: "Tosh",
        Extras: new Dictionary<string, string>(), ImageAttachment: null);

    private static string FreshDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "guildrelay-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task AppendWritesOneJsonLinePerEvent()
    {
        var dir = FreshDir();
        var log = new EventLog(dir);

        await log.AppendAsync(Sample("a"), EventPostStatus.Pending);
        await log.AppendAsync(Sample("b"), EventPostStatus.Success);

        var today = $"events-{System.DateTime.UtcNow:yyyyMMdd}.jsonl";
        var lines = File.ReadAllLines(Path.Combine(dir, today));
        lines.Should().HaveCount(2);
        lines.All(l => l.TrimStart().StartsWith("{")).Should().BeTrue();
    }

    [Fact]
    public async Task StatusUpdateAppendsStatusOnlyLine()
    {
        var dir = FreshDir();
        var log = new EventLog(dir);

        var evt = Sample("a");
        await log.AppendAsync(evt, EventPostStatus.Pending);
        await log.UpdateStatusAsync(evt, EventPostStatus.Success);

        var today = $"events-{System.DateTime.UtcNow:yyyyMMdd}.jsonl";
        var lines = File.ReadAllLines(Path.Combine(dir, today));
        lines.Should().HaveCount(2);
        lines[1].Should().Contain("\"status\":\"Success\"");
        lines[1].Should().Contain(evt.RuleLabel);
    }
}
