namespace GuildRelay.Core.Config;

public sealed record GeneralConfig(
    string WebhookUrl,
    string PlayerName,
    bool GlobalEnabled
)
{
    public static GeneralConfig Default => new(
        WebhookUrl: string.Empty,
        PlayerName: string.Empty,
        GlobalEnabled: true);
}
