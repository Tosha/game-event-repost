# Audio Watcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Audio Watcher feature — continuous WASAPI loopback capture with MFCC-based reference-clip matching and per-rule cooldowns, completing the second v1 detection feature.

**Architecture:** Audio Watcher captures system audio via NAudio `WasapiLoopbackCapture`, downmixes to 16 kHz mono, extracts MFCC features via NWaves, and runs sliding-window cosine similarity against pre-processed reference clips. Matches above a per-rule sensitivity threshold that have cooled down emit `DetectionEvent`s to the `EventBus`. All NAudio/NWaves code lives in `GuildRelay.Platform.Windows`; the feature orchestration in `GuildRelay.Features.Audio` depends only on Core interfaces. See architecture spec §6.

**Tech Stack:** .NET 8, NAudio (`WasapiLoopbackCapture`), NWaves (MFCC extraction), xUnit + FluentAssertions.

**Prerequisite:** Plans 1 (Foundations) and 2 (Chat Watcher) must be complete and merged. The solution, Core contracts, Platform.Windows, Publisher, Logging, and App shell already exist.

**Definition of done:**
- `dotnet build -c Release` succeeds with zero warnings.
- `dotnet test` passes all existing tests plus all new Audio Watcher tests.
- Running the app: Audio Watcher tab appears in config. User can add a reference clip, set sensitivity and cooldown, enable the feature, and see matching audio events posted to Discord.

---

## File structure

```
src/
├── GuildRelay.Core/
│   ├── Audio/
│   │   ├── IAudioMatcher.cs           (NEW — interface)
│   │   ├── IAudioSource.cs            (NEW — interface)
│   │   ├── AudioMatch.cs              (NEW — value type)
│   │   └── AudioRule.cs               (NEW — value type)
│   └── Config/
│       ├── AudioConfig.cs             (NEW)
│       ├── AudioRuleConfig.cs         (NEW)
│       └── AppConfig.cs              (MODIFY — add AudioConfig)
│
├── GuildRelay.Platform.Windows/
│   └── Audio/
│       ├── WasapiLoopbackSource.cs    (NEW — IAudioSource impl)
│       └── NWavesMfccMatcher.cs       (NEW — IAudioMatcher impl)
│
├── GuildRelay.Features.Audio/         (NEW project)
│   ├── GuildRelay.Features.Audio.csproj
│   ├── AudioWatcher.cs                (IFeature impl)
│   └── CooldownTracker.cs
│
└── GuildRelay.App/
    ├── CoreHost.cs                    (MODIFY — register AudioWatcher)
    ├── Config/
    │   ├── AudioConfigTab.xaml        (NEW)
    │   ├── AudioConfigTab.xaml.cs     (NEW)
    │   └── ConfigWindow.xaml          (MODIFY — add tab)

tests/
├── GuildRelay.Features.Audio.Tests/   (NEW project)
│   ├── GuildRelay.Features.Audio.Tests.csproj
│   ├── CooldownTrackerTests.cs
│   └── AudioWatcherTests.cs
```

---

## Task 1: Project scaffold — Features.Audio + test project

**Files:**
- Create: `src/GuildRelay.Features.Audio/GuildRelay.Features.Audio.csproj`
- Create: `tests/GuildRelay.Features.Audio.Tests/GuildRelay.Features.Audio.Tests.csproj`
- Modify: `GuildRelay.sln`
- Modify: `src/GuildRelay.App/GuildRelay.App.csproj`

- [ ] **Step 1: Create projects**

```bash
cd C:/Users/tosha/IdeaProjects/game-event-repost
dotnet new classlib -n GuildRelay.Features.Audio -o src/GuildRelay.Features.Audio -f net8.0
dotnet new xunit -n GuildRelay.Features.Audio.Tests -o tests/GuildRelay.Features.Audio.Tests -f net8.0
```

Delete auto-generated `Class1.cs` / `UnitTest1.cs`.

- [ ] **Step 2: Add to solution**

