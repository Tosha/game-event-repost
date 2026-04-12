using System.Threading;
using System.Threading.Tasks;
using GuildRelay.Core.Events;

namespace GuildRelay.Core.Publishing;

public interface IDiscordPublisher
{
    ValueTask PublishAsync(DetectionEvent evt, CancellationToken ct);
}
