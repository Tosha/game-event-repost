using System.Collections.Generic;
using GuildRelay.Core.Capture;
using GuildRelay.Core.Preprocessing;

namespace GuildRelay.Features.Chat.Preprocessing;

/// <summary>
/// Applies a sequence of <see cref="IPreprocessStage"/>s to a captured
/// frame in order. Stage list is built from config at startup and swapped
/// atomically on config reload.
/// </summary>
public sealed class PreprocessPipeline
{
    private readonly IReadOnlyList<IPreprocessStage> _stages;

    public PreprocessPipeline(IReadOnlyList<IPreprocessStage> stages)
    {
        _stages = stages;
    }

    public CapturedFrame Apply(CapturedFrame frame)
    {
        var current = frame;
        foreach (var stage in _stages)
            current = stage.Apply(current);
        return current;
    }
}
