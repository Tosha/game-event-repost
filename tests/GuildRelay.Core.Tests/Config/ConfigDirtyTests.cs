using FluentAssertions;
using GuildRelay.Core.Config;
using Xunit;

namespace GuildRelay.Core.Tests.Config;

public class ConfigDirtyTests
{
    [Fact]
    public void ChatTabIsCleanForIdenticalConfigs()
    {
        var c = AppConfig.Default;
        ConfigDirty.IsDirtyChatTab(c, c).Should().BeFalse();
    }

    [Fact]
    public void ChatTabDirtyWhenEnabledFlips()
    {
        var saved = AppConfig.Default;
        var pending = saved with { Chat = saved.Chat with { Enabled = !saved.Chat.Enabled } };
        ConfigDirty.IsDirtyChatTab(pending, saved).Should().BeTrue();
    }

    [Fact]
    public void ChatTabDirtyWhenRegionChanges()
    {
        var saved = AppConfig.Default;
        var pending = saved with
        {
            Chat = saved.Chat with
            {
                Region = new RegionConfig(5, 5, 50, 50, 96, new ResolutionConfig(1920, 1080), "PRIMARY")
            }
        };
        ConfigDirty.IsDirtyChatTab(pending, saved).Should().BeTrue();
    }

    [Fact]
    public void ChatTabNotDirtyForChatAdvancedSettingsChange()
    {
        var saved = AppConfig.Default;
        var pending = saved with { Chat = saved.Chat with { CaptureIntervalSec = saved.Chat.CaptureIntervalSec + 1 } };

        ConfigDirty.IsDirtyChatTab(pending, saved).Should().BeFalse();
        ConfigDirty.IsDirtySettingsTab(pending, saved).Should().BeTrue();
    }

    [Fact]
    public void SettingsTabDirtyForGeneralChange()
    {
        var saved = AppConfig.Default;
        var pending = saved with { General = saved.General with { PlayerName = "Tosh" } };
        ConfigDirty.IsDirtySettingsTab(pending, saved).Should().BeTrue();
    }

    [Fact]
    public void SettingsTabDirtyForEachChatAdvancedField()
    {
        var saved = AppConfig.Default;

        var pendingInterval = saved with { Chat = saved.Chat with { CaptureIntervalSec = saved.Chat.CaptureIntervalSec + 1 } };
        var pendingConfidence = saved with { Chat = saved.Chat with { OcrConfidenceThreshold = saved.Chat.OcrConfidenceThreshold + 0.1 } };
        var pendingCooldown = saved with { Chat = saved.Chat with { DefaultCooldownSec = saved.Chat.DefaultCooldownSec + 1 } };

        ConfigDirty.IsDirtySettingsTab(pendingInterval, saved).Should().BeTrue();
        ConfigDirty.IsDirtySettingsTab(pendingConfidence, saved).Should().BeTrue();
        ConfigDirty.IsDirtySettingsTab(pendingCooldown, saved).Should().BeTrue();
    }

    [Fact]
    public void AnyDirtyIsTrueWhenAnySectionDiffers()
    {
        var saved = AppConfig.Default;
        var pending = saved with { Audio = saved.Audio with { Enabled = !saved.Audio.Enabled } };
        ConfigDirty.AnyDirty(pending, saved).Should().BeTrue();
    }

    [Fact]
    public void AnyDirtyIsFalseForIdenticalConfigs()
    {
        var c = AppConfig.Default;
        ConfigDirty.AnyDirty(c, c).Should().BeFalse();
    }
}
