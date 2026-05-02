using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Config;
using Xunit;

namespace GuildRelay.Core.Tests.Config;

public class ChatConfigMigrationTests
{
    private static string FreshConfigPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "guildrelay-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "config.json");
    }

    [Fact]
    public async Task LoadsLegacyEnabledFieldAsEventRepostEnabled()
    {
        var path = FreshConfigPath();

        // Hand-crafted pre-rename JSON: `enabled: true` on chat, no eventRepostEnabled,
        // no statsEnabled, no counterRules.
        var legacyJson = """
        {
          "schemaVersion": 1,
          "general": { "playerName": "", "webhookUrl": "", "globalEnabled": true },
          "chat": {
            "enabled": true,
            "captureIntervalSec": 5,
            "ocrConfidenceThreshold": 0.65,
            "defaultCooldownSec": 600,
            "region": { "x": 0, "y": 0, "width": 0, "height": 0, "dpi": 96, "resolution": { "width": 1920, "height": 1080 }, "monitorId": "" },
            "preprocessPipeline": [],
            "rules": [],
            "templates": { }
          },
          "audio": { "enabled": false, "rules": [], "templates": {} },
          "status": { "enabled": false, "captureIntervalSec": 5, "ocrConfidenceThreshold": 0.65, "debounceSamples": 3, "region": { "x": 0, "y": 0, "width": 0, "height": 0, "dpi": 96, "resolution": { "width": 1920, "height": 1080 }, "monitorId": "" }, "preprocessPipeline": [], "disconnectPatterns": [], "templates": {} },
          "logs": { "retentionDays": 14, "maxFileSizeMb": 50 }
        }
        """;
        await File.WriteAllTextAsync(path, legacyJson);

        var cfg = await new ConfigStore(path).LoadOrCreateDefaultsAsync();

        cfg.Chat.EventRepostEnabled.Should().BeTrue();
        cfg.Chat.StatsEnabled.Should().BeFalse();
        cfg.Chat.CounterRules.Should().NotBeEmpty(); // Glory built-in injected by migration
        cfg.Chat.CounterRules[0].Id.Should().Be("mo2_glory");
    }
}
