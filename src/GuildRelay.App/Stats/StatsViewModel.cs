using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GuildRelay.Core.Config;
using GuildRelay.Core.Stats;

namespace GuildRelay.App.Stats;

public sealed record CounterRowVm(string Label, double Total, double Last60Min);

public sealed class StatsViewModel
{
    private readonly IStatsAggregator _aggregator;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<IReadOnlyList<CounterRule>> _rulesProvider;
    private readonly Func<bool> _statsEnabledProvider;

    public StatsViewModel(
        IStatsAggregator aggregator,
        Func<DateTimeOffset> clock,
        Func<IReadOnlyList<CounterRule>> rulesProvider,
        Func<bool> statsEnabledProvider)
    {
        _aggregator = aggregator;
        _clock = clock;
        _rulesProvider = rulesProvider;
        _statsEnabledProvider = statsEnabledProvider;
        Rows = Array.Empty<CounterRowVm>();
        BadgeState = "Stats: OFF";
    }

    public IReadOnlyList<CounterRowVm> Rows { get; private set; }
    public string BadgeState { get; private set; }
    public bool HasNoRules { get; private set; }
    public string SessionElapsedText { get; private set; } = "0:00";

    public void Refresh()
    {
        BadgeState = _statsEnabledProvider() ? "Stats: ON" : "Stats: OFF";

        var rules = _rulesProvider();
        var snap = _aggregator.Snapshot(_clock());
        var byLabel = snap.ToDictionary(s => Canonical(s.Label), s => s, StringComparer.OrdinalIgnoreCase);

        var rows = new List<CounterRowVm>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            var key = Canonical(rule.Label);
            if (!seen.Add(key)) continue;
            if (byLabel.TryGetValue(key, out var s))
                rows.Add(new CounterRowVm(s.Label, s.Total, s.Last60Min));
            else
                rows.Add(new CounterRowVm(rule.Label.Trim(), 0, 0));
        }

        // Surface counters that have data but whose rule was removed.
        foreach (var s in snap)
        {
            var key = Canonical(s.Label);
            if (seen.Add(key))
                rows.Add(new CounterRowVm(s.Label, s.Total, s.Last60Min));
        }

        Rows = rows;
        HasNoRules = rules.Count == 0 && rows.Count == 0;

        var elapsed = _clock() - _aggregator.SessionStart;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        SessionElapsedText = elapsed >= TimeSpan.FromHours(1)
            ? elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    public void ResetCounter(string label) => _aggregator.Reset(label);
    public void ResetAll() => _aggregator.ResetAll();

    private static string Canonical(string label) => label.Trim().ToLowerInvariant();
}
