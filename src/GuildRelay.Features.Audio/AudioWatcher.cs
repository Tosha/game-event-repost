using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GuildRelay.Core.Audio;
using GuildRelay.Core.Config;
using GuildRelay.Core.Events;
using GuildRelay.Core.Features;

namespace GuildRelay.Features.Audio;

public sealed class AudioWatcher : IFeature
{
    private readonly IAudioSource _source;
    private readonly IAudioMatcher _matcher;
    private readonly EventBus _bus;
    private readonly string _playerName;
    private readonly CooldownTracker _cooldown = new();
    private AudioConfig _config;

    public AudioWatcher(
        IAudioSource source,
        IAudioMatcher matcher,
        EventBus bus,
        AudioConfig config,
        string playerName)
    {
        _source = source;
        _matcher = matcher;
        _bus = bus;
        _config = config;
        _playerName = playerName;
    }

    public string Id => "audio";
    public string DisplayName => "Audio Watcher";
    public FeatureStatus Status { get; private set; } = FeatureStatus.Idle;

#pragma warning disable CS0067
    public event EventHandler<StatusChangedArgs>? StatusChanged;
#pragma warning restore CS0067

    public Task StartAsync(CancellationToken ct)
    {
        _cooldown.Reset();
        _source.SamplesReady += OnSamplesReady;
        _source.RecordingStopped += OnRecordingStopped;
        _source.Start();
        Status = FeatureStatus.Running;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _source.SamplesReady -= OnSamplesReady;
        _source.RecordingStopped -= OnRecordingStopped;
        _source.Stop();
        Status = FeatureStatus.Idle;
        return Task.CompletedTask;
    }

    public void ApplyConfig(JsonElement featureConfig)
    {
        var newConfig = featureConfig.Deserialize<AudioConfig>();
        if (newConfig is null) return;
        _config = newConfig;
    }

    private void OnSamplesReady(float[] samples)
    {
        var matches = _matcher.Feed(samples, 16000);
        foreach (var match in matches)
        {
            var ruleConfig = _config.Rules.FirstOrDefault(r => r.Id == match.RuleId);
            var cooldownSec = ruleConfig?.CooldownSec ?? 15;

            if (!_cooldown.TryFire(match.RuleId, TimeSpan.FromSeconds(cooldownSec)))
                continue;

            var evt = new DetectionEvent(
                FeatureId: "audio",
                RuleLabel: match.RuleLabel,
                MatchedContent: match.RuleLabel,
                TimestampUtc: DateTimeOffset.UtcNow,
                PlayerName: _playerName,
                Extras: new Dictionary<string, string>
                {
                    ["score"] = match.Score.ToString("F3")
                },
                ImageAttachment: null);

            _ = _bus.PublishAsync(evt, CancellationToken.None);
        }
    }

    private void OnRecordingStopped(Exception? ex)
    {
        if (ex is not null)
            Status = FeatureStatus.Warning;
    }
}
