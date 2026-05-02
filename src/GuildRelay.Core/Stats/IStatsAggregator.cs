using System;
using System.Collections.Generic;

namespace GuildRelay.Core.Stats;

public interface IStatsAggregator
{
    void Record(string label, double value, DateTimeOffset at);
    void Reset(string label);
    void ResetAll();
    IReadOnlyList<CounterSnapshot> Snapshot(DateTimeOffset now);
}
