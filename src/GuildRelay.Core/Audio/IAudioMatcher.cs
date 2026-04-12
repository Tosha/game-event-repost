using System;
using System.Collections.Generic;

namespace GuildRelay.Core.Audio;

public interface IAudioMatcher
{
    void LoadReferences(IEnumerable<AudioRule> rules);
    IEnumerable<AudioMatch> Feed(ReadOnlySpan<float> monoSamples, int sampleRate);
}
