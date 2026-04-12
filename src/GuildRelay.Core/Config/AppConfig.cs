namespace GuildRelay.Core.Config;

public sealed record AppConfig(
    int SchemaVersion,
    GeneralConfig General,
    ChatConfig Chat,
    AudioConfig Audio,
    LogsConfig Logs
)
{
    public static AppConfig Default => new(
        SchemaVersion: 1,
        General: GeneralConfig.Default,
        Chat: ChatConfig.Default,
        Audio: AudioConfig.Default,
        Logs: LogsConfig.Default);
}
