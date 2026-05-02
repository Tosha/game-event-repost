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

    /// <summary>
    /// OCR fake that returns one set of lines on the first capture and a different
    /// set on every subsequent capture. Used to simulate body-text OCR drift
    /// across ticks for the same chat content.
    /// </summary>
    private sealed class VaryingOcr : IOcrEngine
    {
        private readonly List<OcrLine> _firstTick;
        private readonly List<OcrLine> _laterTicks;
        public int Calls;

        public VaryingOcr(List<OcrLine> firstTick, List<OcrLine> laterTicks)
        {
            _firstTick = firstTick;
            _laterTicks = laterTicks;
        }

        public Task<OcrResult> RecognizeAsync(ReadOnlyMemory<byte> bgraPixels,
            int width, int height, int stride, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new OcrResult(Calls == 1 ? _firstTick : _laterTicks));
        }
    }

    private static readonly CounterRule GloryCounter = new(
        Id: "g", Label: "Glory",
        Channels: new List<string> { "Game" },
        Pattern: "You gained {value} Glory.",
        MatchMode: CounterMatchMode.Template);

    private static ChatWatcher Build(IOcrEngine ocr, EventBus bus, IStatsAggregator stats,
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

    [Fact]
    public async Task DoesNotDoubleCountWhenBodyOcrVariesButRegexStillMatches()
    {
        var stats = new StatsAggregator();
        var bus = new EventBus(capacity: 16);

        // Lenient regex rule (no anchors, no required trailing period) so OCR
        // variation in the body — like a dropped trailing period — does not
        // defeat the rule's match. This isolates the dedup behaviour from the
        // regex strictness.
        var lenientGlory = new CounterRule(
            Id: "g", Label: "Glory",
            Channels: new List<string> { "Game" },
            Pattern: @"you gained (?<value>\d+) glory",
            MatchMode: CounterMatchMode.Regex);

        // Same chat line, different OCR output across ticks: the trailing period
        // disappears on the second pass. Body-based chat dedup MISSES because
        // bodies differ. Counter dedup must catch this via structured fields
        // (channel, timestamp, label, value).
        var ocr = new VaryingOcr(
            firstTick: new()
            {
                new("[16:01:04][Game] You gained 80 Glory.", 0.9f, RectangleF.Empty),
                new("[16:01:05][Game] terminator line.",     0.9f, RectangleF.Empty),
            },
            laterTicks: new()
            {
                new("[16:01:04][Game] You gained 80 Glory",  0.9f, RectangleF.Empty),  // no period
                new("[16:01:05][Game] terminator line.",     0.9f, RectangleF.Empty),
            });

        var watcher = Build(ocr, bus, stats,
            statsEnabled: true, eventRepostEnabled: false,
            counters: new List<CounterRule> { lenientGlory });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(2500);
        await watcher.StopAsync();
        bus.Complete();

        ocr.Calls.Should().BeGreaterThanOrEqualTo(2,
            "the body-OCR-variation scenario only manifests when >=2 ticks fire");

        stats.Snapshot(DateTimeOffset.UtcNow).Should().ContainSingle()
            .Which.Total.Should().Be(80);
    }

    [Fact]
    public async Task RecordsTwoDistinctValuesAtSameInGameSecond()
    {
        var stats = new StatsAggregator();
        var bus = new EventBus(capacity: 16);

        // Two genuinely distinct events at the same chat-timestamp second (e.g.,
        // killed two enemies of different worth in the same second). The counter
        // dedup includes the value in its key, so 80 and 50 are distinguished.
        var ocr = new FakeOcr
        {
            NextLines = new()
            {
                new("[16:01:04][Game] You gained 80 Glory.", 0.9f, RectangleF.Empty),
                new("[16:01:04][Game] You gained 50 Glory.", 0.9f, RectangleF.Empty),
                new("[16:01:05][Game] terminator line.",     0.9f, RectangleF.Empty),
            }
        };

        var watcher = Build(ocr, bus, stats, statsEnabled: true, eventRepostEnabled: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(1500);
        await watcher.StopAsync();
        bus.Complete();

        stats.Snapshot(DateTimeOffset.UtcNow).Should().ContainSingle()
            .Which.Total.Should().Be(130);
    }

    [Fact]
    public async Task RecordsSameValueAtDifferentInGameSeconds()
    {
        var stats = new StatsAggregator();
        var bus = new EventBus(capacity: 16);

        // Same value, different timestamps — both must record. Confirms the
        // counter dedup includes the timestamp and doesn't suppress legitimate
        // repeats across distinct in-game seconds.
        var ocr = new FakeOcr
        {
            NextLines = new()
            {
                new("[16:01:04][Game] You gained 80 Glory.", 0.9f, RectangleF.Empty),
                new("[16:01:05][Game] You gained 80 Glory.", 0.9f, RectangleF.Empty),
                new("[16:01:06][Game] terminator line.",     0.9f, RectangleF.Empty),
            }
        };

        var watcher = Build(ocr, bus, stats, statsEnabled: true, eventRepostEnabled: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(1500);
        await watcher.StopAsync();
        bus.Complete();

        stats.Snapshot(DateTimeOffset.UtcNow).Should().ContainSingle()
            .Which.Total.Should().Be(160);
    }

    [Fact]
    public async Task RecordsBothAdjacentEventsWhenSecondIsTrailing()
    {
        var stats = new StatsAggregator();
        var bus = new EventBus(capacity: 16);

        // User-reported scenario: two adjacent in-game events with identical
        // value (22 Glory at 21:01:16 and 21:01:17), both visible on screen,
        // no terminator line below the second. The second event becomes the
        // deferred trailing in tick 1; the first event is the only Completed
        // message. Counter should record BOTH across tick 1 + tick 2.
        var ocr = new FakeOcr
        {
            NextLines = new()
            {
                new("[21:01:16][Game] You gained 22 Glory.", 0.9f, RectangleF.Empty),
                new("[21:01:17][Game] You gained 22 Glory.", 0.9f, RectangleF.Empty),
            }
        };

        var watcher = Build(ocr, bus, stats, statsEnabled: true, eventRepostEnabled: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await watcher.StartAsync(cts.Token);
        // Allow >= 2 capture ticks at 1s interval — the second event
        // is buffered as deferred trailing in tick 1, emitted in tick 2.
        await Task.Delay(2500);
        await watcher.StopAsync();
        bus.Complete();

        stats.Snapshot(DateTimeOffset.UtcNow).Should().ContainSingle()
            .Which.Total.Should().Be(44);
    }

    [Fact]
    public async Task EagerlyRecordsTrailingMessageWithinSingleTick()
    {
        var stats = new StatsAggregator();
        var bus = new EventBus(capacity: 16);

        // Same scenario as RecordsBothAdjacentEventsWhenSecondIsTrailing but
        // with a single-tick wait. With eager-emit, the counter should record
        // BOTH events within the first capture tick — no need to wait for the
        // deferred trailing to be confirmed in a later tick. This is the user-
        // reported bug: with the default 5-second capture interval, waiting
        // for tick 2 to register the second event feels broken.
        var ocr = new FakeOcr
        {
            NextLines = new()
            {
                new("[21:01:16][Game] You gained 22 Glory.", 0.9f, RectangleF.Empty),
                new("[21:01:17][Game] You gained 22 Glory.", 0.9f, RectangleF.Empty),
            }
        };

        var watcher = Build(ocr, bus, stats, statsEnabled: true, eventRepostEnabled: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(1500);  // a single tick at 1s interval
        await watcher.StopAsync();
        bus.Complete();

        stats.Snapshot(DateTimeOffset.UtcNow).Should().ContainSingle()
            .Which.Total.Should().Be(44);
    }
}
