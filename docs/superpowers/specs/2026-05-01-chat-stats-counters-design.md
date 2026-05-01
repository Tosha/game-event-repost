# Chat Stats Counters — Design

**Status:** Design approved, implementation plan pending
**Date:** 2026-05-01
**Author:** Brainstormed with Claude Code

---

## 1. Goal

Reuse the Chat Watcher's existing OCR pipeline to drive **local-only analytics counters** alongside the existing Discord-event-repost pipeline. Counter examples: Glory gained per session, Standing gained, kills, etc. **Counter matches never trigger Discord posts.** A counter rule matching a chat line increments a local counter and nothing else. (A line that *also* matches an event-repost rule still posts to Discord through that rule — the two pipelines are independent. See §5.)

The user-facing model splits the Chat Watcher into two independently-toggleable sub-features:

- **Event Repost** (renamed from the existing "Chat Watcher" toggle) — chat lines matched against rules → posted to Discord.
- **Stats** (new) — chat lines matched against counter rules → aggregated locally, viewed in a dedicated Stats window.

## 2. Non-goals

- Persistence of counter values across app restarts. Session-only with manual reset is sufficient for v1.
- Cross-user/server-side aggregation.
- Stats for non-chat features (Audio, Status). Out of scope; the design does not preclude adding similar pipelines to those features later, but no infra is shared between watchers.
- Histogram/time-series visualization beyond `Total` and `Last 60 min`.
- A new top-level dependency. No additional NuGet packages required.

## 3. UI design

### 3.1 Chat Watcher tab restructure

```
┌─ Chat Watcher tab ───────────────────────────────────┐
│  Capture (shared by both sub-features)               │
│    Region picker · Capture interval · OCR confidence │
│    [Live View]                                       │
│                                                       │
│  ─────────────────────────────────────────────────── │
│  ☐ Event Repost   (formerly "Chat Watcher" toggle)   │
│     Match chat lines and post to Discord.            │
│     · Default cooldown                               │
│     · Rules list [Add] [Edit] [Remove]               │
│     · Discord post template                          │
│                                                       │
│  ─────────────────────────────────────────────────── │
│  ☐ Stats   (NEW)                                     │
│     Match chat lines and count locally.              │
│     · Counter rules list [Add] [Edit] [Remove]       │
│     · [Open Stats Window]                            │
└───────────────────────────────────────────────────────┘
```

Shared OCR settings (region, capture interval, confidence threshold, Live View) move to the top "Capture" section so they aren't visually owned by either sub-feature.

The capture loop runs whenever **either** sub-toggle is on; nothing runs when both are off. The active-feature green dot on the Chat tab header lights up if either sub-toggle is on.

### 3.2 Counter rule editor

Mirrors the existing rule-editor visual pattern. Fields:

- **Label** — display name in the Stats window. Aggregation key (see §4.4).
- **Channels** — multi-select, defaults to `Game`. Empty list = wildcard (any channel).
- **Pattern** — e.g. `You gained {value} Glory.`
- **Match mode** — `Template` (default) | `Regex`.

### 3.3 Stats window

Modeless WPF FluentWindow (matches `DebugLiveView` style):

| Counter | Total (session) | Last 60 min | Reset |
|---|---|---|---|
| Glory | 1,240 | 320 | [×] |

Footer: `[Reset all]` button, `[ ] Always on top` checkbox, status badge `Stats: ON / OFF`.

