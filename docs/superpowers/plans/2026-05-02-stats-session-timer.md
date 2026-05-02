# Stats Session Timer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a session-elapsed timer to the Stats window footer that ticks up at 1 Hz and drops to `0:00` on any reset (per-counter Reset or Reset all).

**Architecture:** `StatsAggregator` gains a `SessionStart` property captured at construction and reset on either `Reset(label)` or `ResetAll()`. `StatsViewModel.Refresh()` computes `now - SessionStart` and formats it. `StatsWindow.xaml` adds a `TextBlock` in the footer; the existing 1 Hz `DispatcherTimer` updates it alongside the row data.

**Tech Stack:** .NET 8 / WPF. No new dependencies. Existing test projects (`GuildRelay.Core.Tests`, `GuildRelay.App.Tests`) cover the new logic.

**Spec reference:** [`docs/superpowers/specs/2026-05-02-stats-session-timer-design.md`](../specs/2026-05-02-stats-session-timer-design.md)

**Branch:** `feature/session-timer` (already created from `origin/main`).

---

## File map

### Modified
- `src/GuildRelay.Core/Stats/IStatsAggregator.cs` — add `SessionStart` property to the contract.
- `src/GuildRelay.Core/Stats/StatsAggregator.cs` — capture session start at construction (clock-injectable); update on Reset/ResetAll.
- `src/GuildRelay.App/Stats/StatsViewModel.cs` — add `SessionElapsedText` property, populated in `Refresh()`.
- `src/GuildRelay.App/Stats/StatsWindow.xaml` — add a `TextBlock` in the footer.
- `src/GuildRelay.App/Stats/StatsWindow.xaml.cs` — write `SessionElapsedText` to the new `TextBlock` in `Refresh()`.
- `tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs` — add session-start tests.
- `tests/GuildRelay.App.Tests/Stats/StatsViewModelTests.cs` — add session-elapsed-text tests.

### Created
- None.

---

## Conventions

- Build: `dotnet build` from repo root. Expect 0 errors, 0 warnings.
- Tests: `dotnet test`. Expect existing 208 tests pass + 8 new tests added by this plan = 216.
- Working directory: `C:\Users\tosha\IdeaProjects\game-event-repost`.
- Branch: `feature/session-timer`.
- Commit per task. Conventional-commit style (`feat`, `test`).

---

## Task 1: `StatsAggregator.SessionStart` captured at construction

**Files:**
- Modify: `src/GuildRelay.Core/Stats/IStatsAggregator.cs`
- Modify: `src/GuildRelay.Core/Stats/StatsAggregator.cs`
- Modify: `tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs`:

```csharp
[Fact]
public void SessionStartIsCapturedAtConstruction()
{
    // Inject a fixed-time clock so the test is deterministic. The aggregator
    // should capture SessionStart at construction time.
    var t0 = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    var agg = new StatsAggregator(() => t0);

    agg.SessionStart.Should().Be(t0);
}

[Fact]
public void SessionStartIsCapturedAtConstructionWithDefaultClock()
{
    // Default constructor uses DateTimeOffset.UtcNow. Confirm SessionStart
    // is set to roughly "now" — tolerance of a few seconds for slow CI hosts.
    var before = DateTimeOffset.UtcNow;
    var agg = new StatsAggregator();
    var after = DateTimeOffset.UtcNow;

    agg.SessionStart.Should().BeOnOrAfter(before);
    agg.SessionStart.Should().BeOnOrBefore(after);
}

[Fact]
public void RecordDoesNotChangeSessionStart()
{
    var t0 = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    var agg = new StatsAggregator(() => t0);

    agg.Record("Glory", 80, t0.AddSeconds(1));
    agg.Record("Glory", 20, t0.AddSeconds(2));

    agg.SessionStart.Should().Be(t0);
}
```

- [ ] **Step 2: Run the test — confirm it fails (compile error)**

```
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~StatsAggregatorTests" --nologo 2>&1 | tail -10
```

Expected: COMPILE ERROR — `StatsAggregator` constructor doesn't take a `Func<DateTimeOffset>` and `SessionStart` doesn't exist yet.

- [ ] **Step 3: Update the interface**

Replace `src/GuildRelay.Core/Stats/IStatsAggregator.cs` with:

```csharp
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
```

- [ ] **Step 4: Update the implementation**

In `src/GuildRelay.Core/Stats/StatsAggregator.cs`:

a) Add a clock field next to the existing fields (after `_counters`):

```csharp
    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset _sessionStart;
```

b) Replace the existing parameterless constructor with two:

```csharp
    public StatsAggregator() : this(() => DateTimeOffset.UtcNow) { }

    public StatsAggregator(Func<DateTimeOffset> clock)
    {
        _clock = clock;
        _sessionStart = clock();
    }
```

