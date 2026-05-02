using System;
using System.Collections.Generic;
using System.Drawing;
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

public class ChatWatcherTests
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

    private static ChatWatcher CreateWatcher(
        FakeOcr ocr,
        EventBus bus,
        List<StructuredChatRule> rules,
        IStatsAggregator? stats = null)
    {
        var config = ChatConfig.Default with
        {
            EventRepostEnabled = true,
            StatsEnabled = false,
            CaptureIntervalSec = 1,
            OcrConfidenceThreshold = 0.5,
            Region = new RegionConfig(0, 0, 100, 100, 96,
                new ResolutionConfig(1920, 1080), "TEST"),
            Rules = rules
        };
        return new ChatWatcher(
            new FakeCapture(),
            ocr,
            new PreprocessPipeline(Array.Empty<IPreprocessStage>()),
            bus,
            stats ?? new StatsAggregator(),
            config,
            playerName: "Tosh");
    }

    [Fact]
    public async Task MatchingCompletedMessageEmitsDetectionEvent()
    {
        var ocr = new FakeOcr();
        var bus = new EventBus(capacity: 16);
        var rules = new List<StructuredChatRule>
        {
            new("r1", "Incoming",
                new List<string> { "Nave", "Yell" },
                new List<string> { "(inc|incoming)" },
                MatchMode.Regex)
        };
        var watcher = CreateWatcher(ocr, bus, rules);

        // Two lines: the first is the message we want to match, the second
        // acts as a terminator so the first becomes a Completed message.
        ocr.NextLines = new List<OcrLine>
        {
            new("[Nave] [Someone] inc north gate", 0.9f, RectangleF.Empty),
            new("[Nave] [Other]    status ok",      0.9f, RectangleF.Empty),
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(1500);
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().ContainSingle()
            .Which.Should().Match<DetectionEvent>(e =>
                e.FeatureId == "chat" && e.RuleLabel == "Incoming" && e.PlayerName == "Tosh");
    }

    [Fact]
    public async Task WrappedTwoLineGameMessageIsMatchedAfterAssembly()
    {
        var ocr = new FakeOcr();
        var bus = new EventBus(capacity: 16);
        var rules = new List<StructuredChatRule>
        {
            new("r1", "World Event",
                new List<string> { "Game" },
                new List<string> { "plains of meduli" },
                MatchMode.ContainsAny)
        };
        var watcher = CreateWatcher(ocr, bus, rules);

        // First line is the [Game] header + start of body; second line is the
        // continuation. Third line is a terminator so the wrapped message completes.
        ocr.NextLines = new List<OcrLine>
        {
            new("[21:02:15][Game] A large band of Profiteers has been", 0.9f, RectangleF.Empty),
            new("seen pillaging the Plains of Meduli!",                 0.9f, RectangleF.Empty),
            new("[Nave] [Tosh] nothing to see",                         0.9f, RectangleF.Empty),
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(1500);
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().ContainSingle()
            .Which.RuleLabel.Should().Be("World Event");
    }

    [Fact]
    public async Task LastMessageIsDeferredOneTickThenEmitted()
    {
        var ocr = new FakeOcr();
        var bus = new EventBus(capacity: 16);
        var rules = new List<StructuredChatRule>
        {
            new("r1", "Incoming",
                new List<string> { "Nave" },
                new List<string> { "inc" },
                MatchMode.ContainsAny)
        };
        var watcher = CreateWatcher(ocr, bus, rules);

        // Only one line - it's trailing with no terminator, so tick 1 emits
        // nothing. On tick 2 (same OCR), it is emitted from the deferred buffer.
        ocr.NextLines = new List<OcrLine>
        {
            new("[Nave] [Someone] inc north", 0.9f, RectangleF.Empty),
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(2500);  // ~2 ticks at 1s interval
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().ContainSingle("the deferred trailing message should fire once on the second tick");
    }

    [Fact]
    public async Task DuplicateCompletedMessagesEmitOnlyOnce()
    {
        var ocr = new FakeOcr();
        var bus = new EventBus(capacity: 16);
        var rules = new List<StructuredChatRule>
        {
            new("r1", "Incoming",
                new List<string> { "Nave" },
                new List<string> { "inc" },
                MatchMode.ContainsAny)
        };
        var watcher = CreateWatcher(ocr, bus, rules);

        ocr.NextLines = new List<OcrLine>
        {
            new("[Nave] [Someone] inc north",       0.9f, RectangleF.Empty),
            new("[Nave] [Other]    terminator ok",  0.9f, RectangleF.Empty),
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(2500);  // ~2 ticks
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().ContainSingle("the same message on two consecutive ticks dedups to one event");
    }

    [Fact]
    public async Task LowConfidenceHeaderDropsMessage()
    {
        var ocr = new FakeOcr();
        var bus = new EventBus(capacity: 16);
        var rules = new List<StructuredChatRule>
        {
            new("r1", "Incoming",
                new List<string> { "Nave" },
                new List<string> { "inc" },
                MatchMode.ContainsAny)
        };
        var watcher = CreateWatcher(ocr, bus, rules);

        ocr.NextLines = new List<OcrLine>
        {
            new("[Nave] inc north", 0.3f, RectangleF.Empty), // below 0.5 threshold
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(2000);
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().BeEmpty("low confidence headers are dropped before assembly");
    }
}
