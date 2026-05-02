using System;
using System.Collections.Generic;

namespace GuildRelay.Core.Stats;

public interface IStatsAggregator
{
    /// <summary>
    /// Wall-clock time when the current session started. Captured at the
    /// aggregator's construction and updated on any reset (per-counter or
    /// global). The Stats window renders elapsed time as <c>now - SessionStart</c>.
    /// </summary>
    DateTimeOffset SessionStart { get; }

    void Record(string label, double value, DateTimeOffset at);
    void Reset(string label);
    void ResetAll();
    IReadOnlyList<CounterSnapshot> Snapshot(DateTimeOffset now);
}
