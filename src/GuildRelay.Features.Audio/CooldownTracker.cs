using System;
using System.Collections.Generic;

namespace GuildRelay.Features.Audio;

/// <summary>
/// Tracks per-rule cooldowns. A rule can only fire if its cooldown
/// has expired since the last fire. Thread-safe via lock.
/// </summary>
public sealed class CooldownTracker
{
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, DateTimeOffset> _lastFired = new();
    private readonly object _lock = new();

    public CooldownTracker(Func<DateTimeOffset>? timeProvider = null)
    {
        _now = timeProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public bool TryFire(string ruleId, TimeSpan cooldown)
    {
        lock (_lock)
        {
            var now = _now();
            if (_lastFired.TryGetValue(ruleId, out var last) && now - last < cooldown)
                return false;
            _lastFired[ruleId] = now;
            return true;
        }
    }

    public void Reset()
    {
        lock (_lock) { _lastFired.Clear(); }
    }
}
