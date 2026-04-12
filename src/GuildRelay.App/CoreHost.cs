using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using GuildRelay.Core.Config;
using GuildRelay.Core.Events;
using GuildRelay.Core.Features;
using GuildRelay.Core.Security;
using GuildRelay.Logging;
using GuildRelay.Publisher;
using Serilog;

namespace GuildRelay.App;

public sealed class CoreHost : IAsyncDisposable
{
    public CoreHost(
        string appDataDirectory,
        ConfigStore configStore,
        AppConfig config,
        SecretStore secrets,
        EventBus bus,
        EventLog eventLog,
        ILogger logger,
        DiscordPublisher publisher,
        FeatureRegistry registry)
    {
        AppDataDirectory = appDataDirectory;
        ConfigStore = configStore;
        Config = config;
        Secrets = secrets;
        Bus = bus;
        EventLog = eventLog;
        Logger = logger;
        Publisher = publisher;
        Registry = registry;
    }

    public string AppDataDirectory { get; }
    public ConfigStore ConfigStore { get; }
    public AppConfig Config { get; private set; }
    public SecretStore Secrets { get; }
    public EventBus Bus { get; }
    public EventLog EventLog { get; }
    public ILogger Logger { get; }
    public DiscordPublisher Publisher { get; }
    public FeatureRegistry Registry { get; }

    public static async Task<CoreHost> CreateAsync()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GuildRelay");
        Directory.CreateDirectory(appData);

        var configStore = new ConfigStore(Path.Combine(appData, "config.json"));
        var config = await configStore.LoadOrCreateDefaultsAsync().ConfigureAwait(false);

        var secrets = new SecretStore();
        secrets.SetWebhookUrl(config.General.WebhookUrl);

        var logger = LoggingSetup.CreateAppLogger(Path.Combine(appData, "logs"));
        var eventLog = new EventLog(Path.Combine(appData, "logs"));
        var bus = new EventBus(capacity: 256);

        var publisher = new DiscordPublisher(
            new HttpClient(),
            secrets,
            new TemplateEngine(),
            templateByFeatureId: new System.Collections.Generic.Dictionary<string, string>
            {
                ["test"] = "{matched_text}"
            });

        var registry = new FeatureRegistry();

        logger.Information("CoreHost initialized at {Path}", appData);
        return new CoreHost(appData, configStore, config, secrets, bus, eventLog, logger, publisher, registry);
    }

    public void UpdateConfig(AppConfig newConfig)
    {
        Config = newConfig;
        Secrets.SetWebhookUrl(newConfig.General.WebhookUrl);
    }

    public async ValueTask DisposeAsync()
    {
        Logger.Information("CoreHost shutting down");
        await Registry.StopAllAsync().ConfigureAwait(false);
        Bus.Complete();
        if (Logger is IDisposable disposable) disposable.Dispose();
    }
}