c) Add the `SessionStart` property (near the top of the class, just after the field declarations):

```csharp
    public DateTimeOffset SessionStart
    {
        get { lock (_lock) return _sessionStart; }
    }
```

- [ ] **Step 5: Run the test — confirm it passes**

```
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~StatsAggregatorTests" --nologo 2>&1 | tail -5
```

Expected: all `StatsAggregatorTests` pass (the existing 9 + 3 new = 12).

Then full build: `dotnet build` — expect 0 errors. (The interface gains a property; existing implementations only `StatsAggregator` exists, so no callers break.)

- [ ] **Step 6: Commit**

```bash
git add src/GuildRelay.Core/Stats/IStatsAggregator.cs src/GuildRelay.Core/Stats/StatsAggregator.cs tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs
git commit -m "feat(core): StatsAggregator captures SessionStart at construction"
```

---

## Task 2: `Reset` and `ResetAll` update `SessionStart`

**Files:**
- Modify: `src/GuildRelay.Core/Stats/StatsAggregator.cs`
- Modify: `tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs`:

```csharp
[Fact]
public void ResetUpdatesSessionStart()
{
    var t0 = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    var t1 = t0.AddMinutes(5);
    DateTimeOffset clockValue = t0;
    var agg = new StatsAggregator(() => clockValue);

    agg.Record("Glory", 80, t0);
    clockValue = t1;
    agg.Reset("Glory");

    agg.SessionStart.Should().Be(t1);
}

[Fact]
public void ResetAllUpdatesSessionStart()
{
    var t0 = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    var t1 = t0.AddMinutes(5);
    DateTimeOffset clockValue = t0;
    var agg = new StatsAggregator(() => clockValue);

    agg.Record("Glory", 80, t0);
    agg.Record("Standing", 5, t0);
    clockValue = t1;
    agg.ResetAll();

    agg.SessionStart.Should().Be(t1);
}

[Fact]
public void ResetForUnknownLabelStillUpdatesSessionStart()
{
    // Spec: "any reset drops the timer" — including a Reset on a counter
    // that has no events recorded yet (the dictionary lookup misses).
    // Otherwise, Reset on an empty row would be inconsistent with Reset
    // on a populated row.
    var t0 = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    var t1 = t0.AddSeconds(30);
    DateTimeOffset clockValue = t0;
    var agg = new StatsAggregator(() => clockValue);

    clockValue = t1;
    agg.Reset("NonexistentCounter");

    agg.SessionStart.Should().Be(t1);
}
```

- [ ] **Step 2: Run the tests — confirm they fail**

```
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~StatsAggregatorTests" --nologo 2>&1 | tail -5
```

