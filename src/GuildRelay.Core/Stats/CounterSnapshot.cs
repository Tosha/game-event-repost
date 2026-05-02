namespace GuildRelay.Core.Stats;

public sealed record CounterSnapshot(string Label, double Total, double Last60Min);
