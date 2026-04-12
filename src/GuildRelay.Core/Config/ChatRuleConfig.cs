namespace GuildRelay.Core.Config;

public sealed record ChatRuleConfig(
    string Id,
    string Label,
    string Pattern,
    bool Regex);
