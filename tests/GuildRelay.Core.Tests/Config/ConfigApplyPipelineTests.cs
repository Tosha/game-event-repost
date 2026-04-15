using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Config;
using GuildRelay.Core.Features;
using Xunit;

namespace GuildRelay.Core.Tests.Config;

public class ConfigApplyPipelineTests
{
    private sealed class SpyRegistry : IFeatureRegistry
    {
        public List<string> Calls { get; } = new();

        public Task StartAsync(string id, CancellationToken ct)
        {
            Calls.Add($"start:{id}");
            return Task.CompletedTask;
        }

        public Task StopAsync(string id)
        {
            Calls.Add($"stop:{id}");
            return Task.CompletedTask;
        }

        public Task ApplyConfigAsync(string id, JsonElement featureConfig)
        {
            Calls.Add($"apply:{id}");
            return Task.CompletedTask;
        }
    }

    private static AppConfig BaselineEnabled() => AppConfig.Default with
    {
        Chat = ChatConfig.Default with
        {
            Enabled = true,
            Region = new RegionConfig(0, 0, 100, 100, 96, new ResolutionConfig(1920, 1080), "PRIMARY")
        },
        Audio = AudioConfig.Default with { Enabled = true },
        Status = StatusConfig.Default with
        {
            Enabled = true,
            Region = new RegionConfig(0, 0, 50, 50, 96, new ResolutionConfig(1920, 1080), "PRIMARY")
        }
    };

    [Fact]
    public async Task IdenticalConfigsProduceNoCalls()
    {
        var registry = new SpyRegistry();
        var cfg = BaselineEnabled();

        await ConfigApplyPipeline.DispatchAsync(cfg, cfg, registry, CancellationToken.None);

        registry.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ChatEnabledTrueToFalseStopsOnly()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        var newCfg = oldCfg with { Chat = oldCfg.Chat with { Enabled = false } };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("stop:chat");
    }

    [Fact]
    public async Task ChatEnabledFalseToTrueAppliesThenStarts()
    {
        var registry = new SpyRegistry();
        var baseline = BaselineEnabled();
        var oldCfg = baseline with { Chat = baseline.Chat with { Enabled = false } };
        var newCfg = baseline;

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("apply:chat", "start:chat");
    }

    [Fact]
    public async Task ChatCaptureIntervalChangeForcesRestart()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        var newCfg = oldCfg with { Chat = oldCfg.Chat with { CaptureIntervalSec = oldCfg.Chat.CaptureIntervalSec + 1 } };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("apply:chat", "stop:chat", "start:chat");
    }

    [Fact]
    public async Task ChatRulesChangeHotAppliesWithoutRestart()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        var newCfg = oldCfg with
        {
            Chat = oldCfg.Chat with
            {
                Rules = new List<StructuredChatRule>
                {
                    new("r1", "Test", new List<string>(), new List<string> { "inc" }, MatchMode.ContainsAny)
                }
            }
        };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("apply:chat");
    }

    [Fact]
    public async Task ChatRegionChangeHotAppliesWithoutRestart()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        var newCfg = oldCfg with
        {
            Chat = oldCfg.Chat with
            {
                Region = new RegionConfig(10, 10, 200, 200, 96, new ResolutionConfig(1920, 1080), "PRIMARY")
            }
        };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("apply:chat");
    }

    [Fact]
    public async Task StatusCaptureIntervalChangeForcesRestart()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        var newCfg = oldCfg with { Status = oldCfg.Status with { CaptureIntervalSec = oldCfg.Status.CaptureIntervalSec + 1 } };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("apply:status", "stop:status", "start:status");
    }

    [Fact]
    public async Task StatusDebounceSamplesChangeForcesRestart()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        var newCfg = oldCfg with { Status = oldCfg.Status with { DebounceSamples = oldCfg.Status.DebounceSamples + 1 } };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("apply:status", "stop:status", "start:status");
    }

    [Fact]
    public async Task AudioRulesChangeHotAppliesWithoutRestart()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        var newCfg = oldCfg with
        {
            Audio = oldCfg.Audio with
            {
                Rules = new List<AudioRuleConfig>
                {
                    new("boom", "Boom", "clips/boom.wav", 0.8, 30)
                }
            }
        };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("apply:audio");
    }

    [Fact]
    public async Task DisabledFeatureChangesProduceNoRuntimeCalls()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        oldCfg = oldCfg with { Chat = oldCfg.Chat with { Enabled = false } };
        var newCfg = oldCfg with { Chat = oldCfg.Chat with { CaptureIntervalSec = 42 } };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task MultipleFeaturesDispatchIndependently()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        var newCfg = oldCfg with
        {
            Chat = oldCfg.Chat with
            {
                Rules = new List<StructuredChatRule>
                {
                    new("r", "Lbl", new List<string>(), new List<string> { "x" }, MatchMode.ContainsAny)
                }
            },
            Status = oldCfg.Status with { CaptureIntervalSec = oldCfg.Status.CaptureIntervalSec + 1 }
        };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("apply:chat", "apply:status", "stop:status", "start:status");
    }
}
