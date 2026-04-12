namespace GuildRelay.Core.Config;

public sealed record LogsConfig(int RetentionDays, int MaxFileSizeMb)
{
    public static LogsConfig Default => new(RetentionDays: 14, MaxFileSizeMb: 50);
}
