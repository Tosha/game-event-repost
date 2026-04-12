namespace GuildRelay.Core.Audio;

/// <summary>
/// A loaded reference clip ready for matching. Created from a user-provided
/// WAV file path resampled to 16 kHz mono.
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
