namespace GuildRelay.Core.Config;

public sealed record AudioRuleConfig(
    string Id,
    string Label,
    string ClipPath,
    double Sensitivity,
    int CooldownSec);
