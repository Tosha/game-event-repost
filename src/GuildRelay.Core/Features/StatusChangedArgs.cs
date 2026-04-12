using System;

namespace GuildRelay.Core.Features;

public sealed class StatusChangedArgs : EventArgs
{
    public StatusChangedArgs(FeatureStatus previous, FeatureStatus current, string? message)
    {
        Previous = previous;
        Current = current;
        Message = message;
    }

    public FeatureStatus Previous { get; }
    public FeatureStatus Current { get; }
    public string? Message { get; }
}
