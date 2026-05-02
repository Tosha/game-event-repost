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

        var templates = new System.Collections.Generic.Dictionary<string, string>
        {
            ["test"] = "{matched_text}",
            ["chat"] = config.Chat.Templates.GetValueOrDefault("default",
                "**{player}** saw chat match [{rule_label}]: `{matched_text}`"),
            ["audio"] = config.Audio.Templates.GetValueOrDefault("default",
                "**{player}** heard [{rule_label}]"),
            ["status"] = config.Status.Templates.GetValueOrDefault("default",
                "**{player}** status: {rule_label}")
        };
        var publisher = new DiscordPublisher(
            new HttpClient(),
            secrets,
            new TemplateEngine(),
            templateByFeatureId: templates);

        var registry = new FeatureRegistry();

        // Register Chat Watcher
        var chatCapture = new Platform.Windows.Capture.BitBltCapture();
        var chatOcr = new Platform.Windows.Ocr.WindowsMediaOcrEngine();
        var chatStages = Platform.Windows.Preprocessing.StageFactory.CreatePipeline(
            config.Chat.PreprocessPipeline);
        var chatPipeline = new Features.Chat.Preprocessing.PreprocessPipeline(chatStages);
        var chatWatcher = new Features.Chat.ChatWatcher(
            chatCapture, chatOcr, chatPipeline, bus, config.Chat, config.General.PlayerName);
        registry.Register(chatWatcher);

        if ((config.Chat.EventRepostEnabled || config.Chat.StatsEnabled) && !config.Chat.Region.IsEmpty)
            await chatWatcher.StartAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);

        // Register Audio Watcher
        var audioSource = new Platform.Windows.Audio.WasapiLoopbackSource();
        var audioMatcher = new Platform.Windows.Audio.NWavesMfccMatcher();
        var audioWatcher = new Features.Audio.AudioWatcher(
            audioSource, audioMatcher, bus, config.Audio, config.General.PlayerName);
        registry.Register(audioWatcher);

        if (config.Audio.Enabled && config.Audio.Rules.Count > 0)
            await audioWatcher.StartAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);

        // Register Status Watcher (reuses BitBlt + OCR from Chat Watcher infra)
        var statusCapture = new Platform.Windows.Capture.BitBltCapture();
        var statusOcr = new Platform.Windows.Ocr.WindowsMediaOcrEngine();
        var statusStages = Platform.Windows.Preprocessing.StageFactory.CreatePipeline(
            config.Status.PreprocessPipeline);
        var statusPipeline = new Features.Chat.Preprocessing.PreprocessPipeline(statusStages);
        var statusWatcher = new Features.Status.StatusWatcher(
            statusCapture, statusOcr, statusPipeline, bus, config.Status, config.General.PlayerName);
        registry.Register(statusWatcher);

        if (config.Status.Enabled && !config.Status.Region.IsEmpty)
            await statusWatcher.StartAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);

        // Start the publisher consumer loop — drains the EventBus and posts to Discord.
        // This is a fire-and-forget background task that runs for the lifetime of the app.
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in bus.ConsumeAllAsync(System.Threading.CancellationToken.None))
                {
                    try
                    {
                        await eventLog.AppendAsync(evt, Logging.EventPostStatus.Pending);
                        await publisher.PublishAsync(evt, System.Threading.CancellationToken.None);
                        await eventLog.UpdateStatusAsync(evt, Logging.EventPostStatus.Success);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Failed to publish event {FeatureId}/{RuleLabel}", evt.FeatureId, evt.RuleLabel);
                        await eventLog.UpdateStatusAsync(evt, Logging.EventPostStatus.Failed);
                    }
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
        });

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
