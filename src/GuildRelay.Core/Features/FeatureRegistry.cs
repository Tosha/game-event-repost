using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GuildRelay.Core.Features;

public sealed class FeatureRegistry : IFeatureRegistry
{
    private readonly List<IFeature> _features = new();

    public IReadOnlyList<IFeature> All => _features;

    public void Register(IFeature feature) => _features.Add(feature);

    public IFeature? Get(string id) => _features.FirstOrDefault(f => f.Id == id);

    public async Task StartAsync(string id, CancellationToken ct)
    {
        var feature = Get(id);
        if (feature is null) return;
        await feature.StartAsync(ct).ConfigureAwait(false);
    }

    public async Task StopAsync(string id)
    {
        var feature = Get(id);
        if (feature is null) return;
        await feature.StopAsync().ConfigureAwait(false);
    }

    public Task ApplyConfigAsync(string id, JsonElement featureConfig)
    {
        var feature = Get(id);
        if (feature is null) return Task.CompletedTask;
        feature.ApplyConfig(featureConfig);
        return Task.CompletedTask;
    }

    public async Task StopAllAsync()
    {
        foreach (var f in _features)
            await f.StopAsync().ConfigureAwait(false);
    }
}
