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
        var bytesPerSample = waveFormat.BitsPerSample / 8;
        var sampleCount = e.BytesRecorded / bytesPerSample;
        var channels = waveFormat.Channels;

        // Convert bytes to float samples (WASAPI loopback is IEEE float)
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

        // Resample to 16 kHz (linear interpolation)
        var sourceSampleRate = waveFormat.SampleRate;
        if (sourceSampleRate != TargetSampleRate)
        {
            var ratio = (double)sourceSampleRate / TargetSampleRate;
            var outLen = (int)(mono.Length / ratio);
            if (outLen == 0) return;
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
