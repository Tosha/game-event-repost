using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GuildRelay.Core.Capture;
using GuildRelay.Core.Config;
using GuildRelay.Core.Events;
using GuildRelay.Core.Features;
using GuildRelay.Core.Ocr;
using GuildRelay.Features.Chat.Preprocessing;

namespace GuildRelay.Features.Chat;

public sealed class ChatWatcher : IFeature
{
    private readonly IScreenCapture _capture;
    private readonly IOcrEngine _ocr;
    private readonly PreprocessPipeline _pipeline;
    private readonly EventBus _bus;
    private readonly string _playerName;
    private readonly ChatDedup _dedup = new(capacity: 256);
    private readonly CooldownTracker _cooldown = new();
    private ChatConfig _config;
    private ChannelMatcher _matcher;
    private CancellationTokenSource? _cts;

    public ChatWatcher(
        IScreenCapture capture,
        IOcrEngine ocr,
        PreprocessPipeline pipeline,
        EventBus bus,
        ChatConfig config,
        string playerName)
    {
        _capture = capture;
        _ocr = ocr;
        _pipeline = pipeline;
        _bus = bus;
        _config = config;
        _playerName = playerName;
        _matcher = new ChannelMatcher(config.Rules);
    }

    public string Id => "chat";
    public string DisplayName => "Chat Watcher";
    public FeatureStatus Status { get; private set; } = FeatureStatus.Idle;

#pragma warning disable CS0067
    public event EventHandler<StatusChangedArgs>? StatusChanged;
#pragma warning restore CS0067

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _dedup.Clear();
        _cooldown.Reset();
        Status = FeatureStatus.Running;
        _ = Task.Run(() => CaptureLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        Status = FeatureStatus.Idle;
        return Task.CompletedTask;
    }

    public void ApplyConfig(JsonElement featureConfig)
    {
        var newConfig = featureConfig.Deserialize<ChatConfig>();
        if (newConfig is null) return;
        _config = newConfig;
        _matcher = new ChannelMatcher(newConfig.Rules);
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_config.CaptureIntervalMs));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await ProcessOneTickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Log and continue — don't let a single tick failure kill the loop.
            }
        }
    }

    private async Task ProcessOneTickAsync(CancellationToken ct)
    {
        if (_config.Region.IsEmpty) return;

        var rect = new Rectangle(_config.Region.X, _config.Region.Y,
            _config.Region.Width, _config.Region.Height);

        using var raw = _capture.CaptureRegion(rect);
        using var preprocessed = _pipeline.Apply(raw);

        var ocrResult = await _ocr.RecognizeAsync(
            preprocessed.BgraPixels,
            preprocessed.Width,
            preprocessed.Height,
            preprocessed.Stride,
            ct).ConfigureAwait(false);

        var matcher = _matcher; // snapshot

        var normalizedLines = new List<(string normalized, string original)>();
        foreach (var line in ocrResult.Lines)
        {
            if (line.Confidence < _config.OcrConfidenceThreshold)
                continue;
            var normalized = TextNormalizer.Normalize(line.Text);
            if (!string.IsNullOrEmpty(normalized))
                normalizedLines.Add((normalized, line.Text));
        }

        for (int i = 0; i < normalizedLines.Count; i++)
        {
            var (normalized, original) = normalizedLines[i];

            var joinedNormalized = normalized;
            var joinedOriginal = original;
            if (i + 1 < normalizedLines.Count)
            {
                joinedNormalized = normalized + " " + normalizedLines[i + 1].normalized;
                joinedOriginal = original + " " + normalizedLines[i + 1].original;
            }

            // Try joined first (catches split messages), then single line
            foreach (var candidate in new[] { (joinedNormalized, joinedOriginal), (normalized, original) })
            {
                if (_dedup.IsDuplicate(candidate.Item1))
                    continue;

                // Parse the channel from the OCR text, then route to matching rules
                var parsed = ChatLineParser.Parse(candidate.Item1);
                var match = matcher.FindMatch(parsed);
                if (match is null) continue;

                if (!_cooldown.TryFire(match.Rule.Id, TimeSpan.FromSeconds(match.Rule.CooldownSec)))
                    continue;

                var evt = new DetectionEvent(
                    FeatureId: "chat",
                    RuleLabel: match.Rule.Label,
                    MatchedContent: candidate.Item2,
                    TimestampUtc: DateTimeOffset.UtcNow,
                    PlayerName: _playerName,
                    Extras: new Dictionary<string, string>(),
                    ImageAttachment: null);

                await _bus.PublishAsync(evt, ct).ConfigureAwait(false);
                break;
            }
        }
    }
}
