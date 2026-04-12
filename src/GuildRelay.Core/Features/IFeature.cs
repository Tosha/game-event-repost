using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GuildRelay.Core.Features;

public interface IFeature
{
    string Id { get; }
    string DisplayName { get; }
    FeatureStatus Status { get; }

    Task StartAsync(CancellationToken ct);
    Task StopAsync();
    void ApplyConfig(JsonElement featureConfig);

    event EventHandler<StatusChangedArgs>? StatusChanged;
}
