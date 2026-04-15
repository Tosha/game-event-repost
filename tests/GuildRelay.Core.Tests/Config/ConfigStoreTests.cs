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

    // Regression tests for the CaptureIntervalMs→CaptureIntervalSec rename.
    // When a pre-rename config on disk carries the old key (or a zero value
    // left over from a silent deserialization failure), the load path must
    // fall back to the default of 5 seconds, not a silently-zero interval.

    [Fact]
    public async Task LoadConfigWithZeroChatIntervalFallsBackToDefault()
    {
        var path = FreshConfigPath();
        var store = new ConfigStore(path);
        await store.LoadOrCreateDefaultsAsync();

        // Simulate the pre-rename state: captureIntervalSec persisted as 0
        // because the old captureIntervalMs key no longer matched any ctor param.
        var raw = await File.ReadAllTextAsync(path);
        raw = System.Text.RegularExpressions.Regex.Replace(
            raw,
            "\"captureIntervalSec\"\\s*:\\s*5",
            "\"captureIntervalSec\": 0",
            System.Text.RegularExpressions.RegexOptions.None);
        await File.WriteAllTextAsync(path, raw);

        var reopened = await new ConfigStore(path).LoadOrCreateDefaultsAsync();

        reopened.Chat.CaptureIntervalSec.Should().Be(5);
        reopened.Status.CaptureIntervalSec.Should().Be(5);
    }

    [Fact]
    public async Task LoadConfigWithLegacyCaptureIntervalMsKeyFallsBackToDefault()
    {
        var path = FreshConfigPath();
        var store = new ConfigStore(path);
        await store.LoadOrCreateDefaultsAsync();

        // Simulate a config written before the rename: the key is
        // captureIntervalMs (unknown to the new constructor), so the
        // new parameter silently defaults to 0.
        var raw = await File.ReadAllTextAsync(path);
        raw = raw.Replace("\"captureIntervalSec\"", "\"captureIntervalMs\"");
        await File.WriteAllTextAsync(path, raw);

        var reopened = await new ConfigStore(path).LoadOrCreateDefaultsAsync();

        reopened.Chat.CaptureIntervalSec.Should().Be(5);
        reopened.Status.CaptureIntervalSec.Should().Be(5);
    }

    [Fact]
    public async Task LoadConfigWithValidIntervalsPreservesThem()
    {
        var path = FreshConfigPath();
        var store = new ConfigStore(path);
        var cfg = await store.LoadOrCreateDefaultsAsync();
        cfg = cfg with
        {
            Chat = cfg.Chat with { CaptureIntervalSec = 7 },
            Status = cfg.Status with { CaptureIntervalSec = 12 }
        };
        await store.SaveAsync(cfg);

        var reopened = await new ConfigStore(path).LoadOrCreateDefaultsAsync();

        reopened.Chat.CaptureIntervalSec.Should().Be(7);
        reopened.Status.CaptureIntervalSec.Should().Be(12);
    }
}
