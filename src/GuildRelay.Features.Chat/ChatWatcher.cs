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
using GuildRelay.Core.Stats;
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
    public List<string> LineRoles { get; init; } = new();  // NEW: "HEADER" | "CONT" | "SKIP"
    public List<string> MatchResults { get; init; } = new();
    public DateTimeOffset Timestamp { get; init; }
}

public sealed class ChatWatcher : IFeature
{
    private readonly IScreenCapture _capture;
    private readonly IOcrEngine _ocr;
    private readonly PreprocessPipeline _pipeline;
    private readonly EventBus _bus;
    private readonly IStatsAggregator _stats;
    private readonly string _playerName;
    private readonly ChatDedup _dedup = new(capacity: 256);
    private readonly CooldownTracker _cooldown = new();
    private AssembledMessage? _deferredTrailing;
    private ChatConfig _config;
    private ChannelMatcher _matcher;
    private CounterMatcher _counterMatcher;
    private CancellationTokenSource? _cts;

    public ChatWatcher(
        IScreenCapture capture,
        IOcrEngine ocr,
        PreprocessPipeline pipeline,
        EventBus bus,
        IStatsAggregator stats,
        ChatConfig config,
        string playerName)
    {
        _capture = capture;
        _ocr = ocr;
        _pipeline = pipeline;
        _bus = bus;
        _stats = stats;
        _config = config;
        _playerName = playerName;
        _matcher = new ChannelMatcher(config.Rules);
        _counterMatcher = new CounterMatcher(config.CounterRules);
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
        _deferredTrailing = null;
        Status = FeatureStatus.Running;
        _ = Task.Run(() => CaptureLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _deferredTrailing = null;
        Status = FeatureStatus.Idle;
        return Task.CompletedTask;
    }

    public void ApplyConfig(JsonElement featureConfig)
    {
        var newConfig = featureConfig.Deserialize<ChatConfig>();
        if (newConfig is null) return;
        _config = newConfig;
        _matcher = new ChannelMatcher(newConfig.Rules);
        _counterMatcher = new CounterMatcher(newConfig.CounterRules);
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
        var counterMatcher = _counterMatcher;

        var debug = DebugTick is not null ? new ChatTickDebugInfo
        {
            CapturedImageBgra = (byte[])raw.BgraPixels.Clone(),
            ImageWidth = raw.Width,
            ImageHeight = raw.Height,
            ImageStride = raw.Stride,
            Timestamp = DateTimeOffset.UtcNow
        } : null;

        // Build assembler input: normalize each OCR line.
        var inputs = new List<OcrLineInput>(ocrResult.Lines.Count);
        foreach (var line in ocrResult.Lines)
        {
            var normalized = TextNormalizer.Normalize(line.Text);
            if (string.IsNullOrEmpty(normalized)) continue;
            inputs.Add(new OcrLineInput(normalized, line.Text, line.Confidence));

            if (debug is not null)
            {
                debug.OcrLines.Add(line.Text);
                debug.NormalizedLines.Add(normalized);
                bool skip = line.Confidence < _config.OcrConfidenceThreshold;
                bool header = !skip && ChatLineParser.IsHeader(normalized);
                debug.LineRoles.Add(skip ? "SKIP" : header ? "HEADER" : "CONT");
                var parsed = ChatLineParser.Parse(normalized);
                debug.ParsedChannels.Add(parsed.Channel ?? "—");
            }
        }

        var assembly = ChatMessageAssembler.Assemble(inputs, _config.OcrConfidenceThreshold);
        var (toEmit, newBuffer) = DeferredTrailing.Resolve(_deferredTrailing, assembly);

        // Diagnostics: anything held in the new buffer is "deferred" this tick.
        if (debug is not null && newBuffer is not null)
            debug.MatchResults.Add(
                $"DEFERRED [rows {newBuffer.StartRow}-{newBuffer.EndRow}]: " +
                $"[{newBuffer.Channel ?? "—"}] {newBuffer.OriginalText}");

        // Track which emitted messages originated from the deferred buffer so we can
        // distinguish EMITTED-DEFERRED from EMITTED in the debug log. This relies on
        // a deliberate contract from DeferredTrailing.Resolve: when it could not find
        // a grown version of the previous trailing in current.Completed, it inserts
        // the previousTrailing *by reference* at toEmit[0]. That reference identity
        // is the only reliable signal that toEmit[0] is the freshly-unbuffered version
        // versus a normally-completed message. If DeferredTrailing.Resolve is ever
        // changed to copy/clone the message, update this check in lockstep.
        var previousWasEmittedFromBuffer =
            _deferredTrailing is not null &&
            toEmit.Count > 0 &&
            ReferenceEquals(toEmit[0], _deferredTrailing);

        _deferredTrailing = newBuffer;

        for (int i = 0; i < toEmit.Count; i++)
        {
            var msg = toEmit[i];
            var fromBuffer = (i == 0) && previousWasEmittedFromBuffer;
            var prefix = fromBuffer ? "EMITTED-DEFERRED" : "EMITTED";

            // U+001F (Unit Separator) cannot appear in OCR output or user text, so
            // it is safe to use as a field delimiter in the dedup key.
            const string sep = "\x1F";
            var dedupKey = $"{msg.Channel}{sep}{msg.PlayerName}{sep}{msg.Timestamp}{sep}{msg.Body}";
            if (_dedup.IsDuplicate(dedupKey))
            {
                debug?.MatchResults.Add(
                    $"DEDUP [rows {msg.StartRow}-{msg.EndRow}]: {msg.OriginalText}");
                continue;
            }

            var parsed = msg.ToParsedChatLine();

            // Stats pipeline (independent of Event Repost).
            if (_config.StatsEnabled)
            {
                var counter = counterMatcher.Match(parsed);
                if (counter is not null)
                {
                    _stats.Record(counter.Label, counter.Value, DateTimeOffset.UtcNow);
                    debug?.MatchResults.Add(
                        $"COUNTED [{counter.Label}: {counter.Value}] rows {msg.StartRow}-{msg.EndRow}: " +
                        $"{msg.OriginalText}");
                }
            }

            // Event Repost pipeline — only runs when enabled.
            if (!_config.EventRepostEnabled) continue;

            var match = matcher.FindMatch(parsed);
            if (match is null) continue;

            if (!_cooldown.TryFire(match.Rule.Id, TimeSpan.FromSeconds(match.Rule.CooldownSec)))
            {
                debug?.MatchResults.Add(
                    $"COOLDOWN [{match.Rule.Label}] rows {msg.StartRow}-{msg.EndRow}: " +
                    $"{msg.OriginalText}");
                continue;
            }

            debug?.MatchResults.Add(
                $"{prefix} [{match.Rule.Label}] rows {msg.StartRow}-{msg.EndRow}: " +
                $"{msg.OriginalText}");

            var evt = new DetectionEvent(
                FeatureId: "chat",
                RuleLabel: match.Rule.Label,
                MatchedContent: msg.OriginalText,
                TimestampUtc: DateTimeOffset.UtcNow,
                PlayerName: _playerName,
                Extras: new Dictionary<string, string>(),
                ImageAttachment: null);

            await _bus.PublishAsync(evt, ct).ConfigureAwait(false);
        }

        if (debug is not null &&
            (debug.OcrLines.Count > 0 || debug.MatchResults.Count > 0))
            DebugTick?.Invoke(debug);
    }
}
