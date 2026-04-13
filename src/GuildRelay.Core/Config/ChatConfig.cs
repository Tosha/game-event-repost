using System.Collections.Generic;

namespace GuildRelay.Core.Config;

public sealed record ChatConfig(
    bool Enabled,
    int CaptureIntervalMs,
    double OcrConfidenceThreshold,
    int DefaultCooldownSec,
    RegionConfig Region,
    List<PreprocessStageConfig> PreprocessPipeline,
    List<ChatRuleConfig> Rules,
    Dictionary<string, string> Templates)
{
    public static ChatConfig Default => new(
        Enabled: false,
        CaptureIntervalMs: 1000,
        OcrConfidenceThreshold: 0.65,
        DefaultCooldownSec: 600,
        Region: RegionConfig.Empty,
        PreprocessPipeline: new List<PreprocessStageConfig>
        {
            new("grayscale"),
            new("contrastStretch", new Dictionary<string, double> { ["low"] = 0.1, ["high"] = 0.9 }),
            new("upscale", new Dictionary<string, double> { ["factor"] = 2 }),
            new("adaptiveThreshold", new Dictionary<string, double> { ["blockSize"] = 15 })
        },
        Rules: new List<ChatRuleConfig>(RuleTemplates.BuiltIn["MO2 Game Events"]),
        Templates: new Dictionary<string, string>
        {
            ["default"] = "**{player}** saw chat match [{rule_label}]: `{matched_text}`"
        });
}
