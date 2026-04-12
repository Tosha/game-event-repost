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

        foreach (var line in ocrResult.Lines)
        {
            if (line.Confidence < _config.OcrConfidenceThreshold)
                continue;

            var normalized = TextNormalizer.Normalize(line.Text);
            if (string.IsNullOrEmpty(normalized))
                continue;

            if (_dedup.IsDuplicate(normalized))
                continue;

            foreach (var rule in rules)
            {
                if (rule.Pattern.IsMatch(normalized))
                {
                    var evt = new DetectionEvent(
                        FeatureId: "chat",
                        RuleLabel: rule.Label,
                        MatchedContent: line.Text,
                        TimestampUtc: DateTimeOffset.UtcNow,
                        PlayerName: _playerName,
                        Extras: new Dictionary<string, string>(),
                        ImageAttachment: null);

                    await _bus.PublishAsync(evt, ct).ConfigureAwait(false);
                    break; // first matching rule wins per line
                }
            }
        }
    }

    private static List<CompiledRule> CompileRules(List<ChatRuleConfig> rules)
        => rules.Select(r => new CompiledRule(r.Label, CompiledPattern.Create(r.Pattern, r.Regex))).ToList();

    private sealed record CompiledRule(string Label, CompiledPattern Pattern);
}
