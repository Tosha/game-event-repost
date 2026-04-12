using System.Threading.Tasks;

namespace GuildRelay.App.Config;

public sealed class ConfigViewModel
{
    public ConfigViewModel(CoreHost host)
    {
        Host = host;
        WebhookUrl = host.Config.General.WebhookUrl;
        PlayerName = host.Config.General.PlayerName;
    }

    public CoreHost Host { get; }
    public string WebhookUrl { get; set; }
    public string PlayerName { get; set; }

    public void Apply()
    {
        var next = Host.Config with
        {
            General = Host.Config.General with
            {
                WebhookUrl = WebhookUrl,
                PlayerName = PlayerName
            }
        };
        Host.UpdateConfig(next);
    }

    public async Task SaveAsync()
    {
        Apply();
        await Host.ConfigStore.SaveAsync(Host.Config);
    }
}