**Empty states:**
- No counter rules configured → "No counter rules. Configure in Chat tab → Stats."
- Rules exist, no matches yet → labels with zeros (so the user sees what they're tracking before the first match).

**Open from:**
- "Open Stats Window" button in the Stats section of the Chat tab.
- "View Stats" entry in the tray context menu.

Both handlers route through a single window controller. Single instance — reopening focuses the existing window.

## 4. Data model

### 4.1 Config schema

`GuildRelay.Core/Config/ChatConfig.cs`:

```csharp
public sealed record ChatConfig(
    bool EventRepostEnabled,        // renamed from Enabled
    bool StatsEnabled,              // NEW
    int CaptureIntervalSec,
    double OcrConfidenceThreshold,
    int DefaultCooldownSec,
    RegionConfig Region,
    List<PreprocessStageConfig> PreprocessPipeline,
    List<StructuredChatRule> Rules,           // event-repost rules (unchanged)
    List<CounterRule> CounterRules,           // NEW
    Dictionary<string, string> Templates);    // Discord templates (unchanged)
```

`GuildRelay.Core/Config/CounterRule.cs` (new):

```csharp
public enum CounterMatchMode { Template, Regex }

public sealed record CounterRule(
    string Id,
    string Label,
    List<string> Channels,
    string Pattern,
    CounterMatchMode MatchMode);
```

### 4.2 Pattern compilation

`GuildRelay.Features.Chat/CounterRuleCompiler.cs` (new) compiles each `CounterRule` to a `Regex` once at construction:

- **Template mode:** split `Pattern` on the literal `{value}`, `Regex.Escape` each piece, splice `(?<value>-?\d+(?:\.\d+)?)` between them, wrap in `^…$`. Compiled with `RegexOptions.Compiled | RegexOptions.IgnoreCase`.
- **Regex mode:** pattern used as-is. Look for a `(?<value>…)` named group at match time.
- **No `{value}` / no `(?<value>…)`:** count-only rule. Each match contributes value = 1.

### 4.3 Built-in default

`ChatConfig.Default.CounterRules` ships a single Glory rule:

```csharp
new CounterRule(
    Id: "mo2_glory",
    Label: "Glory",
    Channels: new() { "Game" },
    Pattern: "You gained {value} Glory.",
    MatchMode: CounterMatchMode.Template);
```

Stats is OFF by default, so this rule sits dormant until the user enables Stats.

### 4.4 Aggregator

`GuildRelay.Core/Stats/StatsAggregator.cs` (new):

```csharp
public sealed class StatsAggregator
{
    public void Record(string label, double value, DateTimeOffset at);
    public void Reset(string label);
    public void ResetAll();
    public IReadOnlyList<CounterSnapshot> Snapshot(DateTimeOffset now);
}

public sealed record CounterSnapshot(string Label, double Total, double Last60Min);
```

- **Aggregation key:** `Label.Trim()` lower-cased. Two rules labelled "Glory" and "glory " feed the same row, letting users define multiple patterns for one metric without a `List<string>` patterns field.
- **Rolling window:** per counter, the aggregator stores `(timestamp, value)` tuples. `Snapshot` sums those with `at > now - 60min` (strict — events at exactly the 60-min boundary are excluded). Older entries are trimmed on each `Record`/`Snapshot` to bound memory.
- **Concurrency:** single internal `lock`. Single writer (Chat Watcher capture loop), readers are the UI 1 Hz timer and reset buttons. `Snapshot` returns an immutable list so the UI can iterate lock-free.
- **Persistence:** none. Values are session-only.
- **Ownership:** a single `StatsAggregator` instance is created in `CoreHost.CreateAsync`, injected into the `ChatWatcher` constructor (writer) and exposed on `CoreHost` for the Stats window (reader). It survives config reloads — editing a counter rule does not clear accumulated values.

## 5. Capture loop integration

The existing flow in [ChatWatcher.cs:122](src/GuildRelay.Features.Chat/ChatWatcher.cs:122) — capture → preprocess → OCR → normalize → assemble → dedup → match → cooldown → publish — is preserved. After dedup, parsed messages fan out to two independent consumers:

```
deduped message
   ├── EventRepostEnabled? → ChannelMatcher → cooldown → EventBus → Discord  (existing)
   └── StatsEnabled?       → CounterMatcher → StatsAggregator.Record         (new)
```

A line that matches both an event-repost rule and a counter rule fires both pipelines independently — no suppression. Users avoid the overlap in practice by writing counter rules for system messages they don't repost (`[Game] You gained …`) and event rules for player chat they do repost.

`CounterMatcher` (`GuildRelay.Features.Chat/CounterMatcher.cs`, new) is structurally symmetric to [`ChannelMatcher`](src/GuildRelay.Features.Chat/ChannelMatcher.cs):

```csharp
public sealed record CounterMatchResult(string Label, double Value);

public sealed class CounterMatcher
{
    public CounterMatcher(IEnumerable<CounterRule> rules);
    public CounterMatchResult? Match(ParsedChatLine parsed);
}
```

Indexed by channel for fast lookup; wildcard rules tried after channel-specific rules. First match wins on ambiguity.

### 5.1 Lifecycle / apply-config

- `ChatWatcher.ApplyConfig` rebuilds both `_matcher` (existing) and a new `_counterMatcher` from the freshly-deserialized config.
- `CoreHost.CreateAsync` start gate becomes `(EventRepostEnabled || StatsEnabled) && !Region.IsEmpty`.
- `ConfigApplyPipeline` starts/stops the watcher as that combined flag flips.
- Disabling Stats while the watcher is running: capture loop simply skips the Stats branch on subsequent ticks. Counter values are **not cleared** — re-enabling resumes from the same totals. Clearing requires explicit Reset.

### 5.2 Capture region missing

Unchanged: `if (_config.Region.IsEmpty) return;` early-exits the tick. Both sub-features are no-ops without a region picked.

### 5.3 Live View diagnostics

The existing `ChatTickDebugInfo` (consumed by `DebugLiveView`) is extended to surface counter-match outcomes alongside event-repost outcomes. New entries in `MatchResults`:

- `COUNTED [Glory: 80] rows X-Y: <text>` — counter rule matched, value extracted.
- `COUNTED [Glory: 1] rows X-Y: <text>` — count-only counter rule matched (no `{value}`).

This makes the Live View a debugging surface for both pipelines without separate UI.

## 6. Stats window behaviour

Window file: `GuildRelay.App/Stats/StatsWindow.xaml` (+ `.cs`).

- **Refresh:** `DispatcherTimer` at 1 Hz, runs only while the window is visible. Tick → `_aggregator.Snapshot(DateTimeOffset.UtcNow)` → diff into row VMs.
- **View model:** logic-heavy parts (snapshot → row VM mapping, "Stats: ON/OFF" badge, empty-state branching) live in `StatsViewModel` taking `IStatsAggregator` and a `Func<DateTimeOffset>` clock. The XAML code-behind is a thin wiring shell, mirroring the `RuleEditorLogic.cs` ↔ `RuleEditorWindow.xaml.cs` split.
- **Reset:** per-row Reset → `aggregator.Reset(label)` clears total + rolling history for that label. Reset all → `aggregator.ResetAll()`. No confirmation dialog.
- **Single-instance lifecycle:** held as a nullable field on `App.xaml.cs` (or a small `StatsWindowController` if cleaner). Open handler: if existing instance is alive, `Activate()`; else instantiate fresh. Closing the window does not affect aggregation.
- **Stats: OFF semantics:** the badge reflects the `StatsEnabled` config flag. When toggled off, the window keeps showing accumulated values (not zeros) so a temporary disable doesn't surprise-clear the session count.

## 7. Testing strategy

TDD per the project's `superpowers:test-driven-development` rule. Tests authored before implementation.

| Project | New / extended tests |
|---|---|
| `GuildRelay.Core.Tests` | `CounterRuleCompilerTests`, `StatsAggregatorTests`, `ConfigEqualityTests` extension, `ChatConfigMigrationTests` |
| `GuildRelay.Features.Chat.Tests` | `CounterMatcherTests`, `ChatWatcherStatsIntegrationTests` |
| `GuildRelay.App` | `StatsViewModelTests` (pure VM, no WPF) |

### 7.1 Key cases

**Template compilation:**
- `You gained {value} Glory.` matches `You gained 80 Glory.` extracting `80`.
- Literal regex chars escaped (`Mana (HP)` literal parens, trailing `.`).
- Negative values (`-5`), decimals (`1.5`) parse.
- No `{value}` placeholder → count-only with value = 1.
- Anchored `^…$`, case-insensitive.

**CounterMatcher:**
- Channel-scoped: a Game-channel rule does not match a Say line.
- Wildcard (empty channels) matches any channel.
- No-match returns null; first matching rule wins on ambiguity.

**StatsAggregator:**
- Aggregation key trimmed + case-insensitive (`"Glory"`, `"glory "` feed same row).
- 60-min rolling-window edge: events at exactly `now - 60min` excluded.
- Reset clears total + rolling history; ResetAll clears all counters.
- Concurrent Record/Snapshot don't deadlock or corrupt state (basic stress test, ≥1000 ops across two threads).

**ChatWatcher integration** (using existing fakes):
- Inject `[22:31:45][Game] You gained 80 Glory.` via fake OCR. Assert:
  - `Record("Glory", 80, _)` called when `StatsEnabled = true`.
  - No `Record` call when `StatsEnabled = false`.
  - When a line matches both an event-repost rule and a counter rule, both fire.
  - Dedup'd lines (same OCR line on consecutive ticks) don't double-count.

### 7.2 Manual smoke checklist (UI)

Documented in the implementation plan, run before merge:

- Open Stats window from tray → counter rows visible.
- Open Stats window from config-tab button → same window focused (single-instance).
- With Stats ON, manually trigger an OCR'd Glory line via screen capture → counter increments within 1 second.
- Click per-row Reset → that counter zeroes; others unchanged.
- Click Reset all → all counters zero.
- Toggle Stats OFF → badge flips to "Stats: OFF", existing values remain visible.

## 8. Migration plan

### 8.1 Config field migration

Existing user configs serialize `Enabled: bool` (the old Chat Watcher toggle) and have no `EventRepostEnabled`, `StatsEnabled`, or `CounterRules` fields.

`ConfigStore.LoadOrCreateDefaultsAsync` performs a JSON-level migration on load:

1. If JSON has `Enabled` and lacks `EventRepostEnabled`, copy `Enabled → EventRepostEnabled`.
2. Default `StatsEnabled = false`.
3. Default `CounterRules = [Glory built-in]` if missing (harmless because Stats is off; user sees the rule pre-populated when they enable Stats).

The migration is unit-tested with a hand-crafted pre-rename JSON blob through the loader.

### 8.2 Equality and dirty-tracking

`ConfigEquality.cs` updated to compare `CounterRules` (structural list comparison, same pattern as existing `Rules`) and `StatsEnabled` (scalar). The `ConfigDirty` machinery picks up changes automatically once equality is correct.

### 8.3 Architecture doc update

Per CLAUDE.md ("if you need to deviate from the architecture doc, update the doc first"), the implementation plan includes a small edit to [`docs/superpowers/specs/2026-04-11-guild-event-relay-architecture.md`](docs/superpowers/specs/2026-04-11-guild-event-relay-architecture.md) noting that Chat Watcher now hosts two parallel pipelines (event-repost and stats) sharing the OCR work. No new top-level dependency is introduced.

## 9. Anti-cheat compliance

This change adds no new screen capture, no new audio capture, no process interaction. It reuses the existing GDI BitBlt + Windows.Media.Ocr pipeline already in use by Chat Watcher, and adds an in-memory aggregator. Fully compliant with the §3.6 anti-cheat policy.

## 10. Open questions

None. All design decisions resolved during brainstorming. The implementation plan can proceed directly from this spec.
