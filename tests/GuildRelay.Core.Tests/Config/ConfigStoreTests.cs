using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Config;
using Xunit;

namespace GuildRelay.Core.Tests.Config;

public class ConfigStoreTests
{
    private static string FreshConfigPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "guildrelay-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "config.json");
    }

    [Fact]
    public async Task LoadMissingFileReturnsDefaultsAndCreatesFile()
    {
        var path = FreshConfigPath();
        var store = new ConfigStore(path);

        var cfg = await store.LoadOrCreateDefaultsAsync();

        cfg.SchemaVersion.Should().Be(1);
        cfg.General.PlayerName.Should().BeEmpty();
        cfg.General.WebhookUrl.Should().BeEmpty();
        cfg.General.GlobalEnabled.Should().BeTrue();
        cfg.Logs.RetentionDays.Should().Be(14);
        cfg.Logs.MaxFileSizeMb.Should().Be(50);
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task SavedConfigRoundTrips()
    {
        var path = FreshConfigPath();
        var store = new ConfigStore(path);

        var cfg = await store.LoadOrCreateDefaultsAsync();
        cfg = cfg with
        {
            General = cfg.General with
            {
                PlayerName = "Tosh",
                WebhookUrl = "https://discord.com/api/webhooks/1/x"
            }
        };
        await store.SaveAsync(cfg);

        var reopened = await new ConfigStore(path).LoadOrCreateDefaultsAsync();

        reopened.General.PlayerName.Should().Be("Tosh");
        reopened.General.WebhookUrl.Should().Be("https://discord.com/api/webhooks/1/x");
    }

    [Fact]
    public async Task SaveIsAtomicViaTmpFile()
    {
        var path = FreshConfigPath();
        var store = new ConfigStore(path);

        var cfg = await store.LoadOrCreateDefaultsAsync();
        await store.SaveAsync(cfg);

        // After save completes, no .tmp file should linger
        File.Exists(path + ".tmp").Should().BeFalse();
    }
}
