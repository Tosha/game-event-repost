using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Audio;
using GuildRelay.Core.Config;
using GuildRelay.Core.Events;
using GuildRelay.Features.Audio;
using Xunit;

namespace GuildRelay.Features.Audio.Tests;

public class AudioWatcherTests
{
    private sealed class FakeAudioSource : IAudioSource
    {
        public event Action<float[]>? SamplesReady;
#pragma warning disable CS0067
        public event Action<Exception?>? RecordingStopped;
#pragma warning restore CS0067
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }

        public void PushSamples(float[] samples) => SamplesReady?.Invoke(samples);
    }

    private sealed class FakeMatcher : IAudioMatcher
    {
        public List<AudioMatch> NextMatches { get; set; } = new();

        public void LoadReferences(IEnumerable<AudioRule> rules) { }

        public IEnumerable<AudioMatch> Feed(ReadOnlySpan<float> monoSamples, int sampleRate)
            => NextMatches;
    }

    private static AudioWatcher CreateWatcher(
        FakeAudioSource source, FakeMatcher matcher, EventBus bus,
        List<AudioRuleConfig> rules)
    {
        var config = AudioConfig.Default with
        {
            Enabled = true,
            Rules = rules
        };
        return new AudioWatcher(source, matcher, bus, config, playerName: "Tosh");
    }

    [Fact]
    public async Task MatchAboveSensitivityEmitsEvent()
    {
        var source = new FakeAudioSource();
        var matcher = new FakeMatcher();
        var bus = new EventBus(capacity: 16);
        var rules = new List<AudioRuleConfig>
        {
            new("horse", "Horse nearby", "whinny.wav", Sensitivity: 0.8, CooldownSec: 15)
        };
        var watcher = CreateWatcher(source, matcher, bus, rules);

        matcher.NextMatches = new List<AudioMatch>
        {
            new("horse", "Horse nearby", Score: 0.9)
        };

        await watcher.StartAsync(CancellationToken.None);
        source.PushSamples(new float[1600]);
        await Task.Delay(50);
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().ContainSingle()
            .Which.Should().Match<DetectionEvent>(e =>
                e.FeatureId == "audio" &&
                e.RuleLabel == "Horse nearby" &&
                e.PlayerName == "Tosh");
    }

    [Fact]
    public async Task MatchWithinCooldownIsBlocked()
    {
        var source = new FakeAudioSource();
        var matcher = new FakeMatcher();
        var bus = new EventBus(capacity: 16);
        var rules = new List<AudioRuleConfig>
        {
            new("horse", "Horse nearby", "whinny.wav", Sensitivity: 0.8, CooldownSec: 60)
        };
        var watcher = CreateWatcher(source, matcher, bus, rules);

        matcher.NextMatches = new List<AudioMatch>
        {
            new("horse", "Horse nearby", Score: 0.9)
        };

        await watcher.StartAsync(CancellationToken.None);
        source.PushSamples(new float[1600]);
        await Task.Delay(50);
        source.PushSamples(new float[1600]);
        await Task.Delay(50);
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().HaveCount(1, "second match should be blocked by cooldown");
    }

    [Fact]
    public async Task NoMatchEmitsNoEvent()
    {
        var source = new FakeAudioSource();
        var matcher = new FakeMatcher();
        var bus = new EventBus(capacity: 16);
        var rules = new List<AudioRuleConfig>
        {
            new("horse", "Horse nearby", "whinny.wav", Sensitivity: 0.8, CooldownSec: 15)
        };
        var watcher = CreateWatcher(source, matcher, bus, rules);

        matcher.NextMatches = new List<AudioMatch>();

        await watcher.StartAsync(CancellationToken.None);
        source.PushSamples(new float[1600]);
        await Task.Delay(50);
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().BeEmpty();
    }
}
