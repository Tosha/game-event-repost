using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Capture;
using GuildRelay.Core.Config;
using GuildRelay.Core.Events;
using GuildRelay.Core.Ocr;
using GuildRelay.Core.Preprocessing;
using GuildRelay.Core.Stats;
using GuildRelay.Features.Chat;
using GuildRelay.Features.Chat.Preprocessing;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class ChatWatcherStatsTests
{
    private sealed class FakeCapture : IScreenCapture
    {
        public CapturedFrame CaptureRegion(Rectangle rect)
            => new(new byte[4 * 4], 2, 2, 8);
    }

    private sealed class FakeOcr : IOcrEngine
    {
        public List<OcrLine> NextLines { get; set; } = new();
        public Task<OcrResult> RecognizeAsync(ReadOnlyMemory<byte> bgraPixels,
            int width, int height, int stride, CancellationToken ct)
            => Task.FromResult(new OcrResult(NextLines));
    }

    private static readonly CounterRule GloryCounter = new(
        Id: "g", Label: "Glory",
        Channels: new List<string> { "Game" },
        Pattern: "You gained {value} Glory.",
        MatchMode: CounterMatchMode.Template);

    private static ChatWatcher Build(FakeOcr ocr, EventBus bus, IStatsAggregator stats,
        bool statsEnabled, bool eventRepostEnabled,
        List<StructuredChatRule>? rules = null,
        List<CounterRule>? counters = null)
    {
        var config = ChatConfig.Default with
        {
            EventRepostEnabled = eventRepostEnabled,
            StatsEnabled = statsEnabled,
            CaptureIntervalSec = 1,
            OcrConfidenceThreshold = 0.5,
            Region = new RegionConfig(0, 0, 100, 100, 96,
                new ResolutionConfig(1920, 1080), "TEST"),
            Rules = rules ?? new List<StructuredChatRule>(),
            CounterRules = counters ?? new List<CounterRule> { GloryCounter }
        };
        return new ChatWatcher(
            new FakeCapture(), ocr,
            new PreprocessPipeline(Array.Empty<IPreprocessStage>()),
            bus, stats, config, "Tosh");
    }

    private static List<OcrLine> GloryLine() => new()
    {
        new("[22:31:45][Game] You gained 80 Glory.", 0.9f, RectangleF.Empty),
        new("[22:31:46][Game] terminator line.",      0.9f, RectangleF.Empty),
    };

    [Fact]
    public async Task RecordsCounterWhenStatsEnabled()
    {
        var stats = new StatsAggregator();
        var bus = new EventBus(capacity: 16);
        var ocr = new FakeOcr { NextLines = GloryLine() };
        var watcher = Build(ocr, bus, stats, statsEnabled: true, eventRepostEnabled: false);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(1500);
        await watcher.StopAsync();
        bus.Complete();

        var snap = stats.Snapshot(DateTimeOffset.UtcNow);
        snap.Should().ContainSingle()
            .Which.Total.Should().Be(80);
    }

    [Fact]
    public async Task DoesNotRecordWhenStatsDisabled()
    {
        var stats = new StatsAggregator();
        var bus = new EventBus(capacity: 16);
        var ocr = new FakeOcr { NextLines = GloryLine() };
        var watcher = Build(ocr, bus, stats, statsEnabled: false, eventRepostEnabled: true);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(1500);
        await watcher.StopAsync();
        bus.Complete();

        stats.Snapshot(DateTimeOffset.UtcNow).Should().BeEmpty();
    }

    [Fact]
    public async Task FiresBothPipelinesWhenLineMatchesBoth()
    {
        var stats = new StatsAggregator();
        var bus = new EventBus(capacity: 16);
        var ocr = new FakeOcr { NextLines = GloryLine() };

        // Event-repost rule that also matches the Glory line.
        var chatRule = new StructuredChatRule(
            Id: "c1", Label: "GameMessages",
            Channels: new List<string> { "Game" },
            Keywords: new List<string> { "Glory" },
            MatchMode: MatchMode.ContainsAny);

        var watcher = Build(ocr, bus, stats,
            statsEnabled: true, eventRepostEnabled: true,
            rules: new List<StructuredChatRule> { chatRule });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(1500);
        await watcher.StopAsync();
        bus.Complete();

        stats.Snapshot(DateTimeOffset.UtcNow).Should().ContainSingle()
            .Which.Total.Should().Be(80);
        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);
        events.Should().ContainSingle()
            .Which.RuleLabel.Should().Be("GameMessages");
    }

    [Fact]
    public async Task DedupedLinesDoNotDoubleCount()
    {
        var stats = new StatsAggregator();
        var bus = new EventBus(capacity: 16);
        // Both ticks present the same Glory line as the assembler's "in-progress" item.
        // The dedup layer should suppress the second emission.
        var ocr = new FakeOcr { NextLines = GloryLine() };
        var watcher = Build(ocr, bus, stats, statsEnabled: true, eventRepostEnabled: false);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await watcher.StartAsync(cts.Token);
        // Allow >= 2 capture ticks at 1s interval.
        await Task.Delay(2500);
        await watcher.StopAsync();
        bus.Complete();

        stats.Snapshot(DateTimeOffset.UtcNow).Should().ContainSingle()
            .Which.Total.Should().Be(80);
    }
}
