using System.Collections.Generic;
using System.Text.Json;
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

    // The ConfigViewModel mints its pending copy via a JSON round-trip, so
    // list/dictionary members end up with different references but identical
    // content. Dirty predicates must treat that as clean — otherwise every tab
    // shows an "unsaved" dot the moment the Settings window opens.
    [Fact]
    public void NoTabIsDirtyAfterJsonRoundTrip()
    {
        var saved = AppConfig.Default;
        var pending = JsonRoundTrip(saved);

        ConfigDirty.AnyDirty(pending, saved).Should().BeFalse();
        ConfigDirty.IsDirtyChatTab(pending, saved).Should().BeFalse();
        ConfigDirty.IsDirtyAudioTab(pending, saved).Should().BeFalse();
        ConfigDirty.IsDirtyStatusTab(pending, saved).Should().BeFalse();
        ConfigDirty.IsDirtySettingsTab(pending, saved).Should().BeFalse();
    }

    [Fact]
    public void ChatTabDirtyWhenRuleEditedAcrossClone()
    {
        var saved = AppConfig.Default;
        var pending = JsonRoundTrip(saved);
        pending = pending with
        {
            Chat = pending.Chat with
            {
                Rules = new List<StructuredChatRule>
                {
                    pending.Chat.Rules[0] with { Label = "CHANGED" }
                }
            }
        };

        ConfigDirty.IsDirtyChatTab(pending, saved).Should().BeTrue();
        ConfigDirty.IsDirtyAudioTab(pending, saved).Should().BeFalse();
        ConfigDirty.IsDirtyStatusTab(pending, saved).Should().BeFalse();
    }

    [Fact]
    public void AudioTabDirtyWhenRuleAddedAcrossClone()
    {
        var saved = AppConfig.Default;
        var pending = JsonRoundTrip(saved);
        pending = pending with
        {
            Audio = pending.Audio with
            {
                Rules = new List<AudioRuleConfig>
                {
                    new("boom", "Boom", "clips/boom.wav", 0.8, 30)
                }
            }
        };

        ConfigDirty.IsDirtyAudioTab(pending, saved).Should().BeTrue();
        ConfigDirty.IsDirtyChatTab(pending, saved).Should().BeFalse();
        ConfigDirty.IsDirtyStatusTab(pending, saved).Should().BeFalse();
    }

    private static AppConfig JsonRoundTrip(AppConfig src)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(src);
        return JsonSerializer.Deserialize<AppConfig>(bytes)
            ?? throw new System.InvalidOperationException("round-trip failed");
    }
}