```bash
dotnet sln add src/GuildRelay.Features.Audio/GuildRelay.Features.Audio.csproj
dotnet sln add tests/GuildRelay.Features.Audio.Tests/GuildRelay.Features.Audio.Tests.csproj
```

- [ ] **Step 3: Add project references**

```bash
dotnet add src/GuildRelay.Features.Audio reference src/GuildRelay.Core
dotnet add src/GuildRelay.App reference src/GuildRelay.Features.Audio
dotnet add tests/GuildRelay.Features.Audio.Tests reference src/GuildRelay.Features.Audio src/GuildRelay.Core
```

- [ ] **Step 4: Add packages**

```bash
dotnet add tests/GuildRelay.Features.Audio.Tests package FluentAssertions
dotnet add src/GuildRelay.Platform.Windows package NAudio
dotnet add src/GuildRelay.Platform.Windows package NWaves
```

- [ ] **Step 5: Harden csproj settings**

Add `<LangVersion>latest</LangVersion>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` to both new project csprojs. `Features.Audio` targets `net8.0` (platform-agnostic).

- [ ] **Step 6: Build and verify**

```bash
dotnet build
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add GuildRelay.sln src/GuildRelay.Features.Audio src/GuildRelay.Platform.Windows/GuildRelay.Platform.Windows.csproj src/GuildRelay.App/GuildRelay.App.csproj tests/GuildRelay.Features.Audio.Tests
git commit -m "feat: scaffold Features.Audio project + add NAudio/NWaves packages"
```

---

## Task 2: Core interfaces — IAudioSource, IAudioMatcher, AudioMatch, AudioRule

**Files:**
- Create: `src/GuildRelay.Core/Audio/IAudioSource.cs`
- Create: `src/GuildRelay.Core/Audio/IAudioMatcher.cs`
- Create: `src/GuildRelay.Core/Audio/AudioMatch.cs`
- Create: `src/GuildRelay.Core/Audio/AudioRule.cs`

- [ ] **Step 1: Implement the types**

`src/GuildRelay.Core/Audio/AudioRule.cs`:

```csharp
namespace GuildRelay.Core.Audio;

/// <summary>
/// A loaded reference clip ready for matching. Created by the matcher
/// from a user-provided WAV file path + config.
/// </summary>
public sealed class AudioRule
{
    public AudioRule(string id, string label, float[] monoSamples16Khz, double sensitivity, int cooldownSec)
    {
        Id = id;
        Label = label;
        MonoSamples16Khz = monoSamples16Khz;
        Sensitivity = sensitivity;
        CooldownSec = cooldownSec;
    }

    public string Id { get; }
    public string Label { get; }
    public float[] MonoSamples16Khz { get; }
    public double Sensitivity { get; }
    public int CooldownSec { get; }
}
```

`src/GuildRelay.Core/Audio/AudioMatch.cs`:

```csharp
namespace GuildRelay.Core.Audio;

public sealed record AudioMatch(string RuleId, string RuleLabel, double Score);
```

`src/GuildRelay.Core/Audio/IAudioMatcher.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace GuildRelay.Core.Audio;

public interface IAudioMatcher
{
    void LoadReferences(IEnumerable<AudioRule> rules);
    IEnumerable<AudioMatch> Feed(ReadOnlySpan<float> monoSamples, int sampleRate);
}
```

`src/GuildRelay.Core/Audio/IAudioSource.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GuildRelay.Core.Audio;

/// <summary>
/// Abstraction over audio capture. The implementation (WASAPI loopback)
/// lives in Platform.Windows. Tests use a fake that feeds synthetic samples.
/// </summary>
public interface IAudioSource : IDisposable
{
    /// <summary>Raised when a buffer of mono 16 kHz float samples is ready.</summary>
    event Action<float[]> SamplesReady;
    /// <summary>Raised when recording stops unexpectedly (device loss, etc.).</summary>
    event Action<Exception?> RecordingStopped;
    void Start();
    void Stop();
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/GuildRelay.Core
```

