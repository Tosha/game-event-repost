using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GuildRelay.Core.Features;

public interface IFeatureRegistry
{
    Task StartAsync(string id, CancellationToken ct);
    Task StopAsync(string id);
    Task ApplyConfigAsync(string id, JsonElement featureConfig);
}
