namespace GuildRelay.Core.Config;

public sealed record AppConfig(
    int SchemaVersion,
    GeneralConfig General,
    ChatConfig Chat,
    LogsConfig Logs
)
{
    public static AppConfig Default => new(
        SchemaVersion: 1,
        General: GeneralConfig.Default,
        Chat: ChatConfig.Default,
        Logs: LogsConfig.Default);
}