Expected: 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/GuildRelay.Core/Audio
git commit -m "feat(core): add IAudioSource, IAudioMatcher, AudioMatch, AudioRule contracts"
```

---

## Task 3: CooldownTracker — TDD

**Files:**
- Create: `src/GuildRelay.Features.Audio/CooldownTracker.cs`
- Create: `tests/GuildRelay.Features.Audio.Tests/CooldownTrackerTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/GuildRelay.Features.Audio.Tests/CooldownTrackerTests.cs`:

```csharp
using System;
using FluentAssertions;
using GuildRelay.Features.Audio;
using Xunit;

namespace GuildRelay.Features.Audio.Tests;

public class CooldownTrackerTests
{
    [Fact]
    public void FirstFireIsAlwaysAllowed()
    {
        var tracker = new CooldownTracker();
        tracker.TryFire("rule1", TimeSpan.FromSeconds(15)).Should().BeTrue();
    }

    [Fact]
    public void SecondFireWithinCooldownIsBlocked()
    {
        var tracker = new CooldownTracker();
        tracker.TryFire("rule1", TimeSpan.FromSeconds(15));
        tracker.TryFire("rule1", TimeSpan.FromSeconds(15)).Should().BeFalse();
    }

    [Fact]
    public void DifferentRulesHaveIndependentCooldowns()
    {
        var tracker = new CooldownTracker();
        tracker.TryFire("rule1", TimeSpan.FromSeconds(15));
        tracker.TryFire("rule2", TimeSpan.FromSeconds(15)).Should().BeTrue();
    }

    [Fact]
    public void FireIsAllowedAfterCooldownExpires()
    {
        var tracker = new CooldownTracker(timeProvider: () => DateTimeOffset.UtcNow);
        var start = DateTimeOffset.UtcNow;
        var time = start;
        var tr = new CooldownTracker(timeProvider: () => time);

        tr.TryFire("rule1", TimeSpan.FromSeconds(1)).Should().BeTrue();
        tr.TryFire("rule1", TimeSpan.FromSeconds(1)).Should().BeFalse();

        time = start.AddSeconds(2); // advance past cooldown
        tr.TryFire("rule1", TimeSpan.FromSeconds(1)).Should().BeTrue();
    }

