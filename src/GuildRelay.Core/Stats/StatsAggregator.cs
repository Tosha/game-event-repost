using System;
using System.Collections.Generic;
using System.Linq;

namespace GuildRelay.Core.Stats;

public sealed class StatsAggregator : IStatsAggregator
{
    private readonly object _lock = new();
    private readonly Dictionary<string, CounterState> _counters =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(60);

    public void Record(string label, double value, DateTimeOffset at)
    {
        var key = Canonical(label);
        if (key.Length == 0) return;
        lock (_lock)
        {
            if (!_counters.TryGetValue(key, out var state))
            {
                state = new CounterState(label.Trim());
                _counters[key] = state;
            }
            state.Total += value;
            state.Events.Add((at, value));
            Trim(state, at);
        }
    }

    public void Reset(string label)
    {
        var key = Canonical(label);
        lock (_lock)
        {
            if (_counters.TryGetValue(key, out var state))
            {
                state.Total = 0;
                state.Events.Clear();
            }
        }
    }

    public void ResetAll()
    {
        lock (_lock)
        {
            foreach (var state in _counters.Values)
            {
                state.Total = 0;
                state.Events.Clear();
            }
        }
    }

    public IReadOnlyList<CounterSnapshot> Snapshot(DateTimeOffset now)
    {
        lock (_lock)
        {
            var result = new List<CounterSnapshot>(_counters.Count);
            foreach (var state in _counters.Values)
            {
                Trim(state, now);
                double last60 = 0;
                foreach (var (at, value) in state.Events)
                    if (at > now - Window) last60 += value;
                result.Add(new CounterSnapshot(state.DisplayLabel, state.Total, last60));
            }
            return result;
        }
    }

    private static void Trim(CounterState state, DateTimeOffset now)
    {
        var cutoff = now - Window;
        state.Events.RemoveAll(e => e.At <= cutoff);
    }

    private static string Canonical(string label) => label.Trim().ToLowerInvariant();

    private sealed class CounterState
    {
        public CounterState(string displayLabel) { DisplayLabel = displayLabel; }
        public string DisplayLabel { get; }
        public double Total { get; set; }
        public List<(DateTimeOffset At, double Value)> Events { get; } = new();
    }
}
