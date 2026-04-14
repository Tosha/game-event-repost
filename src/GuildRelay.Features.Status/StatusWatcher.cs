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
using GuildRelay.Features.Chat;
using GuildRelay.Features.Chat.Preprocessing;

namespace GuildRelay.Features.Status;

public sealed class StatusWatcher : IFeature
{
    private readonly IScreenCapture _capture;
    private readonly IOcrEngine _ocr;
    private readonly PreprocessPipeline _pipeline;
    private readonly EventBus _bus;
    private readonly string _playerName;
    private StatusConfig _config;
    private List<CompiledPattern> _patterns;
    private ConnectionStateMachine _stateMachine;
    private CancellationTokenSource? _cts;

    public StatusWatcher(
        IScreenCapture capture,
        IOcrEngine ocr,
        PreprocessPipeline pipeline,
        EventBus bus,
        StatusConfig config,
        string playerName)
    {
        _capture = capture;
        _ocr = ocr;
        _pipeline = pipeline;
        _bus = bus;
        _config = config;
        _playerName = playerName;
        _patterns = CompilePatterns(config.DisconnectPatterns);
        _stateMachine = new ConnectionStateMachine(config.DebounceSamples);
    }

    public string Id => "status";
    public string DisplayName => "Status Watcher";
    public FeatureStatus Status { get; private set; } = FeatureStatus.Idle;

#pragma warning disable CS0067
    public event EventHandler<StatusChangedArgs>? StatusChanged;
#pragma warning restore CS0067

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _stateMachine = new ConnectionStateMachine(_config.DebounceSamples);
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
        var newConfig = featureConfig.Deserialize<StatusConfig>();
        if (newConfig is null) return;
        _config = newConfig;
        _patterns = CompilePatterns(newConfig.DisconnectPatterns);
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

        // Check if any OCR line matches a disconnect pattern
        var isDisconnected = false;
        string? matchedPhrase = null;
        var patterns = _patterns;

        foreach (var line in ocrResult.Lines)
        {
            if (line.Confidence < _config.OcrConfidenceThreshold)
                continue;

            var normalized = TextNormalizer.Normalize(line.Text);
            if (string.IsNullOrEmpty(normalized))
                continue;

            foreach (var pattern in patterns)
            {
                if (pattern.IsMatch(normalized))
                {
                    isDisconnected = true;
                    matchedPhrase = line.Text;
                    break;
                }
            }
            if (isDisconnected) break;
        }

        // Feed observation to the state machine
        var transition = _stateMachine.Observe(isDisconnected);
        if (transition is null) return;

        var ruleLabel = transition.To == ConnectionState.Disconnected ? "disconnected" : "reconnected";
        var evt = new DetectionEvent(
            FeatureId: "status",
            RuleLabel: ruleLabel,
            MatchedContent: matchedPhrase ?? ruleLabel,
            TimestampUtc: DateTimeOffset.UtcNow,
            PlayerName: _playerName,
            Extras: new Dictionary<string, string>
            {
                ["transition"] = $"{transition.From}->{transition.To}"
            },
            ImageAttachment: null);

        await _bus.PublishAsync(evt, ct).ConfigureAwait(false);
    }

    private static List<CompiledPattern> CompilePatterns(List<DisconnectPatternConfig> patterns)
        => patterns.Select(p => CompiledPattern.Create(p.Pattern, p.Regex)).ToList();
}
