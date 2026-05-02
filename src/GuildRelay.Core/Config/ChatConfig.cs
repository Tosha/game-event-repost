using System.Collections.Generic;

namespace GuildRelay.Core.Config;

public sealed record ChatConfig(
    bool EventRepostEnabled,
    bool StatsEnabled,
    int CaptureIntervalSec,
    double OcrConfidenceThreshold,
    int DefaultCooldownSec,
    RegionConfig Region,
    List<PreprocessStageConfig> PreprocessPipeline,
    List<StructuredChatRule> Rules,
    List<CounterRule> CounterRules,
    Dictionary<string, string> Templates)
{
    public static ChatConfig Default => new(
        EventRepostEnabled: false,
        StatsEnabled: false,
        CaptureIntervalSec: 5,
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
        Rules: new List<StructuredChatRule>(RuleTemplates.BuiltIn["MO2 Game Events"]),
        CounterRules: new List<CounterRule>
        {
            new(
                Id: "mo2_glory",
                Label: "Glory",
                Channels: new List<string> { "Game" },
                Pattern: "You gained {value} Glory.",
                MatchMode: CounterMatchMode.Template)
        },
        Templates: new Dictionary<string, string>
        {
            ["default"] = "**{player}** saw chat match [{rule_label}]: `{matched_text}`"
        });
}
