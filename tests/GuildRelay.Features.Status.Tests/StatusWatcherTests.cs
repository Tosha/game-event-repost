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
using GuildRelay.Features.Chat.Preprocessing;
using GuildRelay.Features.Status;
using Xunit;

namespace GuildRelay.Features.Status.Tests;

public class StatusWatcherTests
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

    private static StatusWatcher CreateWatcher(FakeOcr ocr, EventBus bus)
    {
        var config = StatusConfig.Default with
        {
            Enabled = true,
            CaptureIntervalSec = 1,
            DebounceSamples = 1,
            Region = new RegionConfig(0, 0, 100, 100, 96,
                new ResolutionConfig(1920, 1080), "TEST")
        };
        return new StatusWatcher(
            new FakeCapture(),
            ocr,
            new PreprocessPipeline(Array.Empty<IPreprocessStage>()),
            bus,
            config,
            playerName: "Tosh");
    }

    [Fact]
    public async Task DisconnectTextTriggersDisconnectedEvent()
    {
        var ocr = new FakeOcr();
        var bus = new EventBus(capacity: 16);
        var watcher = CreateWatcher(ocr, bus);

        // First tick: clean → establishes Connected (silent)
        ocr.NextLines = new List<OcrLine> { new("Game running", 0.9f, RectangleF.Empty) };
        await watcher.StartAsync(CancellationToken.None);
        await Task.Delay(1500);

        // Second tick: disconnect text → fires transition
        ocr.NextLines = new List<OcrLine> { new("disconnected from server", 0.9f, RectangleF.Empty) };
        await Task.Delay(1500);

        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().ContainSingle()
            .Which.Should().Match<DetectionEvent>(e =>
                e.FeatureId == "status" &&
                e.RuleLabel == "disconnected" &&
                e.PlayerName == "Tosh");
    }

    [Fact]
    public async Task ReconnectAfterDisconnectEmitsReconnectedEvent()
    {
        var ocr = new FakeOcr();
        var bus = new EventBus(capacity: 16);
        var watcher = CreateWatcher(ocr, bus);

        // Establish Disconnected (silent from Unknown)
        ocr.NextLines = new List<OcrLine> { new("disconnected", 0.9f, RectangleF.Empty) };
        await watcher.StartAsync(CancellationToken.None);
        await Task.Delay(1500);

        // Reconnect
        ocr.NextLines = new List<OcrLine> { new("Game running", 0.9f, RectangleF.Empty) };
        await Task.Delay(1500);

        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().ContainSingle()
            .Which.RuleLabel.Should().Be("reconnected");
    }

    [Fact]
    public async Task FirstRunTransitionIsSilent()
    {
        var ocr = new FakeOcr();
        var bus = new EventBus(capacity: 16);
        var watcher = CreateWatcher(ocr, bus);

        // First observation establishes state silently
        ocr.NextLines = new List<OcrLine> { new("disconnected", 0.9f, RectangleF.Empty) };
        await watcher.StartAsync(CancellationToken.None);
        await Task.Delay(1500);

        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().BeEmpty("first-run transitions from Unknown are silent");
    }
}
