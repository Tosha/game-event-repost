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

/// <summary>
/// Diagnostic data emitted each tick for the debug live view.
/// </summary>
public sealed class ChatTickDebugInfo
{
    public byte[] CapturedImageBgra { get; init; } = Array.Empty<byte>();
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    public int ImageStride { get; init; }
    public List<string> OcrLines { get; init; } = new();
    public List<string> NormalizedLines { get; init; } = new();
    public List<string> ParsedChannels { get; init; } = new();
    public List<string> MatchResults { get; init; } = new();
    public DateTimeOffset Timestamp { get; init; }
}

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

    /// <summary>Fired each tick with diagnostic info. Subscribe from the debug window.</summary>
    public event Action<ChatTickDebugInfo>? DebugTick;

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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_config.CaptureIntervalSec));
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
                // Log and continue
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

        var matcher = _matcher;

        // Build debug info
        var debug = DebugTick is not null ? new ChatTickDebugInfo
        {
            CapturedImageBgra = (byte[])raw.BgraPixels.Clone(),
            ImageWidth = raw.Width,
            ImageHeight = raw.Height,
            ImageStride = raw.Stride,
            Timestamp = DateTimeOffset.UtcNow
        } : null;

        var normalizedLines = new List<(string normalized, string original)>();
        foreach (var line in ocrResult.Lines)
        {
            if (line.Confidence < _config.OcrConfidenceThreshold)
                continue;
            var normalized = TextNormalizer.Normalize(line.Text);
            if (!string.IsNullOrEmpty(normalized))
            {
                normalizedLines.Add((normalized, line.Text));
                debug?.OcrLines.Add(line.Text);
                debug?.NormalizedLines.Add(normalized);
                var parsed = ChatLineParser.Parse(normalized);
                debug?.ParsedChannels.Add(parsed.Channel ?? "—");
            }
        }

        for (int i = 0; i < normalizedLines.Count; i++)
        {
            var (normalized, original) = normalizedLines[i];

            // Build candidates: try joining up to 3 consecutive lines (longest first)
            // to handle OCR splitting long [Game] messages across multiple lines.
            var candidates = new List<(string normalized, string original)>();

            // 3-line join
            if (i + 2 < normalizedLines.Count)
            {
                candidates.Add((
                    normalized + " " + normalizedLines[i + 1].normalized + " " + normalizedLines[i + 2].normalized,
                    original + " " + normalizedLines[i + 1].original + " " + normalizedLines[i + 2].original));
            }
            // 2-line join
            if (i + 1 < normalizedLines.Count)
            {
                candidates.Add((
                    normalized + " " + normalizedLines[i + 1].normalized,
                    original + " " + normalizedLines[i + 1].original));
            }
            // Single line
            candidates.Add((normalized, original));

            foreach (var candidate in candidates)
            {
                if (_dedup.IsDuplicate(candidate.Item1))
                {
                    debug?.MatchResults.Add($"DEDUP: {candidate.Item2}");
                    continue;
                }

                var parsed = ChatLineParser.Parse(candidate.Item1);
                var match = matcher.FindMatch(parsed);
                if (match is null) continue;

                if (!_cooldown.TryFire(match.Rule.Id, TimeSpan.FromSeconds(match.Rule.CooldownSec)))
                {
                    debug?.MatchResults.Add($"COOLDOWN: [{match.Rule.Label}] {candidate.Item2}");
                    continue;
                }

                debug?.MatchResults.Add($"POSTED: [{match.Rule.Label}] {candidate.Item2}");

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

        if (debug is not null && normalizedLines.Count > 0)
            DebugTick?.Invoke(debug);
    }
}
