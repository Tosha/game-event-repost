namespace GuildRelay.Core.Config;

public sealed record DisconnectPatternConfig(
    string Id,
    string Label,
    string Pattern,
    bool Regex);
