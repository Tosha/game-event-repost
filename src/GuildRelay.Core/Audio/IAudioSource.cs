using System;

namespace GuildRelay.Core.Audio;

/// <summary>
/// Abstraction over audio capture. The WASAPI loopback implementation
/// lives in Platform.Windows. Tests use a fake that feeds synthetic samples.
/// </summary>
public interface IAudioSource : IDisposable
{
    event Action<float[]>? SamplesReady;
    event Action<Exception?>? RecordingStopped;
    void Start();
    void Stop();
}
