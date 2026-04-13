using System;
using System.Collections.Generic;

namespace GuildRelay.Core.Features;

/// <summary>
/// Tracks per-key cooldowns. A key can only fire if its cooldown
/// has expired since the last fire. Thread-safe via lock.
/// Used by both Chat Watcher (per-rule cooldown) and Audio Watcher.
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

    public bool TryFire(string key, TimeSpan cooldown)
    {
        lock (_lock)
        {
            var now = _now();
            if (_lastFired.TryGetValue(key, out var last) && now - last < cooldown)
                return false;
            _lastFired[key] = now;
            return true;
        }
    }

    public void Reset()
    {
        lock (_lock) { _lastFired.Clear(); }
    }
}
