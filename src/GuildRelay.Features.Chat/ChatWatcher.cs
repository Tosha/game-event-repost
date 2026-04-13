using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GuildRelay.Core.Capture;
using GuildRelay.Core.Config;
using GuildRelay.Core.Events;
using GuildRelay.Core.Features;
using GuildRelay.Core.Ocr;
using GuildRelay.Core.Rules;
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
    private List<CompiledRule> _compiledRules;
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
        _compiledRules = CompileRules(config.Rules);
    }

    public string Id => "chat";
    public string DisplayName => "Chat Watcher";
    public FeatureStatus Status { get; private set; } = FeatureStatus.Idle;

#pragma warning disable CS0067 // StatusChanged wired by App layer in a future task
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
        _compiledRules = CompileRules(newConfig.Rules);
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
                // The watchdog will handle repeated failures at a higher level.
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

        var rules = _compiledRules; // snapshot

        // Build a list of normalized lines, then try matching each line
        // individually AND concatenated with the next line. OCR frequently
        // splits a single chat message across two lines (e.g., "[Game] You
        // received a task to kill Dire" + "Wolf (8)"), so a pattern like
        // "game.*dire wolf" only matches if we join adjacent lines.
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

            // Also build a joined version with the next line for split-message matching
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

                foreach (var rule in rules)
                {
                    if (!rule.Pattern.IsMatch(candidate.Item1))
                        continue;

                    // Per-rule cooldown: skip if this rule fired too recently
                    if (!_cooldown.TryFire(rule.Id, TimeSpan.FromSeconds(rule.CooldownSec)))
                        continue;

                    var evt = new DetectionEvent(
                        FeatureId: "chat",
                        RuleLabel: rule.Label,
                        MatchedContent: candidate.Item2,
                        TimestampUtc: DateTimeOffset.UtcNow,
                        PlayerName: _playerName,
                        Extras: new Dictionary<string, string>(),
                        ImageAttachment: null);

                    await _bus.PublishAsync(evt, ct).ConfigureAwait(false);
                    break; // first matching rule wins
                }
            }
        }
    }

    private static List<CompiledRule> CompileRules(List<ChatRuleConfig> rules)
        => rules.Select(r => new CompiledRule(r.Id, r.Label, r.CooldownSec, CompiledPattern.Create(r.Pattern, r.Regex))).ToList();

    private sealed record CompiledRule(string Id, string Label, int CooldownSec, CompiledPattern Pattern);
}
