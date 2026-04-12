using System.Collections.Generic;

namespace GuildRelay.Core.Config;

public sealed record StatusConfig(
    bool Enabled,
    int CaptureIntervalMs,
    double OcrConfidenceThreshold,
    int DebounceSamples,
    RegionConfig Region,
    List<PreprocessStageConfig> PreprocessPipeline,
    List<DisconnectPatternConfig> DisconnectPatterns,
    Dictionary<string, string> Templates)
{
    public static StatusConfig Default => new(
        Enabled: false,
        CaptureIntervalMs: 3000,
        OcrConfidenceThreshold: 0.65,
        DebounceSamples: 3,
        Region: RegionConfig.Empty,
        PreprocessPipeline: new List<PreprocessStageConfig>
        {
            new("grayscale"),
            new("contrastStretch", new Dictionary<string, double> { ["low"] = 0.1, ["high"] = 0.9 }),
            new("upscale", new Dictionary<string, double> { ["factor"] = 2 }),
            new("adaptiveThreshold", new Dictionary<string, double> { ["blockSize"] = 15 })
        },
        DisconnectPatterns: new List<DisconnectPatternConfig>
        {
            new("main_menu", "Returned to main menu", "return to main menu", Regex: false),
            new("lost_conn", "Lost connection", "(disconnected|lost connection)", Regex: true)
        },
        Templates: new Dictionary<string, string>
        {
            ["default"] = "**{player}** status: {rule_label}",
            ["disconnected"] = ":warning: **{player}** lost connection to the server",
            ["reconnected"] = ":white_check_mark: **{player}** is back online"
        });
}
