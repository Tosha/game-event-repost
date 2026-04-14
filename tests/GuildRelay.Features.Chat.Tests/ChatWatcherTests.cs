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
        List<StructuredChatRule> rules)
    {
        var config = ChatConfig.Default with
        {
            Enabled = true,
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
            config,
            playerName: "Tosh");
    }

    [Fact]
    public async Task MatchingLineEmitsDetectionEvent()
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

        // OCR line must include a channel tag for the parser to match
        ocr.NextLines = new List<OcrLine>
        {
            new("[Nave] [Someone] inc north gate", 0.9f, RectangleF.Empty)
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
                e.FeatureId == "chat" &&
                e.RuleLabel == "Incoming" &&
                e.PlayerName == "Tosh");
    }

    [Fact]
    public async Task DuplicateLinesAreNotReEmitted()
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
            new("[Nave] [Someone] inc north", 0.9f, RectangleF.Empty)
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(2500);
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().HaveCount(1, "same line should only emit once due to dedup");
    }

    [Fact]
    public async Task LowConfidenceLinesAreDropped()
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
            new("[Nave] inc north", 0.3f, RectangleF.Empty) // below 0.5 threshold
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(1500);
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().BeEmpty("low confidence lines should be silently dropped");
    }
}