Expected: 3 new tests fail (`SessionStart` stays at `t0` because Reset/ResetAll don't update it yet).

- [ ] **Step 3: Update Reset and ResetAll**

In `src/GuildRelay.Core/Stats/StatsAggregator.cs`, modify the `Reset` method:

```csharp
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
            // Always update SessionStart, even if the label has no recorded
            // events. Per spec: any reset drops the timer.
            _sessionStart = _clock();
        }
    }
```

And `ResetAll`:

```csharp
    public void ResetAll()
    {
        lock (_lock)
        {
            foreach (var state in _counters.Values)
            {
                state.Total = 0;
                state.Events.Clear();
            }
            _sessionStart = _clock();
        }
    }
```

- [ ] **Step 4: Run the tests — confirm they pass**

```
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~StatsAggregatorTests" --nologo 2>&1 | tail -5
```

Expected: all `StatsAggregatorTests` pass (15 total).

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Core/Stats/StatsAggregator.cs tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs
git commit -m "feat(core): Reset and ResetAll drop the session timer"
```

---

## Task 3: `StatsViewModel.SessionElapsedText`

**Files:**
- Modify: `src/GuildRelay.App/Stats/StatsViewModel.cs`
- Modify: `tests/GuildRelay.App.Tests/Stats/StatsViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `tests/GuildRelay.App.Tests/Stats/StatsViewModelTests.cs`:

```csharp
[Fact]
public void SessionElapsedTextIsZeroAtConstruction()
{
    var clockValue = T0;
    var agg = new StatsAggregator(() => clockValue);
    var vm = new StatsViewModel(agg, () => clockValue, () => Array.Empty<CounterRule>(), () => true);

    vm.Refresh();

    vm.SessionElapsedText.Should().Be("0:00");
}

[Fact]
public void SessionElapsedTextFormatsSubHourAsMinutesSeconds()
{
    var clockValue = T0;
    var agg = new StatsAggregator(() => clockValue);
    clockValue = T0.AddMinutes(5).AddSeconds(23);
    var vm = new StatsViewModel(agg, () => clockValue, () => Array.Empty<CounterRule>(), () => true);

    vm.Refresh();

    vm.SessionElapsedText.Should().Be("5:23");
}

[Fact]
public void SessionElapsedTextFormatsOverHourAsHoursMinutesSeconds()
{
    var clockValue = T0;
    var agg = new StatsAggregator(() => clockValue);
    clockValue = T0.AddHours(1).AddMinutes(5).AddSeconds(23);
    var vm = new StatsViewModel(agg, () => clockValue, () => Array.Empty<CounterRule>(), () => true);

    vm.Refresh();

    vm.SessionElapsedText.Should().Be("1:05:23");
}

[Fact]
public void SessionElapsedTextClampsToZeroWhenNegative()
{
    // Pathological: the VM clock is somehow earlier than the aggregator's
    // SessionStart (e.g., NTP jump backward). Should not display a negative
    // duration; clamp to 0:00 for sanity.
    var clockValue = T0;
    var agg = new StatsAggregator(() => clockValue);
    clockValue = T0.AddSeconds(-5);
    var vm = new StatsViewModel(agg, () => clockValue, () => Array.Empty<CounterRule>(), () => true);

    vm.Refresh();

    vm.SessionElapsedText.Should().Be("0:00");
}

[Fact]
public void SessionElapsedTextResetsAfterResetAll()
{
    DateTimeOffset clockValue = T0;
    var agg = new StatsAggregator(() => clockValue);

    // Five minutes of session.
    clockValue = T0.AddMinutes(5);
    var vm = new StatsViewModel(agg, () => clockValue, () => Array.Empty<CounterRule>(), () => true);
    vm.Refresh();
    vm.SessionElapsedText.Should().Be("5:00");

    // ResetAll under a clock that's now 10 minutes after T0. SessionStart
    // jumps to 10:00. Refresh under same clock → elapsed = 0.
    vm.ResetAll();
    vm.Refresh();

    vm.SessionElapsedText.Should().Be("0:00");
}
```

- [ ] **Step 2: Run tests — confirm they fail (compile error: `SessionElapsedText` doesn't exist)**

```
dotnet test tests/GuildRelay.App.Tests --filter "FullyQualifiedName~StatsViewModelTests" --nologo 2>&1 | tail -5
```

- [ ] **Step 3: Implement `SessionElapsedText`**

Modify `src/GuildRelay.App/Stats/StatsViewModel.cs`:

a) Add a `using System.Globalization;` at the top.

b) Add a property next to the existing public properties:

```csharp
    public string SessionElapsedText { get; private set; } = "0:00";
```

c) Set it in `Refresh()`. Add this block at the end of `Refresh()`, after the `HasNoRules` line:

```csharp
        var elapsed = _clock() - _aggregator.SessionStart;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        SessionElapsedText = elapsed >= TimeSpan.FromHours(1)
            ? elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);
```

- [ ] **Step 4: Run tests — confirm they pass**

```
dotnet test tests/GuildRelay.App.Tests --filter "FullyQualifiedName~StatsViewModelTests" --nologo 2>&1 | tail -5
```

Expected: all `StatsViewModelTests` pass (the existing 7 + 5 new = 12).

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.App/Stats/StatsViewModel.cs tests/GuildRelay.App.Tests/Stats/StatsViewModelTests.cs
git commit -m "feat(app): StatsViewModel exposes SessionElapsedText"
```

---

## Task 4: Stats window footer renders the timer

**Files:**
- Modify: `src/GuildRelay.App/Stats/StatsWindow.xaml`
- Modify: `src/GuildRelay.App/Stats/StatsWindow.xaml.cs`

This task is UI-only; no automated tests (manual smoke at the end). The `StatsViewModel` already gives us `SessionElapsedText`; we just wire it into a TextBlock.

- [ ] **Step 1: Add the timer TextBlock to the footer**

Modify `src/GuildRelay.App/Stats/StatsWindow.xaml`. Find the footer `DockPanel`:

```xml
        <DockPanel Grid.Row="2" Margin="12,4,12,12">
            <ui:Button DockPanel.Dock="Right" Content="Reset all" Click="OnResetAllClick"/>
            <CheckBox x:Name="AlwaysOnTopCheck" Content="Always on top"
                      Margin="0,0,16,0" VerticalAlignment="Center"
                      Checked="OnAlwaysOnTopChanged" Unchecked="OnAlwaysOnTopChanged"/>
            <TextBlock x:Name="BadgeText" VerticalAlignment="Center"
                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                       Text="Stats: OFF"/>
        </DockPanel>
