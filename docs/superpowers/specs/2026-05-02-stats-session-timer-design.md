# Stats Window Session Timer — Design

**Status:** Design approved, implementation plan pending
**Date:** 2026-05-02
**Author:** Brainstormed with Claude Code

---

## 1. Goal

Add a session-elapsed timer to the Stats window so users can see how long the current counting session has been running. The timer drops to `0:00` whenever counters are reset — both per-row Reset and Reset all — so a "fresh session" visibly starts the clock over.

## 2. Why

Counters in the Stats window are session-only: they accumulate from the moment the watcher starts (or the last reset) until the user resets again. Without a timer, the user has no way to gauge the time horizon of those counts. "1240 Glory in this session" is meaningless without knowing whether the session is 5 minutes or 5 hours.

## 3. Behaviour

- **Initial state**: timer starts at `0:00` when the `StatsAggregator` is constructed (effectively at app startup).
- **Steady state**: ticks up once per second, displayed in the footer of the Stats window.
- **Per-row Reset**: clicking the per-counter Reset button drops the timer back to `0:00` and resumes counting up.
- **Reset all**: clicking the global Reset all button drops the timer back to `0:00` and resumes.
- **No persistence**: the timer is not saved across app restarts. Restarting the app starts a new session at `0:00`, matching the existing session-only counter semantics.

**Caveat (deliberate):** in a multi-rule setup, resetting one counter row drops the global timer even though other counters still show accumulated values. That mismatch is the deliberate consequence of "any reset drops the timer." If it proves confusing in practice, the alternative ("Reset all drops the timer; per-row Reset leaves it alone") is a single-line change in `StatsAggregator.Reset(label)`.

## 4. Display

**Format:**
- Under 1 hour: `m:ss` (e.g., `5:23`).
- 1 hour or more: `h:mm:ss` (e.g., `1:05:23`).

**Position:** new `TextBlock` in the existing footer of `StatsWindow.xaml`, positioned between the "Stats: ON/OFF" badge and the Always-on-top checkbox. Reads `Session: 5:23`.

**Update rate:** 1 Hz, piggybacking on the existing `DispatcherTimer` that already updates row totals — no new timer needed.

## 5. Implementation

### 5.1 `IStatsAggregator` (Core)

Add a property:

```csharp
DateTimeOffset SessionStart { get; }
```

### 5.2 `StatsAggregator` (Core)

- New private field `_sessionStart: DateTimeOffset`.
- Constructor takes an optional `Func<DateTimeOffset> clock` (defaulting to `() => DateTimeOffset.UtcNow`) so tests can inject a deterministic clock. `_sessionStart = _clock()` at construction.
- `Reset(string label)` updates `_sessionStart = _clock()` (under the existing lock).
- `ResetAll()` updates `_sessionStart = _clock()` (under the existing lock).
- `SessionStart` property returns `_sessionStart` under the lock.

### 5.3 `StatsViewModel` (App)

New property `SessionElapsedText: string`, computed in `Refresh()`:

```csharp
var elapsed = _clock() - _aggregator.SessionStart;
SessionElapsedText = elapsed >= TimeSpan.FromHours(1)
    ? elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
    : elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);
```

Negative elapsed (clock skew or pathological cases) clamps to `0:00`.

### 5.4 `StatsWindow.xaml`

Add a `TextBlock` named `SessionTimerText` in the footer's `DockPanel`, positioned between `BadgeText` and `AlwaysOnTopCheck`. Renders `Session: {SessionElapsedText}`.

The existing 1 Hz `DispatcherTimer.Tick → Refresh()` updates `SessionTimerText.Text` from `_vm.SessionElapsedText`.

## 6. Testing

### `StatsAggregatorTests` (Core.Tests)

- `SessionStartIsCapturedAtConstruction`: instantiate with a fake clock returning `T0`; assert `aggregator.SessionStart == T0`.
- `ResetUpdatesSessionStart`: instantiate with a clock that returns `T0` then `T1`; record some events; call `Reset("Glory")`; assert `SessionStart == T1`.
- `ResetAllUpdatesSessionStart`: same shape but with `ResetAll()`.
- `RecordDoesNotChangeSessionStart`: assert calling `Record` keeps `SessionStart` unchanged.

### `StatsViewModelTests` (App.Tests)

- `SessionElapsedTextFormatsSubHourAsMinutesSeconds`: with mock clock and aggregator, set elapsed to 5m23s; assert `SessionElapsedText == "5:23"`.
- `SessionElapsedTextFormatsOverHourAsHoursMinutesSeconds`: elapsed 1h05m23s; assert `"1:05:23"`.
- `SessionElapsedTextIsZeroAtConstruction`: with elapsed = 0; assert `"0:00"`.
- `SessionElapsedTextClampsToZeroWhenNegative`: with elapsed = -1s (clock skew); assert `"0:00"`.

## 7. Out of scope

- Persisting the session timer across app restarts.
- Per-counter session timers (each row tracking its own start time).
- A pause/resume button for the timer.
- Configurable timer format (always `m:ss` / `h:mm:ss`).

## 8. Anti-cheat compliance

Pure UI / in-memory bookkeeping change. No new screen capture, audio capture, or process interaction. Fully compliant with the project's anti-cheat policy.

## 9. Open questions

None. The single ambiguity ("does per-row Reset drop the timer?") was resolved during brainstorming — yes.
