using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Features;
using Xunit;

namespace GuildRelay.Core.Tests.Features;

public class FeatureRegistryApplyConfigTests
{
    private sealed class SpyFeature : IFeature
    {
        public string Id { get; }
        public SpyFeature(string id) { Id = id; }
        public string DisplayName => Id;
        public FeatureStatus Status => FeatureStatus.Idle;
        public JsonElement? LastApplied { get; private set; }
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public void ApplyConfig(JsonElement featureConfig) => LastApplied = featureConfig.Clone();
        public event EventHandler<StatusChangedArgs>? StatusChanged { add { } remove { } }
    }

    [Fact]
    public async Task ApplyConfigAsyncForwardsToRegisteredFeature()
    {
        var feature = new SpyFeature("chat");
        var registry = new FeatureRegistry();
        registry.Register(feature);
        using var doc = JsonDocument.Parse("""{"Enabled":true}""");

        await registry.ApplyConfigAsync("chat", doc.RootElement);

        feature.LastApplied.HasValue.Should().BeTrue();
        feature.LastApplied!.Value.GetProperty("Enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ApplyConfigAsyncNoOpsForUnknownFeature()
    {
        var registry = new FeatureRegistry();
        using var doc = JsonDocument.Parse("{}");

        Func<Task> act = async () => await registry.ApplyConfigAsync("missing", doc.RootElement);

        await act.Should().NotThrowAsync();
    }
}