```

Replace it with (adds `SessionTimerText` between `BadgeText` and `AlwaysOnTopCheck`):

```xml
        <DockPanel Grid.Row="2" Margin="12,4,12,12">
            <ui:Button DockPanel.Dock="Right" Content="Reset all" Click="OnResetAllClick"/>
            <CheckBox x:Name="AlwaysOnTopCheck" Content="Always on top"
                      Margin="0,0,16,0" VerticalAlignment="Center"
                      Checked="OnAlwaysOnTopChanged" Unchecked="OnAlwaysOnTopChanged"/>
            <TextBlock x:Name="SessionTimerText" VerticalAlignment="Center"
                       Margin="16,0,16,0"
                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                       Text="Session: 0:00"/>
            <TextBlock x:Name="BadgeText" VerticalAlignment="Center"
                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                       Text="Stats: OFF"/>
        </DockPanel>
```

- [ ] **Step 2: Wire `SessionTimerText` in code-behind**

Modify `src/GuildRelay.App/Stats/StatsWindow.xaml.cs`. In the `Refresh()` method, find:

```csharp
    private void Refresh()
    {
        _vm.Refresh();
        CounterGrid.ItemsSource = _vm.Rows;
        BadgeText.Text = _vm.BadgeState;
        EmptyHint.Visibility = _vm.HasNoRules ? Visibility.Visible : Visibility.Collapsed;
        CounterGrid.Visibility       = _vm.HasNoRules ? Visibility.Collapsed : Visibility.Visible;
    }
```

Add the `SessionTimerText.Text` line at the bottom:

```csharp
    private void Refresh()
    {
        _vm.Refresh();
        CounterGrid.ItemsSource = _vm.Rows;
        BadgeText.Text = _vm.BadgeState;
        SessionTimerText.Text = "Session: " + _vm.SessionElapsedText;
        EmptyHint.Visibility = _vm.HasNoRules ? Visibility.Visible : Visibility.Collapsed;
        CounterGrid.Visibility       = _vm.HasNoRules ? Visibility.Collapsed : Visibility.Visible;
    }
```

- [ ] **Step 3: Build**

```
dotnet build --nologo 2>&1 | tail -3
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Run all tests as a regression check**

```
dotnet test --nologo 2>&1 | tail -3
```

Expected: 216 tests pass (208 prior + 8 new).

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.App/Stats/StatsWindow.xaml src/GuildRelay.App/Stats/StatsWindow.xaml.cs
git commit -m "feat(stats-window): render Session: m:ss timer in footer"
```

---

## Task 5: Manual smoke test

**Files:** none — runtime verification.

- [ ] **Step 1: Launch the app**

```
dotnet run --project src/GuildRelay.App
```

- [ ] **Step 2: Open the Stats window**

Right-click the tray icon → View Stats. (Or click "Open Stats Window" in the Chat tab's Stats section.)

- [ ] **Step 3: Confirm the timer is visible and ticking**

Footer should read `Session: 0:00` initially, and tick up by one second roughly every second. After 5 seconds it should say `Session: 0:05`. After ~1 minute it should read `1:00`. Format updates as the duration grows.

- [ ] **Step 4: Confirm Reset all drops the timer**

Click **Reset all**. Timer should immediately reset to `0:00` and start ticking up again.

- [ ] **Step 5: Confirm per-row Reset drops the timer**

(Requires at least one counter row to exist.) If your config doesn't already have a Glory rule, save the default config so the Glory built-in is present. Click the per-row **Reset** (×) button on the Glory row. Timer should drop to `0:00`.

- [ ] **Step 6: No commit needed** (unless smoke-testing surfaced a defect — fix and commit separately).

---

## Self-review checklist (post-write)

- [ ] Spec §3 behaviour (initial state, steady tick, both resets drop timer) → Tasks 1, 2, 4. ✓
- [ ] Spec §4 display (format `m:ss` / `h:mm:ss`, position in footer, 1 Hz update) → Tasks 3, 4. ✓
- [ ] Spec §5.1 IStatsAggregator interface change → Task 1 step 3. ✓
- [ ] Spec §5.2 StatsAggregator clock injection + Reset/ResetAll updates → Tasks 1, 2. ✓
- [ ] Spec §5.3 StatsViewModel SessionElapsedText property → Task 3. ✓
- [ ] Spec §5.4 StatsWindow.xaml TextBlock + Refresh wiring → Task 4. ✓
- [ ] Spec §6 testing — all four aggregator cases + four VM cases plus a Reset-on-unknown-label edge case + a multi-step "ResetAll resets timer" VM test (more than spec lists, all in spirit). ✓
- [ ] Spec §7 out of scope (persistence, per-counter timers, pause, configurable format) → none of the tasks touch these. ✓
