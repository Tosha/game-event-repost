using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Features;
using Xunit;

namespace GuildRelay.Core.Tests.Features;

public class FeatureRegistryTests
{
    [Fact]
    public async Task StartingRegisteredFeatureCallsStartAsync()
    {
        var feature = new FakeFeature("chat");
        var registry = new FeatureRegistry();
        registry.Register(feature);

        await registry.StartAsync("chat", CancellationToken.None);

        feature.Started.Should().BeTrue();
    }

    [Fact]
    public async Task StartingUnknownFeatureIsNoOp()
    {
        var registry = new FeatureRegistry();

        var act = async () => await registry.StartAsync("ghost", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void AllReturnsRegisteredFeaturesInOrder()
    {
        var registry = new FeatureRegistry();
        registry.Register(new FakeFeature("chat"));
        registry.Register(new FakeFeature("audio"));

        registry.All.Should().HaveCount(2);
        registry.All[0].Id.Should().Be("chat");
        registry.All[1].Id.Should().Be("audio");
    }

    [Fact]
    public async Task StopAllStopsEveryFeature()
    {
        var chat = new FakeFeature("chat");
        var audio = new FakeFeature("audio");
        var registry = new FeatureRegistry();
        registry.Register(chat);
        registry.Register(audio);

        await registry.StartAsync("chat", CancellationToken.None);
        await registry.StartAsync("audio", CancellationToken.None);
        await registry.StopAllAsync();

        chat.Status.Should().Be(FeatureStatus.Idle);
        audio.Status.Should().Be(FeatureStatus.Idle);
    }

    [Fact]
    public void GetReturnsRegisteredFeatureById()
    {
        var registry = new FeatureRegistry();
        var chat = new FakeFeature("chat");
        registry.Register(chat);

        registry.Get("chat").Should().BeSameAs(chat);
        registry.Get("nonexistent").Should().BeNull();
    }

    private sealed class FakeFeature : IFeature
    {
        public FakeFeature(string id) { Id = id; }
        public string Id { get; }
        public string DisplayName => Id;
        public FeatureStatus Status { get; private set; } = FeatureStatus.Idle;
        public bool Started { get; private set; }
        public Task StartAsync(CancellationToken ct) { Started = true; Status = FeatureStatus.Running; return Task.CompletedTask; }
        public Task StopAsync() { Status = FeatureStatus.Idle; return Task.CompletedTask; }
        public void ApplyConfig(JsonElement featureConfig) { }
#pragma warning disable CS0067 // Event required by IFeature but intentionally unused in fake
        public event EventHandler<StatusChangedArgs>? StatusChanged;
#pragma warning restore CS0067
    }
}
