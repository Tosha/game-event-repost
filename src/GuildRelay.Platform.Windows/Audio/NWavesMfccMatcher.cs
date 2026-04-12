using System;
using System.Collections.Generic;
using System.Linq;
using GuildRelay.Core.Audio;
using NWaves.FeatureExtractors;
using NWaves.FeatureExtractors.Options;
using NWaves.Signals;

namespace GuildRelay.Platform.Windows.Audio;

public sealed class NWavesMfccMatcher : IAudioMatcher
{
    private const int SampleRate = 16000;
    private const int MfccCount = 13;
    private const double FrameDurationSec = 0.025;
    private const double HopDurationSec = 0.010;

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

        // Copy span to array up front — ReadOnlySpan can't cross lock/yield boundaries
        var samples = monoSamples.ToArray();
        var results = new List<AudioMatch>();

        lock (_lock)
        {
            if (_references.Count == 0)
                return results;

            var extractor = CreateExtractor();
            var signal = new DiscreteSignal(SampleRate, samples);
            var newFrames = extractor.ComputeFrom(signal);

            foreach (var frame in newFrames)
                _liveFrames.Add(frame);

            var maxFrames = (int)(4.0 / HopDurationSec);
            while (_liveFrames.Count > maxFrames)
                _liveFrames.RemoveAt(0);

            foreach (var refr in _references)
            {
                if (_liveFrames.Count < refr.Frames.Count)
                    continue;

                var score = SlidingCosineSimilarity(_liveFrames, refr.Frames);
                if (score >= refr.Rule.Sensitivity)
                    results.Add(new AudioMatch(refr.Rule.Id, refr.Rule.Label, score));
            }
        }

        return results;
    }

    private static double SlidingCosineSimilarity(
        List<float[]> liveFrames, IReadOnlyList<float[]> refFrames)
    {
        var refLen = refFrames.Count;
        var bestScore = double.MinValue;

        var startRange = Math.Max(0, liveFrames.Count - refLen - 10);
        var endRange = liveFrames.Count - refLen;

        for (var offset = startRange; offset <= endRange; offset++)
        {
            double totalSim = 0;
            for (var i = 0; i < refLen; i++)
                totalSim += CosineSimilarity(liveFrames[offset + i], refFrames[i]);
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
