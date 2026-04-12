using System;
using System.Collections.Generic;
using GuildRelay.Core.Config;
using GuildRelay.Core.Preprocessing;

namespace GuildRelay.Platform.Windows.Preprocessing;

public static class StageFactory
{
    public static IPreprocessStage Create(PreprocessStageConfig config)
    {
        return config.Stage.ToLowerInvariant() switch
        {
            "grayscale" => new GrayscaleStage(),
            "contraststretch" => new ContrastStretchStage(
                config.Parameters?.GetValueOrDefault("low", 0.1) ?? 0.1,
                config.Parameters?.GetValueOrDefault("high", 0.9) ?? 0.9),
            "upscale" => new UpscaleStage(
                (int)(config.Parameters?.GetValueOrDefault("factor", 2) ?? 2)),
            "adaptivethreshold" => new AdaptiveThresholdStage(
                (int)(config.Parameters?.GetValueOrDefault("blockSize", 15) ?? 15)),
            _ => throw new ArgumentException($"Unknown preprocess stage: {config.Stage}")
        };
    }

    public static List<IPreprocessStage> CreatePipeline(List<PreprocessStageConfig> configs)
    {
        var stages = new List<IPreprocessStage>();
        foreach (var c in configs)
            stages.Add(Create(c));
        return stages;
    }
}
