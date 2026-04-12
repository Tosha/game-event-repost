using System.Collections.Generic;

namespace GuildRelay.Core.Config;

public sealed record AudioConfig(
    bool Enabled,
    List<AudioRuleConfig> Rules,
    Dictionary<string, string> Templates)
{
    public static AudioConfig Default => new(
        Enabled: false,
        Rules: new List<AudioRuleConfig>(),
        Templates: new Dictionary<string, string>
        {
            ["default"] = "**{player}** heard [{rule_label}]"
        });
}
