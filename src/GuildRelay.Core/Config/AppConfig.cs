namespace GuildRelay.Core.Config;

public sealed record AppConfig(
    int SchemaVersion,
    GeneralConfig General,
    LogsConfig Logs
)
{
    public static AppConfig Default => new(
        SchemaVersion: 1,
        General: GeneralConfig.Default,
        Logs: LogsConfig.Default);
}