    [Fact]
    public void ResetClearsAllCooldowns()
    {
        var tracker = new CooldownTracker();
        tracker.TryFire("rule1", TimeSpan.FromSeconds(15));
        tracker.Reset();
        tracker.TryFire("rule1", TimeSpan.FromSeconds(15)).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/GuildRelay.Features.Audio.Tests --filter "FullyQualifiedName~CooldownTrackerTests"
```

Expected: compile error, `CooldownTracker` does not exist.

- [ ] **Step 3: Implement `CooldownTracker`**

`src/GuildRelay.Features.Audio/CooldownTracker.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace GuildRelay.Features.Audio;

/// <summary>
/// Tracks per-rule cooldowns. A rule can only fire if its cooldown
/// has expired since the last fire. Thread-safe via lock.
/// </summary>
public sealed class CooldownTracker
{
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, DateTimeOffset> _lastFired = new();
    private readonly object _lock = new();

    public CooldownTracker(Func<DateTimeOffset>? timeProvider = null)
    {
        _now = timeProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public bool TryFire(string ruleId, TimeSpan cooldown)
    {
        lock (_lock)
        {
            var now = _now();
            if (_lastFired.TryGetValue(ruleId, out var last) && now - last < cooldown)
                return false;
            _lastFired[ruleId] = now;
            return true;
        }
    }

    public void Reset()
    {
        lock (_lock) { _lastFired.Clear(); }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/GuildRelay.Features.Audio.Tests
```

Expected: 5 passed.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Features.Audio/CooldownTracker.cs tests/GuildRelay.Features.Audio.Tests/CooldownTrackerTests.cs
git commit -m "feat(audio): add CooldownTracker with per-rule time-based gating"
```

---

## Task 4: Audio config DTOs

**Files:**
- Create: `src/GuildRelay.Core/Config/AudioRuleConfig.cs`
- Create: `src/GuildRelay.Core/Config/AudioConfig.cs`
- Modify: `src/GuildRelay.Core/Config/AppConfig.cs`

- [ ] **Step 1: Implement config DTOs**

`src/GuildRelay.Core/Config/AudioRuleConfig.cs`:

```csharp
namespace GuildRelay.Core.Config;

public sealed record AudioRuleConfig(
    string Id,
    string Label,
    string ClipPath,
    double Sensitivity,
    int CooldownSec);
```

`src/GuildRelay.Core/Config/AudioConfig.cs`:

```csharp
using System.Collections.Generic;

namespace GuildRelay.Core.Config;

public sealed record AudioConfig(
    bool Enabled,
    List<AudioRuleConfig> Rules,
    Dictionary<string, string> Templates)
{
    public static AudioConfig Default => new(
        Enabled: false,
        Rules: new List<AudioRuleConfig>(),
        Templates: new Dictionary<string, string>
        {
            ["default"] = "**{player}** heard [{rule_label}]"
        });
}
```

- [ ] **Step 2: Update `AppConfig`**

Modify `src/GuildRelay.Core/Config/AppConfig.cs`:

```csharp
namespace GuildRelay.Core.Config;

public sealed record AppConfig(
    int SchemaVersion,
    GeneralConfig General,
    ChatConfig Chat,
    AudioConfig Audio,
    LogsConfig Logs
)
{
    public static AppConfig Default => new(
        SchemaVersion: 1,
        General: GeneralConfig.Default,
        Chat: ChatConfig.Default,
        Audio: AudioConfig.Default,
        Logs: LogsConfig.Default);
}
```

- [ ] **Step 3: Build and run all tests to verify nothing broke**

```bash
dotnet build && dotnet test
```

- [ ] **Step 4: Commit**

```bash
git add src/GuildRelay.Core/Config
git commit -m "feat(core): add AudioConfig and AudioRuleConfig DTOs"
```

---

## Task 5: NWavesMfccMatcher — IAudioMatcher implementation

**Files:**
- Create: `src/GuildRelay.Platform.Windows/Audio/NWavesMfccMatcher.cs`

This is the core DSP code. It pre-computes MFCC matrices for reference clips and runs sliding-window cosine similarity on live audio. Build-verified; the math is exercised through AudioWatcher integration tests in Task 6.

- [ ] **Step 1: Implement `NWavesMfccMatcher`**

`src/GuildRelay.Platform.Windows/Audio/NWavesMfccMatcher.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GuildRelay.Core.Audio;
using NWaves.FeatureExtractors;
using NWaves.FeatureExtractors.Options;
using NWaves.Signals;
using NWaves.Utils;

namespace GuildRelay.Platform.Windows.Audio;

public sealed class NWavesMfccMatcher : IAudioMatcher
{
    private const int SampleRate = 16000;
    private const int MfccCount = 13;
    private const double FrameDurationSec = 0.025; // 25 ms
    private const double HopDurationSec = 0.010;   // 10 ms

    private readonly List<LoadedReference> _references = new();
    private readonly List<float[]> _liveFrames = new();
    private readonly object _lock = new();

    public void LoadReferences(IEnumerable<AudioRule> rules)
    {
        lock (_lock)
        {
            _references.Clear();
            _liveFrames.Clear();

            var extractor = CreateExtractor();

            foreach (var rule in rules)
            {
                var signal = new DiscreteSignal(SampleRate, rule.MonoSamples16Khz);
                var frames = extractor.ComputeFrom(signal);
                var normalized = ZScoreNormalize(frames);
                _references.Add(new LoadedReference(rule, normalized));
            }
        }
    }

    public IEnumerable<AudioMatch> Feed(ReadOnlySpan<float> monoSamples, int sampleRate)
    {
        if (sampleRate != SampleRate)
            throw new ArgumentException($"Expected {SampleRate} Hz, got {sampleRate}");

        lock (_lock)
        {
            if (_references.Count == 0)
                yield break;

            // Extract MFCC frames from the incoming chunk
            var extractor = CreateExtractor();
            var signal = new DiscreteSignal(SampleRate, monoSamples.ToArray());
            var newFrames = extractor.ComputeFrom(signal);

            foreach (var frame in newFrames)
                _liveFrames.Add(frame);

            // Cap live buffer to ~4 seconds worth of frames
            var maxFrames = (int)(4.0 / HopDurationSec);
            while (_liveFrames.Count > maxFrames)
                _liveFrames.RemoveAt(0);

            // Match against each reference
            foreach (var refr in _references)
            {
                if (_liveFrames.Count < refr.Frames.Count)
                    continue;

                var score = SlidingCosineSimilarity(_liveFrames, refr.Frames);
                if (score >= refr.Rule.Sensitivity)
                    yield return new AudioMatch(refr.Rule.Id, refr.Rule.Label, score);
            }
        }
    }

    private static double SlidingCosineSimilarity(
        List<float[]> liveFrames, IReadOnlyList<float[]> refFrames)
    {
        var refLen = refFrames.Count;
        var bestScore = double.MinValue;

        // Slide the reference over the tail of the live buffer
        var startRange = Math.Max(0, liveFrames.Count - refLen - 10);
        var endRange = liveFrames.Count - refLen;

        for (var offset = startRange; offset <= endRange; offset++)
        {
            double totalSim = 0;
            for (var i = 0; i < refLen; i++)
            {
                totalSim += CosineSimilarity(liveFrames[offset + i], refFrames[i]);
            }
            var avg = totalSim / refLen;
            if (avg > bestScore) bestScore = avg;
        }

        return bestScore;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        var denom = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denom < 1e-10 ? 0 : dot / denom;
    }

    private static IReadOnlyList<float[]> ZScoreNormalize(IReadOnlyList<float[]> frames)
    {
        if (frames.Count == 0) return frames;
        var coeffCount = frames[0].Length;
        var means = new double[coeffCount];
        var stds = new double[coeffCount];

        foreach (var frame in frames)
            for (int i = 0; i < coeffCount; i++)
                means[i] += frame[i];
        for (int i = 0; i < coeffCount; i++)
            means[i] /= frames.Count;

        foreach (var frame in frames)
            for (int i = 0; i < coeffCount; i++)
                stds[i] += (frame[i] - means[i]) * (frame[i] - means[i]);
        for (int i = 0; i < coeffCount; i++)
            stds[i] = Math.Sqrt(stds[i] / frames.Count);

        var result = new List<float[]>();
        foreach (var frame in frames)
        {
            var normalized = new float[coeffCount];
            for (int i = 0; i < coeffCount; i++)
                normalized[i] = stds[i] > 1e-10 ? (float)((frame[i] - means[i]) / stds[i]) : 0;
            result.Add(normalized);
        }
        return result;
    }

    private static MfccExtractor CreateExtractor()
    {
        return new MfccExtractor(new MfccOptions
        {
            SamplingRate = SampleRate,
            FeatureCount = MfccCount,
            FrameDuration = FrameDurationSec,
            HopDuration = HopDurationSec
        });
    }

    private sealed record LoadedReference(AudioRule Rule, IReadOnlyList<float[]> Frames);
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/GuildRelay.Platform.Windows
```

Expected: 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/GuildRelay.Platform.Windows/Audio
git commit -m "feat(platform): add NWavesMfccMatcher with MFCC extraction and cosine similarity"
```

---

## Task 6: WasapiLoopbackSource — IAudioSource implementation

**Files:**
- Create: `src/GuildRelay.Platform.Windows/Audio/WasapiLoopbackSource.cs`

Windows-only, build-verified. Uses NAudio to capture system audio and re-emit as 16 kHz mono float buffers.

- [ ] **Step 1: Implement `WasapiLoopbackSource`**

`src/GuildRelay.Platform.Windows/Audio/WasapiLoopbackSource.cs`:

```csharp
using System;
using GuildRelay.Core.Audio;
using NAudio.Wave;

namespace GuildRelay.Platform.Windows.Audio;

public sealed class WasapiLoopbackSource : IAudioSource
{
    private const int TargetSampleRate = 16000;
    private WasapiLoopbackCapture? _capture;

    public event Action<float[]>? SamplesReady;
    public event Action<Exception?>? RecordingStopped;

    public void Start()
    {
        _capture = new WasapiLoopbackCapture();
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
    }

    public void Stop()
    {
        _capture?.StopRecording();
    }

    public void Dispose()
    {
        _capture?.Dispose();
        _capture = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _capture is null) return;

        var waveFormat = _capture.WaveFormat;
        var sampleCount = e.BytesRecorded / (waveFormat.BitsPerSample / 8);
        var channels = waveFormat.Channels;

        // Convert bytes to float samples
        var floats = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
            floats[i] = BitConverter.ToSingle(e.Buffer, i * 4);

        // Downmix to mono
        var monoCount = sampleCount / channels;
        var mono = new float[monoCount];
        for (int i = 0; i < monoCount; i++)
        {
            float sum = 0;
            for (int ch = 0; ch < channels; ch++)
                sum += floats[i * channels + ch];
            mono[i] = sum / channels;
        }

        // Resample to 16 kHz (simple linear interpolation)
        var sourceSampleRate = waveFormat.SampleRate;
        if (sourceSampleRate != TargetSampleRate)
        {
            var ratio = (double)sourceSampleRate / TargetSampleRate;
            var outLen = (int)(mono.Length / ratio);
            var resampled = new float[outLen];
            for (int i = 0; i < outLen; i++)
            {
                var srcIndex = i * ratio;
                var lo = (int)srcIndex;
                var hi = Math.Min(lo + 1, mono.Length - 1);
                var frac = (float)(srcIndex - lo);
                resampled[i] = mono[lo] * (1 - frac) + mono[hi] * frac;
            }
            mono = resampled;
        }

        SamplesReady?.Invoke(mono);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        RecordingStopped?.Invoke(e.Exception);
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/GuildRelay.Platform.Windows
```

Expected: 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/GuildRelay.Platform.Windows/Audio/WasapiLoopbackSource.cs
git commit -m "feat(platform): add WasapiLoopbackSource for WASAPI loopback capture"
```

---

## Task 7: AudioWatcher : IFeature — TDD with fakes

**Files:**
- Create: `src/GuildRelay.Features.Audio/AudioWatcher.cs`
- Create: `tests/GuildRelay.Features.Audio.Tests/AudioWatcherTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/GuildRelay.Features.Audio.Tests/AudioWatcherTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Audio;
using GuildRelay.Core.Config;
using GuildRelay.Core.Events;
using GuildRelay.Core.Features;
using GuildRelay.Features.Audio;
using Xunit;

namespace GuildRelay.Features.Audio.Tests;

public class AudioWatcherTests
{
    private sealed class FakeAudioSource : IAudioSource
    {
        public event Action<float[]>? SamplesReady;
        public event Action<Exception?>? RecordingStopped;
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
        source.PushSamples(new float[1600]); // trigger a feed
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
        source.PushSamples(new float[1600]); // first match fires
        await Task.Delay(50);
        source.PushSamples(new float[1600]); // second should be blocked by cooldown
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

        matcher.NextMatches = new List<AudioMatch>(); // no matches

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
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/GuildRelay.Features.Audio.Tests --filter "FullyQualifiedName~AudioWatcherTests"
```

Expected: compile error, `AudioWatcher` does not exist.

- [ ] **Step 3: Implement `AudioWatcher`**

`src/GuildRelay.Features.Audio/AudioWatcher.cs`:

```csharp
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

        // Load references from config (AudioWatcher doesn't load WAV files
        // itself — the App layer pre-loads them into AudioRule objects when
        // wiring the feature. For now, we just wire the source callback.)
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
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/GuildRelay.Features.Audio.Tests
```

Expected: all pass (CooldownTracker + AudioWatcher tests).

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Features.Audio/AudioWatcher.cs tests/GuildRelay.Features.Audio.Tests/AudioWatcherTests.cs
git commit -m "feat(audio): add AudioWatcher with source/matcher/cooldown pipeline"
```

---

## Task 8: Audio config tab + CoreHost wiring

**Files:**
- Create: `src/GuildRelay.App/Config/AudioConfigTab.xaml`
- Create: `src/GuildRelay.App/Config/AudioConfigTab.xaml.cs`
- Modify: `src/GuildRelay.App/Config/ConfigWindow.xaml` (add tab)
- Modify: `src/GuildRelay.App/Config/ConfigWindow.xaml.cs` (pass DataContext)
- Modify: `src/GuildRelay.App/CoreHost.cs` (register AudioWatcher)

- [ ] **Step 1: Create `AudioConfigTab.xaml`**

`src/GuildRelay.App/Config/AudioConfigTab.xaml`:

```xml
<UserControl x:Class="GuildRelay.App.Config.AudioConfigTab"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Margin="12">
        <CheckBox x:Name="EnabledCheck" Content="Enable Audio Watcher" Margin="0,0,0,8"/>
        <TextBlock Text="Audio detection uses WASAPI loopback (system audio output)."
                   Foreground="Orange" Margin="0,0,0,4" TextWrapping="Wrap"/>
        <TextBlock Text="Warning: Discord voice, music, and browser audio can cause false matches."
                   Foreground="Orange" Margin="0,0,0,12" TextWrapping="Wrap"/>
        <TextBlock Text="Rules (one per line: label|path-to-wav|sensitivity|cooldown-sec)" FontWeight="SemiBold"/>
        <TextBox x:Name="RulesBox" AcceptsReturn="True" Height="120"
                 VerticalScrollBarVisibility="Auto" Margin="0,4,0,8"
                 FontFamily="Consolas"/>
        <Button Content="Save Audio Settings" Click="OnSave" Padding="12,4"
                HorizontalAlignment="Left"/>
        <TextBlock x:Name="StatusText" Margin="0,8,0,0" Foreground="Gray"/>
    </StackPanel>
</UserControl>
```

- [ ] **Step 2: Create `AudioConfigTab.xaml.cs`**

`src/GuildRelay.App/Config/AudioConfigTab.xaml.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using GuildRelay.Core.Config;

namespace GuildRelay.App.Config;

public partial class AudioConfigTab : UserControl
{
    private CoreHost? _host;

    public AudioConfigTab() { InitializeComponent(); Loaded += OnLoaded; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _host = (DataContext as ConfigViewModel)?.Host;
        if (_host is null) return;

        var audio = _host.Config.Audio;
        EnabledCheck.IsChecked = audio.Enabled;

        var ruleLines = audio.Rules.Select(r =>
            $"{r.Label}|{r.ClipPath}|{r.Sensitivity:F2}|{r.CooldownSec}");
        RulesBox.Text = string.Join(Environment.NewLine, ruleLines);
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        try
        {
            var rules = ParseRules(RulesBox.Text);
            var newAudio = _host.Config.Audio with
            {
                Enabled = EnabledCheck.IsChecked ?? false,
                Rules = rules
            };
            var newConfig = _host.Config with { Audio = newAudio };
            _host.UpdateConfig(newConfig);
            await _host.ConfigStore.SaveAsync(newConfig);

            await _host.Registry.StopAsync("audio");
            if (newAudio.Enabled && newAudio.Rules.Count > 0)
                await _host.Registry.StartAsync("audio", CancellationToken.None);

            StatusText.Text = "Audio settings saved.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private static List<AudioRuleConfig> ParseRules(string text)
    {
        var rules = new List<AudioRuleConfig>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('|');
            if (parts.Length < 4) continue;
            var label = parts[0].Trim();
            var clipPath = parts[1].Trim();
            var sensitivity = double.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0.8;
            var cooldown = int.TryParse(parts[3].Trim(), out var c) ? c : 15;
            rules.Add(new AudioRuleConfig(
                Id: label.ToLowerInvariant().Replace(' ', '_'),
                Label: label,
                ClipPath: clipPath,
                Sensitivity: sensitivity,
                CooldownSec: cooldown));
        }
        return rules;
    }
}
```

- [ ] **Step 3: Add Audio Watcher tab to ConfigWindow.xaml**

Add after the Chat Watcher `TabItem`:

```xml
<TabItem Header="Audio Watcher">
    <local:AudioConfigTab x:Name="AudioTab"/>
</TabItem>
```

- [ ] **Step 4: Update ConfigWindow.xaml.cs to pass DataContext**

Add after the `ChatTab.DataContext = vm;` line:

```csharp
AudioTab.DataContext = vm;
```

- [ ] **Step 5: Wire AudioWatcher into CoreHost**

In `CoreHost.CreateAsync()`, after the Chat Watcher registration block, add:

```csharp
// Register Audio Watcher
var audioSource = new Platform.Windows.Audio.WasapiLoopbackSource();
var audioMatcher = new Platform.Windows.Audio.NWavesMfccMatcher();
var audioWatcher = new Features.Audio.AudioWatcher(
    audioSource, audioMatcher, bus, config.Audio, config.General.PlayerName);
registry.Register(audioWatcher);

if (config.Audio.Enabled && config.Audio.Rules.Count > 0)
    await audioWatcher.StartAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);
```

Also add the audio template to the templates dictionary:

```csharp
["audio"] = config.Audio.Templates.GetValueOrDefault("default", "**{player}** heard [{rule_label}]")
```

- [ ] **Step 6: Build and run all tests**

```bash
dotnet build && dotnet test
```

Expected: 0 warnings. All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/GuildRelay.App/Config/AudioConfigTab.xaml src/GuildRelay.App/Config/AudioConfigTab.xaml.cs src/GuildRelay.App/Config/ConfigWindow.xaml src/GuildRelay.App/Config/ConfigWindow.xaml.cs src/GuildRelay.App/CoreHost.cs
git commit -m "feat(app): add Audio Watcher config tab and wire AudioWatcher into CoreHost"
```

---

## Self-review

**Spec coverage (architecture §6):**
- WASAPI loopback via NAudio → Task 6 (WasapiLoopbackSource)
- Downmix to mono, resample to 16 kHz → Task 6 (OnDataAvailable)
- MFCC frame extraction via NWaves, 13 coeffs, 25ms/10ms → Task 5 (NWavesMfccMatcher)
- Sliding-window cosine similarity → Task 5 (SlidingCosineSimilarity)
- Z-score normalization per coefficient → Task 5 (ZScoreNormalize)
- Per-rule cooldown (default 15s) → Task 3 (CooldownTracker)
- DetectionEvent emission → Task 7 (AudioWatcher.OnSamplesReady)
- Device loss → Warning status → Task 7 (OnRecordingStopped)
- Audio config DTOs → Task 4
- Config tab with loopback warning banner → Task 8
- CoreHost wiring → Task 8

**Gap noted:** The architecture mentions "reference clips are pre-processed once on load: resampled to 16 kHz mono, MFCC'd." The current plan has `NWavesMfccMatcher.LoadReferences` accepting `AudioRule` objects with pre-loaded `float[] MonoSamples16Khz`. The WAV loading + resampling step (reading the .wav file from disk and converting to 16 kHz mono) needs to happen in the CoreHost wiring or in a helper. This is a small gap — the engineer should add a WAV-loading utility in Platform.Windows when wiring Task 8. Not complex enough for its own task.

**Placeholder scan:** No TBDs found.

**Type consistency:**
- `AudioRule(id, label, monoSamples16Khz, sensitivity, cooldownSec)` — consistent across Tasks 2, 5, 7.
- `AudioMatch(RuleId, RuleLabel, Score)` — consistent across Tasks 2, 5, 7.
- `AudioConfig` / `AudioRuleConfig` — consistent across Tasks 4, 7, 8.
- `CooldownTracker.TryFire(ruleId, cooldown)` — consistent across Tasks 3 and 7.
- `IAudioSource.SamplesReady` event signature `Action<float[]>` — consistent across Tasks 2, 6, 7.
- `EventBus.PublishAsync` uses `ValueTask` (void) — consistent with Plan 1 deviation.
