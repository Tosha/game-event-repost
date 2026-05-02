# Chat Stats Counters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a local-only "Stats" pipeline to Chat Watcher that counts numeric values extracted from matched chat lines (e.g. Glory per session) without posting to Discord.

**Architecture:** Single Chat Watcher feature with two parallel post-dedup pipelines: existing Event Repost (Discord) and new Stats (in-memory aggregator + dedicated viewing window). User-facing: rename the Chat Watcher master toggle to "Event Repost" and add a sibling "Stats" toggle.

**Tech Stack:** .NET 8, WPF + WPF-UI (FluentWindow), xUnit + FluentAssertions, System.Text.RegularExpressions, System.Text.Json. No new NuGet dependencies.

**Spec reference:** [`docs/superpowers/specs/2026-05-01-chat-stats-counters-design.md`](../specs/2026-05-01-chat-stats-counters-design.md)

**Branch:** `feature/chat-stats-counters` (already created from `origin/main`).

---

## File map

### New files
- `src/GuildRelay.Core/Config/CounterRule.cs` — `CounterRule` record + `CounterMatchMode` enum
- `src/GuildRelay.Core/Stats/IStatsAggregator.cs` — interface
- `src/GuildRelay.Core/Stats/CounterSnapshot.cs` — record
- `src/GuildRelay.Core/Stats/StatsAggregator.cs` — implementation
- `src/GuildRelay.Features.Chat/CounterRuleCompiler.cs` — template/regex → `Regex`
- `src/GuildRelay.Features.Chat/CounterMatcher.cs` — channel-routed matcher
- `src/GuildRelay.App/Stats/StatsViewModel.cs` — VM (no WPF deps)
- `src/GuildRelay.App/Stats/StatsWindow.xaml` + `.xaml.cs` — view
- `src/GuildRelay.App/Stats/StatsWindowController.cs` — single-instance lifecycle
- `src/GuildRelay.App/Config/CounterRuleEditorWindow.xaml` + `.xaml.cs` — counter rule add/edit dialog
- `tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs`
- `tests/GuildRelay.Features.Chat.Tests/CounterRuleCompilerTests.cs`
- `tests/GuildRelay.Features.Chat.Tests/CounterMatcherTests.cs`
- `tests/GuildRelay.Features.Chat.Tests/ChatWatcherStatsTests.cs`
- `tests/GuildRelay.Core.Tests/Config/ChatConfigMigrationTests.cs`
- `tests/GuildRelay.App.Tests/Stats/StatsViewModelTests.cs` (new test project may be needed)

### Modified files
- `src/GuildRelay.Core/Config/ChatConfig.cs` — rename `Enabled` → `EventRepostEnabled`, add `StatsEnabled` + `CounterRules`, update `Default`
- `src/GuildRelay.Core/Config/ConfigEquality.cs` — extend `Equal(ChatConfig)`, add `Equal(CounterRule)`
- `src/GuildRelay.Core/Config/ConfigDirty.cs` — combined any-enabled, list-equal `CounterRules`
- `src/GuildRelay.Core/Config/ConfigApplyPipeline.cs` — combined any-enabled gate
- `src/GuildRelay.Core/Config/ConfigStore.cs` — JSON migration of `enabled` field
- `src/GuildRelay.Features.Chat/ChatWatcher.cs` — inject aggregator, fan out post-dedup, extend `ChatTickDebugInfo`
- `src/GuildRelay.App/CoreHost.cs` — instantiate aggregator, pass to `ChatWatcher`, expose for window, update start gate
- `src/GuildRelay.App/Config/ChatConfigTab.xaml` + `.xaml.cs` — restructure into Capture/Event Repost/Stats sections, dual toggles, counter rules list
- `src/GuildRelay.App/Config/ConfigWindow.xaml.cs` — combined any-enabled for active dot
- `src/GuildRelay.App/Tray/TrayView.xaml` + `.xaml.cs` — "View Stats" menu item
- `src/GuildRelay.App/Tray/TrayViewModel.cs` — `OpenStats()` action
- `src/GuildRelay.App/App.xaml.cs` — wire stats window controller to tray + open stats action
- `tests/GuildRelay.Features.Chat.Tests/ChatWatcherTests.cs` — `Enabled = true` → `EventRepostEnabled = true`
- `tests/GuildRelay.Core.Tests/Config/ConfigDirtyTests.cs` — test new fields' dirty behaviour
- `tests/GuildRelay.Core.Tests/Config/ConfigApplyPipelineTests.cs` — test combined gate
- `docs/superpowers/specs/2026-04-11-guild-event-relay-architecture.md` — note dual-pipeline

---

## Conventions

- xUnit + FluentAssertions (matches existing tests).
- TDD: every task with logic writes the failing test first, runs it, then minimal implementation.
- Build / test commands:
  - `dotnet build` (run from repo root)
  - `dotnet test --filter "FullyQualifiedName~<TestClass>"` for a single class
  - `dotnet test` for all
- Commit after every task (frequent commits). Use a clear conventional-commit-style message.

---

## Phase 1 — New types in Core (additive, non-breaking)

### Task 1: Add `CounterRule` and `CounterMatchMode`

**Files:**
- Create: `src/GuildRelay.Core/Config/CounterRule.cs`

- [ ] **Step 1: Create the file**

```csharp
using System.Collections.Generic;

namespace GuildRelay.Core.Config;

public enum CounterMatchMode { Template, Regex }

public sealed record CounterRule(
    string Id,
    string Label,
    List<string> Channels,
    string Pattern,
    CounterMatchMode MatchMode);
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/GuildRelay.Core/GuildRelay.Core.csproj`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/GuildRelay.Core/Config/CounterRule.cs
git commit -m "feat(core): add CounterRule type for stats analytics"
```

### Task 2: Add `CounterSnapshot` and `IStatsAggregator`

**Files:**
- Create: `src/GuildRelay.Core/Stats/CounterSnapshot.cs`
- Create: `src/GuildRelay.Core/Stats/IStatsAggregator.cs`

- [ ] **Step 1: Create CounterSnapshot.cs**

```csharp
namespace GuildRelay.Core.Stats;

public sealed record CounterSnapshot(string Label, double Total, double Last60Min);
```

- [ ] **Step 2: Create IStatsAggregator.cs**

```csharp
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
```

- [ ] **Step 3: Verify compilation**

Run: `dotnet build src/GuildRelay.Core/GuildRelay.Core.csproj`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add src/GuildRelay.Core/Stats/
git commit -m "feat(core): add IStatsAggregator interface and CounterSnapshot"
```

---

## Phase 2 — `StatsAggregator` implementation (TDD)

### Task 3: Stub `StatsAggregator` + first failing test

**Files:**
- Create: `src/GuildRelay.Core/Stats/StatsAggregator.cs`
- Create: `tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs`

- [ ] **Step 1: Create the failing test**

`tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs`:
```csharp
using System;
using FluentAssertions;
using GuildRelay.Core.Stats;
using Xunit;

namespace GuildRelay.Core.Tests.Stats;

public class StatsAggregatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecordIncrementsTotal()
    {
        var agg = new StatsAggregator();
        agg.Record("Glory", 80, T0);

        var snap = agg.Snapshot(T0);
        snap.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new CounterSnapshot("Glory", 80, 80));
    }
}
```

- [ ] **Step 2: Run test — verify it fails (no `StatsAggregator` yet)**

Run: `dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~StatsAggregatorTests"`
Expected: COMPILE ERROR — `StatsAggregator` does not exist.

- [ ] **Step 3: Write minimal `StatsAggregator`**

`src/GuildRelay.Core/Stats/StatsAggregator.cs`:
```csharp
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
```

- [ ] **Step 4: Run test — verify it passes**

Run: `dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~StatsAggregatorTests"`
Expected: 1 test passed.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Core/Stats/StatsAggregator.cs tests/GuildRelay.Core.Tests/Stats/
git commit -m "feat(core): StatsAggregator with Record + Snapshot"
```

### Task 4: Aggregation by case-insensitive trimmed label

**Files:**
- Modify: `tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs` (add tests)

- [ ] **Step 1: Add failing tests**

Append to the test class:
```csharp
[Fact]
public void RecordsToSameLabelAggregate()
{
    var agg = new StatsAggregator();
    agg.Record("Glory", 80, T0);
    agg.Record("Glory", 20, T0.AddSeconds(1));

    var snap = agg.Snapshot(T0.AddSeconds(2));
    snap.Should().ContainSingle()
        .Which.Total.Should().Be(100);
}

[Fact]
public void RecordsToDifferentLabelsAreSeparate()
{
    var agg = new StatsAggregator();
    agg.Record("Glory", 80, T0);
    agg.Record("Standing", 5, T0);

    var snap = agg.Snapshot(T0);
    snap.Should().HaveCount(2);
}

[Fact]
public void AggregationKeyIsCaseInsensitiveAndTrimmed()
{
    var agg = new StatsAggregator();
    agg.Record("Glory", 80, T0);
    agg.Record("glory ", 20, T0.AddSeconds(1));
    agg.Record(" GLORY", 10, T0.AddSeconds(2));

    var snap = agg.Snapshot(T0.AddSeconds(3));
    snap.Should().ContainSingle()
        .Which.Total.Should().Be(110);
}
```

- [ ] **Step 2: Run tests — they should pass already**

Run: `dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~StatsAggregatorTests"`
Expected: 4 tests passed (the existing logic in Task 3 already supports this).

- [ ] **Step 3: Commit**

```bash
git add tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs
git commit -m "test(stats): aggregation key is case-insensitive and trimmed"
```

### Task 5: Rolling 60-minute window

**Files:**
- Modify: `tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs`

- [ ] **Step 1: Add failing tests**

```csharp
[Fact]
public void Last60MinExcludesEventsOlderThan60Minutes()
{
    var agg = new StatsAggregator();
    agg.Record("Glory", 100, T0);
    agg.Record("Glory", 50, T0.AddMinutes(30));

    var snap = agg.Snapshot(T0.AddMinutes(70));
    snap.Should().ContainSingle()
        .Which.Should().BeEquivalentTo(new CounterSnapshot("Glory", 150, 50));
}

[Fact]
public void Last60MinExcludesEventsAtExactly60MinBoundary()
{
    // strict > now - 60min. An event timestamped exactly 60 minutes ago is excluded.
    var agg = new StatsAggregator();
    agg.Record("Glory", 100, T0);

    var snap = agg.Snapshot(T0.AddMinutes(60));
    snap.Should().ContainSingle()
        .Which.Last60Min.Should().Be(0);
}
```

- [ ] **Step 2: Run tests — verify pass**

Run: `dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~StatsAggregatorTests"`
Expected: 6 tests passed.

- [ ] **Step 3: Commit**

```bash
git add tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs
git commit -m "test(stats): rolling 60-minute window with strict boundary"
```

### Task 6: Reset and ResetAll

**Files:**
- Modify: `tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs`

- [ ] **Step 1: Add failing tests**

```csharp
[Fact]
public void ResetClearsTotalAndRollingHistoryForOneLabel()
{
    var agg = new StatsAggregator();
    agg.Record("Glory", 80, T0);
    agg.Record("Standing", 5, T0);

    agg.Reset("Glory");

    var snap = agg.Snapshot(T0);
    snap.Should().HaveCount(2);
    var glory    = System.Linq.Enumerable.Single(snap, s => s.Label == "Glory");
    var standing = System.Linq.Enumerable.Single(snap, s => s.Label == "Standing");
    glory.Total.Should().Be(0);
    glory.Last60Min.Should().Be(0);
    standing.Total.Should().Be(5);
}

[Fact]
public void ResetAllClearsAllCounters()
{
    var agg = new StatsAggregator();
    agg.Record("Glory", 80, T0);
    agg.Record("Standing", 5, T0);

    agg.ResetAll();

    var snap = agg.Snapshot(T0);
    snap.Should().AllSatisfy(s =>
    {
        s.Total.Should().Be(0);
        s.Last60Min.Should().Be(0);
    });
}
```

- [ ] **Step 2: Run tests — verify pass**

Run: `dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~StatsAggregatorTests"`
Expected: 8 tests passed.

- [ ] **Step 3: Commit**

```bash
git add tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs
git commit -m "test(stats): Reset and ResetAll clear totals and history"
```

### Task 7: Concurrency stress test

**Files:**
- Modify: `tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs`

- [ ] **Step 1: Add stress test**

```csharp
[Fact]
public async System.Threading.Tasks.Task ConcurrentRecordAndSnapshotIsSafe()
{
    var agg = new StatsAggregator();
    var stop = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));

    var writer = System.Threading.Tasks.Task.Run(() =>
    {
        var t = T0;
        while (!stop.IsCancellationRequested)
        {
            agg.Record("Glory", 1, t);
            t = t.AddMilliseconds(1);
        }
    });

    var reader = System.Threading.Tasks.Task.Run(() =>
    {
        while (!stop.IsCancellationRequested)
        {
            _ = agg.Snapshot(T0.AddMinutes(30));
        }
    });

    await System.Threading.Tasks.Task.WhenAll(writer, reader);
    var final = agg.Snapshot(T0.AddMinutes(30));
    final.Should().ContainSingle().Which.Total.Should().BeGreaterThan(0);
}
```

- [ ] **Step 2: Run tests — verify pass**

Run: `dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~StatsAggregatorTests"`
Expected: 9 tests passed (no deadlock or exception).

- [ ] **Step 3: Commit**

```bash
git add tests/GuildRelay.Core.Tests/Stats/StatsAggregatorTests.cs
git commit -m "test(stats): concurrent record and snapshot is safe"
```

---

## Phase 3 — `CounterRuleCompiler` (TDD)

### Task 8: Template mode compilation

**Files:**
- Create: `src/GuildRelay.Features.Chat/CounterRuleCompiler.cs`
- Create: `tests/GuildRelay.Features.Chat.Tests/CounterRuleCompilerTests.cs`

- [ ] **Step 1: Create failing tests**

```csharp
using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.Core.Config;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class CounterRuleCompilerTests
{
    private static CounterRule TemplateRule(string pattern) => new(
        Id: "r1", Label: "Glory",
        Channels: new List<string> { "Game" },
        Pattern: pattern, MatchMode: CounterMatchMode.Template);

    [Fact]
    public void TemplateExtractsIntegerValue()
    {
        var compiled = CounterRuleCompiler.Compile(TemplateRule("You gained {value} Glory."));
        var match = compiled.Match("You gained 80 Glory.");
        match.Success.Should().BeTrue();
        match.Value.Should().Be(80);
    }

    [Fact]
    public void TemplateExtractsDecimalValue()
    {
        var compiled = CounterRuleCompiler.Compile(TemplateRule("Mana regen {value}."));
        var match = compiled.Match("Mana regen 1.5.");
        match.Success.Should().BeTrue();
        match.Value.Should().Be(1.5);
    }

    [Fact]
    public void TemplateExtractsNegativeValue()
    {
        var compiled = CounterRuleCompiler.Compile(TemplateRule("Standing {value}."));
        var match = compiled.Match("Standing -5.");
        match.Success.Should().BeTrue();
        match.Value.Should().Be(-5);
    }

    [Fact]
    public void TemplateEscapesRegexMetaChars()
    {
        // Literal '.', '(', ')' must be escaped.
        var compiled = CounterRuleCompiler.Compile(TemplateRule("Mana (HP) {value}."));
        var match = compiled.Match("Mana (HP) 42.");
        match.Success.Should().BeTrue();
        match.Value.Should().Be(42);
    }

    [Fact]
    public void TemplateIsAnchored()
    {
        // Pattern is `^…$` — body must match in full, not just contain the pattern.
        var compiled = CounterRuleCompiler.Compile(TemplateRule("You gained {value} Glory."));
        compiled.Match("blah You gained 80 Glory. blah").Success.Should().BeFalse();
    }

    [Fact]
    public void TemplateIsCaseInsensitive()
    {
        var compiled = CounterRuleCompiler.Compile(TemplateRule("You gained {value} Glory."));
        compiled.Match("you gained 80 glory.").Success.Should().BeTrue();
    }

    [Fact]
    public void TemplateWithoutPlaceholderIsCountOnly()
    {
        var compiled = CounterRuleCompiler.Compile(TemplateRule("You died."));
        var match = compiled.Match("You died.");
        match.Success.Should().BeTrue();
        match.Value.Should().Be(1);
    }
}
```

- [ ] **Step 2: Run tests — verify they fail (compile error)**

Run: `dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~CounterRuleCompilerTests"`
Expected: COMPILE ERROR — `CounterRuleCompiler` does not exist.

- [ ] **Step 3: Implement `CounterRuleCompiler`**

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using GuildRelay.Core.Config;

namespace GuildRelay.Features.Chat;

public readonly record struct CounterMatch(bool Success, double Value);

public sealed class CompiledCounterRule
{
    public CompiledCounterRule(CounterRule rule, Regex regex, bool hasValueGroup)
    {
        Rule = rule;
        _regex = regex;
        _hasValueGroup = hasValueGroup;
    }

    public CounterRule Rule { get; }
    private readonly Regex _regex;
    private readonly bool _hasValueGroup;

    public CounterMatch Match(string body)
    {
        var m = _regex.Match(body);
        if (!m.Success) return new CounterMatch(false, 0);
        if (!_hasValueGroup) return new CounterMatch(true, 1);
        var captured = m.Groups["value"].Value;
        if (!double.TryParse(captured, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return new CounterMatch(false, 0);
        return new CounterMatch(true, v);
    }
}

public static class CounterRuleCompiler
{
    private const string ValuePlaceholder = "{value}";
    private const string ValueRegexFragment = @"(?<value>-?\d+(?:\.\d+)?)";

    public static CompiledCounterRule Compile(CounterRule rule)
    {
        const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.IgnoreCase;
        if (rule.MatchMode == CounterMatchMode.Template)
        {
            var pattern = rule.Pattern;
            var hasPlaceholder = pattern.Contains(ValuePlaceholder);
            var parts = pattern.Split(new[] { ValuePlaceholder }, System.StringSplitOptions.None);
            var compiled = string.Join(ValueRegexFragment,
                System.Linq.Enumerable.Select(parts, Regex.Escape));
            return new CompiledCounterRule(rule, new Regex("^" + compiled + "$", Opts), hasPlaceholder);
        }
        else
        {
            var regex = new Regex(rule.Pattern, Opts);
            var hasGroup = System.Linq.Enumerable.Contains(regex.GetGroupNames(), "value");
            return new CompiledCounterRule(rule, regex, hasGroup);
        }
    }
}
```

- [ ] **Step 4: Run tests — verify pass**

Run: `dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~CounterRuleCompilerTests"`
Expected: 7 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Features.Chat/CounterRuleCompiler.cs tests/GuildRelay.Features.Chat.Tests/CounterRuleCompilerTests.cs
git commit -m "feat(chat): CounterRuleCompiler for template-mode value extraction"
```

### Task 9: Regex mode compilation

**Files:**
- Modify: `tests/GuildRelay.Features.Chat.Tests/CounterRuleCompilerTests.cs`

- [ ] **Step 1: Add failing tests**

```csharp
private static CounterRule RegexRule(string pattern) => new(
    Id: "r2", Label: "Glory",
    Channels: new List<string> { "Game" },
    Pattern: pattern, MatchMode: CounterMatchMode.Regex);

[Fact]
public void RegexModeExtractsValueFromNamedGroup()
{
    var compiled = CounterRuleCompiler.Compile(
        RegexRule(@"^You gained (?<value>\d+) Glory\.?$"));
    var match = compiled.Match("You gained 80 Glory");
    match.Success.Should().BeTrue();
    match.Value.Should().Be(80);
}

[Fact]
public void RegexModeWithoutValueGroupIsCountOnly()
{
    var compiled = CounterRuleCompiler.Compile(RegexRule(@"^You died\.?$"));
    var match = compiled.Match("You died.");
    match.Success.Should().BeTrue();
    match.Value.Should().Be(1);
}
```

- [ ] **Step 2: Run tests — verify pass (existing implementation already supports this)**

Run: `dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~CounterRuleCompilerTests"`
Expected: 9 tests passed.

- [ ] **Step 3: Commit**

```bash
git add tests/GuildRelay.Features.Chat.Tests/CounterRuleCompilerTests.cs
git commit -m "test(chat): regex mode counter rules with optional value group"
```

---

## Phase 4 — `CounterMatcher` (TDD)

### Task 10: Channel-scoped matching

**Files:**
- Create: `src/GuildRelay.Features.Chat/CounterMatcher.cs`
- Create: `tests/GuildRelay.Features.Chat.Tests/CounterMatcherTests.cs`

- [ ] **Step 1: Create failing tests**

```csharp
using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.Core.Config;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class CounterMatcherTests
{
    private static CounterRule GloryRule() => new(
        Id: "g", Label: "Glory",
        Channels: new List<string> { "Game" },
        Pattern: "You gained {value} Glory.",
        MatchMode: CounterMatchMode.Template);

    [Fact]
    public void MatchesOnConfiguredChannel()
    {
        var matcher = new CounterMatcher(new[] { GloryRule() });
        var line = new ParsedChatLine("22:31:45", "Game", null, "You gained 80 Glory.");
        var result = matcher.Match(line);
        result.Should().NotBeNull();
        result!.Label.Should().Be("Glory");
        result.Value.Should().Be(80);
    }

    [Fact]
    public void DoesNotMatchOnOtherChannel()
    {
        var matcher = new CounterMatcher(new[] { GloryRule() });
        var line = new ParsedChatLine(null, "Say", "Bob", "You gained 80 Glory.");
        matcher.Match(line).Should().BeNull();
    }

    [Fact]
    public void ReturnsNullOnNoChannel()
    {
        var matcher = new CounterMatcher(new[] { GloryRule() });
        var line = new ParsedChatLine(null, null, null, "You gained 80 Glory.");
        matcher.Match(line).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests — verify they fail (compile error)**

Run: `dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~CounterMatcherTests"`
Expected: COMPILE ERROR — `CounterMatcher` does not exist.

- [ ] **Step 3: Implement `CounterMatcher`**

```csharp
using System;
using System.Collections.Generic;
using GuildRelay.Core.Config;

namespace GuildRelay.Features.Chat;

public sealed record CounterMatchResult(string Label, double Value);

public sealed class CounterMatcher
{
    private readonly Dictionary<string, List<CompiledCounterRule>> _byChannel;
    private readonly List<CompiledCounterRule> _wildcard;

    public CounterMatcher(IEnumerable<CounterRule> rules)
    {
        _byChannel = new Dictionary<string, List<CompiledCounterRule>>(StringComparer.OrdinalIgnoreCase);
        _wildcard = new List<CompiledCounterRule>();
        foreach (var rule in rules)
        {
            var compiled = CounterRuleCompiler.Compile(rule);
            if (rule.Channels.Count == 0)
            {
                _wildcard.Add(compiled);
                continue;
            }
            foreach (var ch in rule.Channels)
            {
                if (!_byChannel.TryGetValue(ch, out var list))
                {
                    list = new List<CompiledCounterRule>();
                    _byChannel[ch] = list;
                }
                list.Add(compiled);
            }
        }
    }

    public CounterMatchResult? Match(ParsedChatLine parsed)
    {
        if (parsed.Channel is null) return null;

        if (_byChannel.TryGetValue(parsed.Channel, out var candidates))
        {
            foreach (var compiled in candidates)
            {
                var m = compiled.Match(parsed.Body);
                if (m.Success) return new CounterMatchResult(compiled.Rule.Label, m.Value);
            }
        }

        foreach (var compiled in _wildcard)
        {
            var m = compiled.Match(parsed.Body);
            if (m.Success) return new CounterMatchResult(compiled.Rule.Label, m.Value);
        }

        return null;
    }
}
```

- [ ] **Step 4: Run tests — verify pass**

Run: `dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~CounterMatcherTests"`
Expected: 3 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Features.Chat/CounterMatcher.cs tests/GuildRelay.Features.Chat.Tests/CounterMatcherTests.cs
git commit -m "feat(chat): CounterMatcher with channel-scoped routing"
```

### Task 11: Wildcard rules and first-match-wins

**Files:**
- Modify: `tests/GuildRelay.Features.Chat.Tests/CounterMatcherTests.cs`

- [ ] **Step 1: Add failing tests**

```csharp
[Fact]
public void WildcardRuleMatchesAnyChannel()
{
    var wildcard = new CounterRule(
        Id: "w", Label: "Anything",
        Channels: new List<string>(),
        Pattern: "{value}",
        MatchMode: CounterMatchMode.Template);
    var matcher = new CounterMatcher(new[] { wildcard });
    var line = new ParsedChatLine(null, "Yell", "Tosh", "42");
    var result = matcher.Match(line);
    result.Should().NotBeNull();
    result!.Value.Should().Be(42);
}

[Fact]
public void ChannelSpecificRulesTriedBeforeWildcard()
{
    var glory = GloryRule();
    var wildcard = new CounterRule(
        Id: "w", Label: "Wildcard",
        Channels: new List<string>(),
        Pattern: "You gained {value} Glory.",
        MatchMode: CounterMatchMode.Template);
    var matcher = new CounterMatcher(new[] { glory, wildcard });
    var line = new ParsedChatLine(null, "Game", null, "You gained 80 Glory.");
    var result = matcher.Match(line);
    result.Should().NotBeNull();
    result!.Label.Should().Be("Glory");
}
```

- [ ] **Step 2: Run tests — verify pass**

Run: `dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~CounterMatcherTests"`
Expected: 5 tests passed.

- [ ] **Step 3: Commit**

```bash
git add tests/GuildRelay.Features.Chat.Tests/CounterMatcherTests.cs
git commit -m "test(chat): CounterMatcher wildcard rules and channel-specific priority"
```

---

## Phase 5 — Schema change (atomic rename + add fields)

### Task 12: Update `ChatConfig` record + `Default` + all callsites

**Files:**
- Modify: `src/GuildRelay.Core/Config/ChatConfig.cs`
- Modify: `src/GuildRelay.Core/Config/ConfigDirty.cs`
- Modify: `src/GuildRelay.Core/Config/ConfigApplyPipeline.cs`
- Modify: `src/GuildRelay.Core/Config/ConfigEquality.cs`
- Modify: `src/GuildRelay.App/CoreHost.cs`
- Modify: `src/GuildRelay.App/Config/ChatConfigTab.xaml.cs` (lines 29, 42)
- Modify: `src/GuildRelay.App/Config/ConfigWindow.xaml.cs` (line 76)
- Modify: `tests/GuildRelay.Features.Chat.Tests/ChatWatcherTests.cs` (line 42)
- Modify: `tests/GuildRelay.Core.Tests/Config/ConfigDirtyTests.cs` (line 22)
- Modify: `tests/GuildRelay.Core.Tests/Config/ConfigApplyPipelineTests.cs` (lines 41, 44, 47, 91, 103, 212)

This is an **atomic** task — the build will not compile until every reference is updated. Do all edits before running build.

- [ ] **Step 1: Update `ChatConfig.cs`**

`src/GuildRelay.Core/Config/ChatConfig.cs`:
```csharp
using System.Collections.Generic;

namespace GuildRelay.Core.Config;

public sealed record ChatConfig(
    bool EventRepostEnabled,
    bool StatsEnabled,
    int CaptureIntervalSec,
    double OcrConfidenceThreshold,
    int DefaultCooldownSec,
    RegionConfig Region,
    List<PreprocessStageConfig> PreprocessPipeline,
    List<StructuredChatRule> Rules,
    List<CounterRule> CounterRules,
    Dictionary<string, string> Templates)
{
    public static ChatConfig Default => new(
        EventRepostEnabled: false,
        StatsEnabled: false,
        CaptureIntervalSec: 5,
        OcrConfidenceThreshold: 0.65,
        DefaultCooldownSec: 600,
        Region: RegionConfig.Empty,
        PreprocessPipeline: new List<PreprocessStageConfig>
        {
            new("grayscale"),
            new("contrastStretch", new Dictionary<string, double> { ["low"] = 0.1, ["high"] = 0.9 }),
            new("upscale", new Dictionary<string, double> { ["factor"] = 2 }),
            new("adaptiveThreshold", new Dictionary<string, double> { ["blockSize"] = 15 })
        },
        Rules: new List<StructuredChatRule>(RuleTemplates.BuiltIn["MO2 Game Events"]),
        CounterRules: new List<CounterRule>
        {
            new(
                Id: "mo2_glory",
                Label: "Glory",
                Channels: new List<string> { "Game" },
                Pattern: "You gained {value} Glory.",
                MatchMode: CounterMatchMode.Template)
        },
        Templates: new Dictionary<string, string>
        {
            ["default"] = "**{player}** saw chat match [{rule_label}]: `{matched_text}`"
        });
}
```

- [ ] **Step 2: Update `ConfigEquality.cs`**

Replace the `Equal(ChatConfig, ChatConfig)` method and add a `CounterRule` comparator:
```csharp
public static bool Equal(ChatConfig a, ChatConfig b)
    => a.EventRepostEnabled == b.EventRepostEnabled
    && a.StatsEnabled == b.StatsEnabled
    && a.CaptureIntervalSec == b.CaptureIntervalSec
    && a.OcrConfidenceThreshold == b.OcrConfidenceThreshold
    && a.DefaultCooldownSec == b.DefaultCooldownSec
    && Equals(a.Region, b.Region)
    && ListEqual(a.PreprocessPipeline, b.PreprocessPipeline, Equal)
    && ListEqual(a.Rules, b.Rules, Equal)
    && ListEqual(a.CounterRules, b.CounterRules, Equal)
    && DictEqual(a.Templates, b.Templates);

public static bool Equal(CounterRule a, CounterRule b)
    => a.Id == b.Id
    && a.Label == b.Label
    && a.Pattern == b.Pattern
    && a.MatchMode == b.MatchMode
    && ListEqual(a.Channels, b.Channels, static (x, y) => x == y);
```

- [ ] **Step 3: Update `ConfigDirty.cs` IsDirtyChatTab**

```csharp
public static bool IsDirtyChatTab(AppConfig pending, AppConfig saved)
{
    if (pending.Chat.EventRepostEnabled != saved.Chat.EventRepostEnabled) return true;
    if (pending.Chat.StatsEnabled != saved.Chat.StatsEnabled) return true;
    if (!Equals(pending.Chat.Region, saved.Chat.Region)) return true;
    if (pending.Chat.Rules.Count != saved.Chat.Rules.Count) return true;
    for (int i = 0; i < pending.Chat.Rules.Count; i++)
        if (!ConfigEquality.Equal(pending.Chat.Rules[i], saved.Chat.Rules[i])) return true;
    if (pending.Chat.CounterRules.Count != saved.Chat.CounterRules.Count) return true;
    for (int i = 0; i < pending.Chat.CounterRules.Count; i++)
        if (!ConfigEquality.Equal(pending.Chat.CounterRules[i], saved.Chat.CounterRules[i])) return true;
    return false;
}
```

- [ ] **Step 4: Update `ConfigApplyPipeline.cs`**

Change the chat dispatch call:
```csharp
await DispatchFeatureAsync(
    name: "chat",
    oldEnabled: oldConfig.Chat.EventRepostEnabled || oldConfig.Chat.StatsEnabled,
    newEnabled: newConfig.Chat.EventRepostEnabled || newConfig.Chat.StatsEnabled,
    oldCfg: oldConfig.Chat, newCfg: newConfig.Chat,
    equal: ConfigEquality.Equal,
    needsRestart: ChatNeedsRestart,
    registry: registry,
    ct: ct).ConfigureAwait(false);
```

- [ ] **Step 5: Update `CoreHost.cs:94`**

Replace:
```csharp
if (config.Chat.Enabled && !config.Chat.Region.IsEmpty)
```
with:
```csharp
if ((config.Chat.EventRepostEnabled || config.Chat.StatsEnabled) && !config.Chat.Region.IsEmpty)
```

- [ ] **Step 6: Update `ChatConfigTab.xaml.cs`**

Change line 29 from `EnabledToggle.IsChecked = chat.Enabled;` to `EnabledToggle.IsChecked = chat.EventRepostEnabled;`. Change line 42 from `_vm.PendingConfig.Chat with { Enabled = EnabledToggle.IsChecked ?? false }` to `_vm.PendingConfig.Chat with { EventRepostEnabled = EnabledToggle.IsChecked ?? false }`. (Phase 9 will fully restructure this file; this change is the minimum to keep the build green.)

- [ ] **Step 7: Update `ConfigWindow.xaml.cs:76`**

```csharp
ChatActiveDot.Visibility   = (vm.PendingConfig.Chat.EventRepostEnabled || vm.PendingConfig.Chat.StatsEnabled)
    ? Visibility.Visible : Visibility.Collapsed;
```

- [ ] **Step 8: Update tests**

In `tests/GuildRelay.Features.Chat.Tests/ChatWatcherTests.cs:42`, change `Enabled = true,` to `EventRepostEnabled = true, StatsEnabled = false,`.

In `tests/GuildRelay.Core.Tests/Config/ConfigDirtyTests.cs`:
- Rename test method `ChatTabDirtyWhenEnabledFlips` → `ChatTabDirtyWhenEventRepostFlips`.
- Change the body of that test:
  ```csharp
  var pending = saved with { Chat = saved.Chat with { EventRepostEnabled = !saved.Chat.EventRepostEnabled } };
  ```
- Add two new tests after it:
  ```csharp
  [Fact]
  public void ChatTabDirtyWhenStatsToggleFlips()
  {
      var saved = AppConfig.Default;
      var pending = saved with { Chat = saved.Chat with { StatsEnabled = !saved.Chat.StatsEnabled } };
      ConfigDirty.IsDirtyChatTab(pending, saved).Should().BeTrue();
  }

  [Fact]
  public void ChatTabDirtyWhenCounterRulesChangeAcrossClone()
  {
      var saved = AppConfig.Default;
      var pending = JsonRoundTrip(saved);
      pending = pending with
      {
          Chat = pending.Chat with
          {
              CounterRules = new System.Collections.Generic.List<CounterRule>
              {
                  pending.Chat.CounterRules[0] with { Label = "CHANGED" }
              }
          }
      };
      ConfigDirty.IsDirtyChatTab(pending, saved).Should().BeTrue();
  }
  ```

In `tests/GuildRelay.Core.Tests/Config/ConfigApplyPipelineTests.cs`, replace each `Enabled = true` and `Enabled = false` on `Chat = ... with { ... }` with `EventRepostEnabled = ...`. Keep `StatsEnabled` defaulting to false. Lines per current file: 41, 91, 103, 212.

- [ ] **Step 9: Build full solution**

Run: `dotnet build`
Expected: `Build succeeded`. (If failure, address each error — the rename is mechanical.)

- [ ] **Step 10: Run all tests**

Run: `dotnet test`
Expected: all tests pass.

- [ ] **Step 11: Commit**

```bash
git add -u src/ tests/
git commit -m "refactor(config): rename ChatConfig.Enabled to EventRepostEnabled, add StatsEnabled and CounterRules with Glory built-in"
```

### Task 13: ConfigStore migration from old `enabled` JSON

**Files:**
- Modify: `src/GuildRelay.Core/Config/ConfigStore.cs`
- Create: `tests/GuildRelay.Core.Tests/Config/ChatConfigMigrationTests.cs`

- [ ] **Step 1: Create failing migration test**

```csharp
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Config;
using Xunit;

namespace GuildRelay.Core.Tests.Config;

public class ChatConfigMigrationTests
{
    private static string FreshConfigPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "guildrelay-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "config.json");
    }

    [Fact]
    public async Task LoadsLegacyEnabledFieldAsEventRepostEnabled()
    {
        var path = FreshConfigPath();

        // Hand-crafted pre-rename JSON: `enabled: true` on chat, no eventRepostEnabled,
        // no statsEnabled, no counterRules.
        var legacyJson = """
        {
          "schemaVersion": 1,
          "general": { "playerName": "", "webhookUrl": "", "globalEnabled": true },
          "chat": {
            "enabled": true,
            "captureIntervalSec": 5,
            "ocrConfidenceThreshold": 0.65,
            "defaultCooldownSec": 600,
            "region": { "x": 0, "y": 0, "width": 0, "height": 0, "dpi": 96, "resolution": { "width": 1920, "height": 1080 }, "monitorId": "" },
            "preprocessPipeline": [],
            "rules": [],
            "templates": { }
          },
          "audio": { "enabled": false, "rules": [], "templates": {} },
          "status": { "enabled": false, "captureIntervalSec": 5, "ocrConfidenceThreshold": 0.65, "debounceSamples": 3, "region": { "x": 0, "y": 0, "width": 0, "height": 0, "dpi": 96, "resolution": { "width": 1920, "height": 1080 }, "monitorId": "" }, "preprocessPipeline": [], "disconnectPatterns": [], "templates": {} },
          "logs": { "retentionDays": 14, "maxFileSizeMb": 50 }
        }
        """;
        await File.WriteAllTextAsync(path, legacyJson);

        var cfg = await new ConfigStore(path).LoadOrCreateDefaultsAsync();

        cfg.Chat.EventRepostEnabled.Should().BeTrue();
        cfg.Chat.StatsEnabled.Should().BeFalse();
        cfg.Chat.CounterRules.Should().NotBeEmpty(); // Glory built-in injected by migration
        cfg.Chat.CounterRules[0].Id.Should().Be("mo2_glory");
    }
}
```

- [ ] **Step 2: Run test — verify it fails**

Run: `dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~ChatConfigMigrationTests"`
Expected: FAIL — currently `LoadOrCreateDefaultsAsync` either backs up to `.bak` and returns Default (losing the `enabled: true` value) or, more likely, deserialises with `EventRepostEnabled = false` because the JSON key doesn't match.

- [ ] **Step 3: Add JSON migration to `ConfigStore.cs`**

In `LoadOrCreateDefaultsAsync`, after the existing successful deserialisation (and after `SanitizeIntervals`), add a migration helper. Replace the success path inside the `try`:

```csharp
try
{
    using var stream = File.OpenRead(_path);
    var loaded = await JsonSerializer.DeserializeAsync<AppConfig>(stream, Json).ConfigureAwait(false);
    if (loaded is null) return AppConfig.Default;

    if (loaded.Chat?.Rules is not null &&
        loaded.Chat.Rules.Exists(r => r.Channels is null || r.Keywords is null))
    {
        var backup = _path + ".bak";
        File.Copy(_path, backup, overwrite: true);
        await SaveAsync(AppConfig.Default).ConfigureAwait(false);
        return AppConfig.Default;
    }

    var migrated = await MigrateLegacyChatFieldsAsync(loaded).ConfigureAwait(false);
    return SanitizeIntervals(migrated);
}
```

Add this method at the bottom of the class:
```csharp
// Pre-rename configs serialise the chat toggle as `enabled`. The new schema uses
// `eventRepostEnabled` + `statsEnabled` + `counterRules`. STJ silently leaves the
// new properties at their default values when the legacy field is present.
// Detect that case and fix it by reading the raw JSON.
private async Task<AppConfig> MigrateLegacyChatFieldsAsync(AppConfig loaded)
{
    var raw = await File.ReadAllTextAsync(_path).ConfigureAwait(false);
    using var doc = System.Text.Json.JsonDocument.Parse(raw);
    if (!doc.RootElement.TryGetProperty("chat", out var chatEl)) return loaded;

    var chat = loaded.Chat;
    bool changed = false;

    if (!chatEl.TryGetProperty("eventRepostEnabled", out _)
        && chatEl.TryGetProperty("enabled", out var enabledEl)
        && enabledEl.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
    {
        chat = chat with { EventRepostEnabled = enabledEl.GetBoolean() };
        changed = true;
    }

    if (!chatEl.TryGetProperty("counterRules", out _) || chat.CounterRules.Count == 0)
    {
        chat = chat with { CounterRules = new System.Collections.Generic.List<CounterRule>(ChatConfig.Default.CounterRules) };
        changed = true;
    }

    return changed ? loaded with { Chat = chat } : loaded;
}
```

- [ ] **Step 4: Run test — verify pass**

Run: `dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~ChatConfigMigrationTests"`
Expected: 1 test passes. Also re-run all tests: `dotnet test` should still be green.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Core/Config/ConfigStore.cs tests/GuildRelay.Core.Tests/Config/ChatConfigMigrationTests.cs
git commit -m "feat(config): migrate legacy chat.enabled to EventRepostEnabled, inject Glory counter"
```

---

## Phase 6 — `ChatWatcher` integration

### Task 14: Inject `IStatsAggregator` and wire counter matching

**Files:**
- Modify: `src/GuildRelay.Features.Chat/ChatWatcher.cs`
- Modify: `src/GuildRelay.App/CoreHost.cs`
- Modify: `tests/GuildRelay.Features.Chat.Tests/ChatWatcherTests.cs` — `CreateWatcher` signature

- [ ] **Step 1: Update `ChatWatcher.cs` constructor + fields**

Add a using and field:
```csharp
using GuildRelay.Core.Stats;
// ...

private readonly IStatsAggregator _stats;
private CounterMatcher _counterMatcher;
```

Update the constructor:
```csharp
public ChatWatcher(
    IScreenCapture capture,
    IOcrEngine ocr,
    PreprocessPipeline pipeline,
    EventBus bus,
    IStatsAggregator stats,
    ChatConfig config,
    string playerName)
{
    _capture = capture;
    _ocr = ocr;
    _pipeline = pipeline;
    _bus = bus;
    _stats = stats;
    _config = config;
    _playerName = playerName;
    _matcher = new ChannelMatcher(config.Rules);
    _counterMatcher = new CounterMatcher(config.CounterRules);
}
```

Update `ApplyConfig` to also rebuild `_counterMatcher`:
```csharp
public void ApplyConfig(JsonElement featureConfig)
{
    var newConfig = featureConfig.Deserialize<ChatConfig>();
    if (newConfig is null) return;
    _config = newConfig;
    _matcher = new ChannelMatcher(newConfig.Rules);
    _counterMatcher = new CounterMatcher(newConfig.CounterRules);
}
```

- [ ] **Step 2: Wire counter matching in the post-dedup loop**

Inside `ProcessOneTickAsync`, after the dedup `if (_dedup.IsDuplicate(...)) { ...; continue; }` block, BEFORE the existing `var match = matcher.FindMatch(parsed);` call, insert:

```csharp
// Stats pipeline (independent of Event Repost).
if (_config.StatsEnabled)
{
    var counter = _counterMatcher.Match(parsed);
    if (counter is not null)
    {
        _stats.Record(counter.Label, counter.Value, DateTimeOffset.UtcNow);
        debug?.MatchResults.Add(
            $"COUNTED [{counter.Label}: {counter.Value}] rows {msg.StartRow}-{msg.EndRow}: " +
            $"{msg.OriginalText}");
    }
}

// Event Repost pipeline — only runs when enabled.
if (!_config.EventRepostEnabled) continue;
```

The existing `var parsed = msg.ToParsedChatLine();` declaration must already be in scope; ensure the new block reads it. The existing `continue` on no-match remains unchanged.

- [ ] **Step 3: Update `CoreHost.CreateAsync`**

Add a using:
```csharp
using GuildRelay.Core.Stats;
```

Inside `CreateAsync`, before `var registry = new FeatureRegistry();`, add:
```csharp
var statsAggregator = new StatsAggregator();
```

Update the chat watcher construction:
```csharp
var chatWatcher = new Features.Chat.ChatWatcher(
    chatCapture, chatOcr, chatPipeline, bus, statsAggregator, config.Chat, config.General.PlayerName);
```

Update the `CoreHost` ctor + properties to expose the aggregator. Add:
```csharp
public IStatsAggregator StatsAggregator { get; }
```

Pass `statsAggregator` to the constructor at the bottom of `CreateAsync`:
```csharp
return new CoreHost(appData, configStore, config, secrets, bus, eventLog, logger, publisher, registry, statsAggregator);
```

Update the `CoreHost` constructor signature:
```csharp
public CoreHost(
    string appDataDirectory,
    ConfigStore configStore,
    AppConfig config,
    SecretStore secrets,
    EventBus bus,
    EventLog eventLog,
    ILogger logger,
    DiscordPublisher publisher,
    FeatureRegistry registry,
    IStatsAggregator statsAggregator)
{
    AppDataDirectory = appDataDirectory;
    ConfigStore = configStore;
    Config = config;
    Secrets = secrets;
    Bus = bus;
    EventLog = eventLog;
    Logger = logger;
    Publisher = publisher;
    Registry = registry;
    StatsAggregator = statsAggregator;
}
```

- [ ] **Step 4: Update `ChatWatcherTests.cs` `CreateWatcher`**

Add a using:
```csharp
using GuildRelay.Core.Stats;
```

Inside the test class, change `CreateWatcher` to accept (or default-construct) an aggregator:

```csharp
private static ChatWatcher CreateWatcher(
    FakeOcr ocr,
    EventBus bus,
    List<StructuredChatRule> rules,
    IStatsAggregator? stats = null)
{
    var config = ChatConfig.Default with
    {
        EventRepostEnabled = true,
        StatsEnabled = false,
        CaptureIntervalSec = 1,
        OcrConfidenceThreshold = 0.5,
        Region = new RegionConfig(0, 0, 100, 100, 96,
            new ResolutionConfig(1920, 1080), "TEST"),
        Rules = rules
    };
    return new ChatWatcher(
        new FakeCapture(),
        ocr,
        new PreprocessPipeline(Array.Empty<IPreprocessStage>()),
        bus,
        stats ?? new StatsAggregator(),
        config,
        playerName: "Tosh");
}
```

- [ ] **Step 5: Build and run all tests**

Run: `dotnet build && dotnet test`
Expected: green.

- [ ] **Step 6: Commit**

```bash
git add src/ tests/
git commit -m "feat(chat): inject IStatsAggregator into ChatWatcher and wire post-dedup fan-out"
```

### Task 15: Integration tests — counter recorded when StatsEnabled

**Files:**
- Create: `tests/GuildRelay.Features.Chat.Tests/ChatWatcherStatsTests.cs`

- [ ] **Step 1: Create failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Capture;
using GuildRelay.Core.Config;
using GuildRelay.Core.Events;
using GuildRelay.Core.Ocr;
using GuildRelay.Core.Preprocessing;
using GuildRelay.Core.Stats;
using GuildRelay.Features.Chat;
using GuildRelay.Features.Chat.Preprocessing;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class ChatWatcherStatsTests
{
    private sealed class FakeCapture : IScreenCapture
    {
        public CapturedFrame CaptureRegion(Rectangle rect)
            => new(new byte[4 * 4], 2, 2, 8);
    }

    private sealed class FakeOcr : IOcrEngine
    {
        public List<OcrLine> NextLines { get; set; } = new();
        public Task<OcrResult> RecognizeAsync(ReadOnlyMemory<byte> bgraPixels,
            int width, int height, int stride, CancellationToken ct)
            => Task.FromResult(new OcrResult(NextLines));
    }

    private static readonly CounterRule GloryCounter = new(
        Id: "g", Label: "Glory",
        Channels: new List<string> { "Game" },
        Pattern: "You gained {value} Glory.",
        MatchMode: CounterMatchMode.Template);

    private static ChatWatcher Build(FakeOcr ocr, EventBus bus, IStatsAggregator stats,
        bool statsEnabled, bool eventRepostEnabled,
        List<StructuredChatRule>? rules = null,
        List<CounterRule>? counters = null)
    {
        var config = ChatConfig.Default with
        {
            EventRepostEnabled = eventRepostEnabled,
            StatsEnabled = statsEnabled,
            CaptureIntervalSec = 1,
            OcrConfidenceThreshold = 0.5,
            Region = new RegionConfig(0, 0, 100, 100, 96,
                new ResolutionConfig(1920, 1080), "TEST"),
            Rules = rules ?? new List<StructuredChatRule>(),
            CounterRules = counters ?? new List<CounterRule> { GloryCounter }
        };
        return new ChatWatcher(
            new FakeCapture(), ocr,
            new PreprocessPipeline(Array.Empty<IPreprocessStage>()),
            bus, stats, config, "Tosh");
    }

    private static List<OcrLine> GloryLine() => new()
    {
        new("[22:31:45][Game] You gained 80 Glory.", 0.9f, RectangleF.Empty),
        new("[22:31:46][Game] terminator line.",      0.9f, RectangleF.Empty),
    };

    [Fact]
    public async Task RecordsCounterWhenStatsEnabled()
    {
        var stats = new StatsAggregator();
        var bus = new EventBus(capacity: 16);
        var ocr = new FakeOcr { NextLines = GloryLine() };
        var watcher = Build(ocr, bus, stats, statsEnabled: true, eventRepostEnabled: false);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(1500);
        await watcher.StopAsync();
        bus.Complete();

        var snap = stats.Snapshot(DateTimeOffset.UtcNow);
        snap.Should().ContainSingle()
            .Which.Total.Should().Be(80);
    }

    [Fact]
    public async Task DoesNotRecordWhenStatsDisabled()
    {
        var stats = new StatsAggregator();
        var bus = new EventBus(capacity: 16);
        var ocr = new FakeOcr { NextLines = GloryLine() };
        var watcher = Build(ocr, bus, stats, statsEnabled: false, eventRepostEnabled: true);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(1500);
        await watcher.StopAsync();
        bus.Complete();

        stats.Snapshot(DateTimeOffset.UtcNow).Should().BeEmpty();
    }

    [Fact]
    public async Task FiresBothPipelinesWhenLineMatchesBoth()
    {
        var stats = new StatsAggregator();
        var bus = new EventBus(capacity: 16);
        var ocr = new FakeOcr { NextLines = GloryLine() };

        // Event-repost rule that also matches the Glory line.
        var chatRule = new StructuredChatRule(
            Id: "c1", Label: "GameMessages",
            Channels: new List<string> { "Game" },
            Keywords: new List<string> { "Glory" },
            MatchMode: MatchMode.ContainsAny);

        var watcher = Build(ocr, bus, stats,
            statsEnabled: true, eventRepostEnabled: true,
            rules: new List<StructuredChatRule> { chatRule });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(1500);
        await watcher.StopAsync();
        bus.Complete();

        stats.Snapshot(DateTimeOffset.UtcNow).Should().ContainSingle()
            .Which.Total.Should().Be(80);
        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);
        events.Should().ContainSingle()
            .Which.RuleLabel.Should().Be("GameMessages");
    }

    [Fact]
    public async Task DedupedLinesDoNotDoubleCount()
    {
        var stats = new StatsAggregator();
        var bus = new EventBus(capacity: 16);
        // Both ticks present the same Glory line as the assembler's "in-progress" item.
        // The dedup layer should suppress the second emission.
        var ocr = new FakeOcr { NextLines = GloryLine() };
        var watcher = Build(ocr, bus, stats, statsEnabled: true, eventRepostEnabled: false);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await watcher.StartAsync(cts.Token);
        // Allow ≥ 2 capture ticks at 1s interval.
        await Task.Delay(2500);
        await watcher.StopAsync();
        bus.Complete();

        stats.Snapshot(DateTimeOffset.UtcNow).Should().ContainSingle()
            .Which.Total.Should().Be(80);
    }
}
```

- [ ] **Step 2: Run tests — verify they pass**

Run: `dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~ChatWatcherStatsTests"`
Expected: 4 tests pass. (If `DedupedLinesDoNotDoubleCount` fails, that means dedup happens after counter matching — the integration order in Task 14 needs adjustment. Re-verify dedup is checked BEFORE the new counter block.)

- [ ] **Step 3: Commit**

```bash
git add tests/GuildRelay.Features.Chat.Tests/ChatWatcherStatsTests.cs
git commit -m "test(chat): integration coverage for stats pipeline (record / skip / both / dedup)"
```

---

## Phase 7 — Stats Window VM (no WPF deps)

### Task 16: Create test project for App-layer VM tests (if missing)

**Files:**
- Verify: `tests/GuildRelay.App.Tests/` — does it exist?

- [ ] **Step 1: Check whether the test project exists**

Run: `ls tests/GuildRelay.App.Tests 2>/dev/null && echo EXISTS || echo MISSING`

- [ ] **Step 2: If MISSING, create it**

Create `tests/GuildRelay.App.Tests/GuildRelay.App.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\GuildRelay.App\GuildRelay.App.csproj" />
    <ProjectReference Include="..\..\src\GuildRelay.Core\GuildRelay.Core.csproj" />
  </ItemGroup>
</Project>
```

(If a sibling test project exists, copy its package versions — these may be slightly different. Use the same versions.)

Add the project to `GuildRelay.sln`:
```bash
dotnet sln add tests/GuildRelay.App.Tests/GuildRelay.App.Tests.csproj
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: success.

- [ ] **Step 4: Commit (only if project was newly created)**

```bash
git add tests/GuildRelay.App.Tests/ GuildRelay.sln
git commit -m "chore(tests): add GuildRelay.App.Tests project for view-model tests"
```

If the project already existed, skip this commit.

### Task 17: `StatsViewModel` — snapshot to row VMs

**Files:**
- Create: `src/GuildRelay.App/Stats/StatsViewModel.cs`
- Create: `tests/GuildRelay.App.Tests/Stats/StatsViewModelTests.cs`

- [ ] **Step 1: Create failing test**

```csharp
using System;
using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.App.Stats;
using GuildRelay.Core.Config;
using GuildRelay.Core.Stats;
using Xunit;

namespace GuildRelay.App.Tests.Stats;

public class StatsViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private static CounterRule Glory() => new(
        Id: "g", Label: "Glory",
        Channels: new List<string> { "Game" },
        Pattern: "You gained {value} Glory.",
        MatchMode: CounterMatchMode.Template);

    [Fact]
    public void RefreshBuildsRowsFromAggregatorSnapshot()
    {
        var agg = new StatsAggregator();
        agg.Record("Glory", 80, T0);

        var vm = new StatsViewModel(agg, () => T0, () => new[] { Glory() }, () => true);
        vm.Refresh();

        vm.Rows.Should().ContainSingle();
        vm.Rows[0].Label.Should().Be("Glory");
        vm.Rows[0].Total.Should().Be(80);
        vm.Rows[0].Last60Min.Should().Be(80);
    }

    [Fact]
    public void BadgeReflectsStatsEnabledFlag()
    {
        var agg = new StatsAggregator();
        var enabled = true;
        var vm = new StatsViewModel(agg, () => T0, () => System.Array.Empty<CounterRule>(), () => enabled);

        vm.Refresh();
        vm.BadgeState.Should().Be("Stats: ON");

        enabled = false;
        vm.Refresh();
        vm.BadgeState.Should().Be("Stats: OFF");
    }
}
```

- [ ] **Step 2: Run test — verify it fails (compile error)**

Run: `dotnet test tests/GuildRelay.App.Tests --filter "FullyQualifiedName~StatsViewModelTests"`
Expected: COMPILE ERROR — `StatsViewModel` does not exist.

- [ ] **Step 3: Create `StatsViewModel.cs`**

```csharp
using System;
using System.Collections.Generic;
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
    }

    public void ResetCounter(string label) => _aggregator.Reset(label);
    public void ResetAll() => _aggregator.ResetAll();

    private static string Canonical(string label) => label.Trim().ToLowerInvariant();
}
```

- [ ] **Step 4: Run test — verify pass**

Run: `dotnet test tests/GuildRelay.App.Tests --filter "FullyQualifiedName~StatsViewModelTests"`
Expected: 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.App/Stats/StatsViewModel.cs tests/GuildRelay.App.Tests/Stats/
git commit -m "feat(app): StatsViewModel building rows from aggregator snapshot"
```

### Task 18: `StatsViewModel` — empty states and reset behaviour

**Files:**
- Modify: `tests/GuildRelay.App.Tests/Stats/StatsViewModelTests.cs`

- [ ] **Step 1: Add failing tests**

```csharp
[Fact]
public void RulesWithoutEventsRenderAsZeroRows()
{
    var agg = new StatsAggregator();
    var vm = new StatsViewModel(agg, () => T0, () => new[] { Glory() }, () => true);

    vm.Refresh();

    vm.Rows.Should().ContainSingle()
        .Which.Should().BeEquivalentTo(new CounterRowVm("Glory", 0, 0));
    vm.HasNoRules.Should().BeFalse();
}

[Fact]
public void NoRulesAndNoEventsSetsHasNoRules()
{
    var agg = new StatsAggregator();
    var vm = new StatsViewModel(agg, () => T0, () => System.Array.Empty<CounterRule>(), () => true);

    vm.Refresh();

    vm.Rows.Should().BeEmpty();
    vm.HasNoRules.Should().BeTrue();
}

[Fact]
public void ResetCounterCallsAggregator()
{
    var agg = new StatsAggregator();
    agg.Record("Glory", 80, T0);

    var vm = new StatsViewModel(agg, () => T0, () => new[] { Glory() }, () => true);
    vm.ResetCounter("Glory");
    vm.Refresh();

    vm.Rows[0].Total.Should().Be(0);
}

[Fact]
public void ResetAllClearsAllCounters()
{
    var agg = new StatsAggregator();
    agg.Record("Glory", 80, T0);
    agg.Record("Standing", 5, T0);

    var vm = new StatsViewModel(agg, () => T0, () => new[] { Glory() }, () => true);
    vm.ResetAll();
    vm.Refresh();

    vm.Rows.Should().AllSatisfy(r => r.Total.Should().Be(0));
}
```

- [ ] **Step 2: Run tests — verify pass**

Run: `dotnet test tests/GuildRelay.App.Tests --filter "FullyQualifiedName~StatsViewModelTests"`
Expected: 6 tests pass.

- [ ] **Step 3: Commit**

```bash
git add tests/GuildRelay.App.Tests/Stats/StatsViewModelTests.cs
git commit -m "test(app): StatsViewModel empty states and reset behaviour"
```

---

## Phase 8 — Stats Window (WPF)

### Task 19: `StatsWindow.xaml` + code-behind

**Files:**
- Create: `src/GuildRelay.App/Stats/StatsWindow.xaml`
- Create: `src/GuildRelay.App/Stats/StatsWindow.xaml.cs`

- [ ] **Step 1: Create `StatsWindow.xaml`**

```xml
<ui:FluentWindow x:Class="GuildRelay.App.Stats.StatsWindow"
                 xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                 xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
                 Title="GuildRelay — Stats"
                 Height="360" Width="540"
                 WindowStartupLocation="Manual"
                 ExtendsContentIntoTitleBar="True"
                 WindowBackdropType="Mica">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <ui:TitleBar Grid.Row="0" Title="GuildRelay — Stats"/>

        <Grid Grid.Row="1" Margin="12">
            <TextBlock x:Name="EmptyHint"
                       Text="No counter rules. Configure in Chat tab → Stats."
                       Visibility="Collapsed"
                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                       HorizontalAlignment="Center" VerticalAlignment="Center"/>

            <DataGrid x:Name="Grid" AutoGenerateColumns="False" CanUserAddRows="False"
                      CanUserDeleteRows="False" IsReadOnly="True" HeadersVisibility="Column"
                      GridLinesVisibility="None" RowHeight="28">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="Counter"        Binding="{Binding Label}"     Width="*"/>
                    <DataGridTextColumn Header="Total"          Binding="{Binding Total, StringFormat=N0}"     Width="120"/>
                    <DataGridTextColumn Header="Last 60 min"    Binding="{Binding Last60Min, StringFormat=N0}" Width="120"/>
                    <DataGridTemplateColumn Header="" Width="80">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <ui:Button Content="Reset" Click="OnRowResetClick" Tag="{Binding Label}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                </DataGrid.Columns>
            </DataGrid>
        </Grid>

        <DockPanel Grid.Row="2" Margin="12,4,12,12">
            <ui:Button DockPanel.Dock="Right" Content="Reset all" Click="OnResetAllClick"/>
            <CheckBox x:Name="AlwaysOnTopCheck" Content="Always on top"
                      Margin="0,0,16,0" VerticalAlignment="Center"
                      Checked="OnAlwaysOnTopChanged" Unchecked="OnAlwaysOnTopChanged"/>
            <TextBlock x:Name="BadgeText" VerticalAlignment="Center"
                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                       Text="Stats: OFF"/>
        </DockPanel>
    </Grid>
</ui:FluentWindow>
```

- [ ] **Step 2: Create `StatsWindow.xaml.cs`**

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GuildRelay.App.Stats;

public partial class StatsWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly StatsViewModel _vm;
    private readonly DispatcherTimer _timer;

    public StatsWindow(StatsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        _vm.Refresh();
        Grid.ItemsSource = _vm.Rows;
        BadgeText.Text = _vm.BadgeState;
        EmptyHint.Visibility = _vm.HasNoRules ? Visibility.Visible : Visibility.Collapsed;
        Grid.Visibility       = _vm.HasNoRules ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnRowResetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string label)
        {
            _vm.ResetCounter(label);
            Refresh();
        }
    }

    private void OnResetAllClick(object sender, RoutedEventArgs e)
    {
        _vm.ResetAll();
        Refresh();
    }

    private void OnAlwaysOnTopChanged(object sender, RoutedEventArgs e)
    {
        Topmost = AlwaysOnTopCheck.IsChecked == true;
    }
}
```

- [ ] **Step 3: Build solution**

Run: `dotnet build`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add src/GuildRelay.App/Stats/StatsWindow.xaml src/GuildRelay.App/Stats/StatsWindow.xaml.cs
git commit -m "feat(app): StatsWindow with 1Hz refresh and per-row reset"
```

### Task 20: `StatsWindowController` — single-instance lifecycle

**Files:**
- Create: `src/GuildRelay.App/Stats/StatsWindowController.cs`

- [ ] **Step 1: Create the controller**

```csharp
using System;
using System.Collections.Generic;
using GuildRelay.Core.Config;

namespace GuildRelay.App.Stats;

public sealed class StatsWindowController
{
    private readonly CoreHost _host;
    private readonly Func<IReadOnlyList<CounterRule>> _rulesProvider;
    private readonly Func<bool> _statsEnabledProvider;
    private StatsWindow? _window;

    public StatsWindowController(
        CoreHost host,
        Func<IReadOnlyList<CounterRule>> rulesProvider,
        Func<bool> statsEnabledProvider)
    {
        _host = host;
        _rulesProvider = rulesProvider;
        _statsEnabledProvider = statsEnabledProvider;
    }

    public void OpenOrFocus()
    {
        if (_window is not null && _window.IsLoaded)
        {
            _window.Activate();
            return;
        }

        var vm = new StatsViewModel(
            _host.StatsAggregator,
            () => DateTimeOffset.UtcNow,
            _rulesProvider,
            _statsEnabledProvider);

        _window = new StatsWindow(vm);
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }
}
```

- [ ] **Step 2: Build solution**

Run: `dotnet build`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/GuildRelay.App/Stats/StatsWindowController.cs
git commit -m "feat(app): StatsWindowController owns single Stats window instance"
```

### Task 21: Wire controller into `App.xaml.cs` and tray menu

**Files:**
- Modify: `src/GuildRelay.App/App.xaml.cs`
- Modify: `src/GuildRelay.App/Tray/TrayViewModel.cs`
- Modify: `src/GuildRelay.App/Tray/TrayView.xaml`
- Modify: `src/GuildRelay.App/Tray/TrayView.xaml.cs`

- [ ] **Step 1: Add `OpenStats` to `TrayViewModel`**

Replace the file:
```csharp
using System;

namespace GuildRelay.App.Tray;

public sealed class TrayViewModel
{
    private readonly Action _openConfig;
    private readonly Action _openStats;
    private readonly Action _quit;

    public TrayViewModel(CoreHost host, Action openConfig, Action openStats, Action quit)
    {
        Host = host;
        _openConfig = openConfig;
        _openStats = openStats;
        _quit = quit;
    }

    public CoreHost Host { get; }

    public void OpenConfig() => _openConfig();
    public void OpenStats()  => _openStats();
    public void Quit()       => _quit();
}
```

- [ ] **Step 2: Add menu item in `TrayView.xaml`**

Add a new `<MenuItem Header="View Stats" Click="OnOpenStats"/>` after the existing "Open Config" item:
```xml
<MenuItem Header="Open Config"      Click="OnOpenConfig"/>
<MenuItem Header="View Stats"       Click="OnOpenStats"/>
<MenuItem Header="View Logs folder" Click="OnOpenLogs"/>
<Separator/>
<MenuItem Header="Quit"             Click="OnQuit"/>
```

- [ ] **Step 3: Add handler in `TrayView.xaml.cs`**

Append:
```csharp
private void OnOpenStats(object sender, RoutedEventArgs e)
    => (DataContext as TrayViewModel)?.OpenStats();
```

- [ ] **Step 4: Wire `App.xaml.cs`**

Replace:
```csharp
using System;
using System.Windows;
using GuildRelay.App.Exceptions;
using GuildRelay.App.Stats;
using GuildRelay.App.Tray;

namespace GuildRelay.App;

public partial class App : Application
{
    private CoreHost? _host;
    private TrayView? _trayView;
    private StatsWindowController? _statsController;
    private Config.ConfigViewModel? _configVm;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        try
        {
            _host = await CoreHost.CreateAsync().ConfigureAwait(true);
            GlobalExceptionHandler.Hook(_host.Logger);

            _statsController = new StatsWindowController(
                _host,
                rulesProvider: () => CurrentChat().CounterRules,
                statsEnabledProvider: () => CurrentChat().StatsEnabled);

            _trayView = new TrayView();
            _trayView.DataContext = new TrayViewModel(_host, OpenConfig, OpenStats, Quit);
            _trayView.Show();

            if (!_host.Secrets.HasWebhookUrl)
                OpenConfig();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"GuildRelay failed to start:\n\n{ex.Message}\n\n{ex.GetType().Name}",
                "GuildRelay — Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private Core.Config.ChatConfig CurrentChat()
    {
        // The pending VM (if open) reflects unsaved edits; otherwise fall back
        // to the saved host config. The stats window should reflect the user's
        // current view of which rules are configured.
        return _configVm?.PendingConfig.Chat ?? _host!.Config.Chat;
    }

    private void OpenConfig()
    {
        _configVm = new Config.ConfigViewModel(_host!);
        var window = new Config.ConfigWindow();
        window.DataContext = _configVm;
        window.Closed += (_, _) => _configVm = null;
        window.Show();
        window.Activate();
    }

    private void OpenStats() => _statsController?.OpenOrFocus();

    private async void Quit()
    {
        if (_trayView is not null) _trayView.Hide();
        if (_host is not null) await _host.DisposeAsync();
        Shutdown();
    }
}
```

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: success.

- [ ] **Step 6: Commit**

```bash
git add src/GuildRelay.App/App.xaml.cs src/GuildRelay.App/Tray/
git commit -m "feat(app): wire Stats window via tray menu and App startup"
```

---

## Phase 9 — Chat tab UI restructure

### Task 22: Restructure `ChatConfigTab.xaml` into Capture / Event Repost / Stats sections

**Files:**
- Modify: `src/GuildRelay.App/Config/ChatConfigTab.xaml`

- [ ] **Step 1: Replace the XAML**

Replace `src/GuildRelay.App/Config/ChatConfigTab.xaml` entirely with:
```xml
<UserControl x:Class="GuildRelay.App.Config.ChatConfigTab"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="12">

            <!-- Capture (shared) -->
            <TextBlock Text="Capture (shared)" FontWeight="SemiBold" FontSize="14"/>
            <StackPanel Orientation="Horizontal" Margin="0,4,0,8">
                <ui:Button Content="Pick region" Click="OnPickRegion" Margin="0,0,8,0"/>
                <TextBlock x:Name="RegionLabel" VerticalAlignment="Center"
                           Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                           Text="No region selected"/>
                <ui:Button Content="Live View" Click="OnOpenLiveView" Margin="16,0,0,0" FontSize="11"/>
            </StackPanel>

            <Separator Margin="0,8,0,12"/>

            <!-- Event Repost -->
            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                <ui:ToggleSwitch x:Name="EventRepostToggle" IsChecked="False"
                                 Checked="OnEventRepostChanged" Unchecked="OnEventRepostChanged"/>
                <TextBlock Text="Event Repost" VerticalAlignment="Center" FontWeight="SemiBold" FontSize="14" Margin="8,0,0,0"/>
            </StackPanel>
            <TextBlock Text="Match chat lines and post to Discord."
                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                       Margin="0,0,0,8"/>

            <TextBlock Text="Rule templates" FontWeight="SemiBold"/>
            <StackPanel Orientation="Horizontal" Margin="0,4,0,12">
                <ComboBox x:Name="TemplateCombo" Width="220" Margin="0,0,8,0"/>
                <ui:Button Content="Load Template" Click="OnLoadTemplate"/>
            </StackPanel>

            <DockPanel Margin="0,0,0,4">
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                    <ui:Button Content="+" Click="OnAddRule" Width="32" Margin="4,0,0,0" ToolTip="Add rule"/>
                    <ui:Button x:Name="EditRuleButton" Content="✎" Click="OnEditRule" Width="32" Margin="4,0,0,0" IsEnabled="False" ToolTip="Edit selected rule"/>
                    <ui:Button x:Name="RemoveRuleButton" Content="—" Click="OnRemoveRule" Width="32" Margin="4,0,0,0" IsEnabled="False" ToolTip="Remove selected rule"/>
                </StackPanel>
                <TextBlock Text="Active rules" FontWeight="SemiBold" VerticalAlignment="Center"/>
            </DockPanel>
            <ListBox x:Name="RulesList" Height="120" Margin="0,4,0,12"
                     SelectionChanged="OnRulesListSelectionChanged"
                     MouseDoubleClick="OnRulesListDoubleClick"/>

            <TextBlock Text="Test a message against your rules" FontWeight="SemiBold" Margin="0,8,0,0"/>
            <StackPanel Orientation="Horizontal" Margin="0,4,0,4">
                <TextBox x:Name="TestMessageBox" Width="350" Margin="0,0,8,0" FontFamily="Consolas"/>
                <ui:Button Content="Test" Click="OnTestMessage"/>
            </StackPanel>
            <TextBlock x:Name="TestResultText" Margin="0,4,0,0"
                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" TextWrapping="Wrap"/>

            <Separator Margin="0,16,0,12"/>

            <!-- Stats -->
            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                <ui:ToggleSwitch x:Name="StatsToggle" IsChecked="False"
                                 Checked="OnStatsChanged" Unchecked="OnStatsChanged"/>
                <TextBlock Text="Stats" VerticalAlignment="Center" FontWeight="SemiBold" FontSize="14" Margin="8,0,0,0"/>
                <ui:Button Content="Open Stats Window" Click="OnOpenStats" Margin="16,0,0,0" FontSize="11"/>
            </StackPanel>
            <TextBlock Text="Match chat lines and count locally. Counters never post to Discord."
                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                       Margin="0,0,0,8"/>

            <DockPanel Margin="0,0,0,4">
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                    <ui:Button Content="+" Click="OnAddCounter" Width="32" Margin="4,0,0,0" ToolTip="Add counter rule"/>
                    <ui:Button x:Name="EditCounterButton" Content="✎" Click="OnEditCounter" Width="32" Margin="4,0,0,0" IsEnabled="False" ToolTip="Edit selected counter"/>
                    <ui:Button x:Name="RemoveCounterButton" Content="—" Click="OnRemoveCounter" Width="32" Margin="4,0,0,0" IsEnabled="False" ToolTip="Remove selected counter"/>
                </StackPanel>
                <TextBlock Text="Counter rules" FontWeight="SemiBold" VerticalAlignment="Center"/>
            </DockPanel>
            <ListBox x:Name="CountersList" Height="100" Margin="0,4,0,12"
                     SelectionChanged="OnCountersListSelectionChanged"
                     MouseDoubleClick="OnCountersListDoubleClick"/>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

- [ ] **Step 2: Build (will fail because handlers don't exist yet — that's fine, the next task wires them)**

Run: `dotnet build`
Expected: COMPILE FAIL — `OnEventRepostChanged`, `OnStatsChanged`, `OnAddCounter`, etc. missing.

(No commit yet — Task 23 brings the build green.)

### Task 23: Wire `ChatConfigTab.xaml.cs` for new toggles + counter list

**Files:**
- Modify: `src/GuildRelay.App/Config/ChatConfigTab.xaml.cs`

- [ ] **Step 1: Replace the file**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GuildRelay.App.RegionPicker;
using GuildRelay.Core.Config;
using GuildRelay.Features.Chat;

namespace GuildRelay.App.Config;

public partial class ChatConfigTab : UserControl
{
    private ConfigViewModel? _vm;
    private bool _loading;
    private DebugLiveView? _debugWindow;

    public ChatConfigTab() { InitializeComponent(); Loaded += OnLoaded; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as ConfigViewModel;
        if (_vm is null) return;

        _loading = true;
        var chat = _vm.PendingConfig.Chat;
        EventRepostToggle.IsChecked = chat.EventRepostEnabled;
        StatsToggle.IsChecked       = chat.StatsEnabled;
        UpdateRegionLabel(chat.Region);
        RefreshRulesList();
        RefreshCountersList();

        TemplateCombo.ItemsSource = RuleTemplates.BuiltInNames;
        if (RuleTemplates.BuiltInNames.Count > 0) TemplateCombo.SelectedIndex = 0;
        _loading = false;
    }

    // --- Toggles ---

    private void OnEventRepostChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _vm is null) return;
        _vm.SetPendingChat(_vm.PendingConfig.Chat with
        {
            EventRepostEnabled = EventRepostToggle.IsChecked ?? false
        });
    }

    private void OnStatsChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _vm is null) return;
        _vm.SetPendingChat(_vm.PendingConfig.Chat with
        {
            StatsEnabled = StatsToggle.IsChecked ?? false
        });
    }

    // --- Region picker ---

    private void OnPickRegion(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var picker = new RegionPickerWindow();
        if (picker.ShowDialog() == true && picker.SelectedRegion is { } rect)
        {
            var dpi = Platform.Windows.Dpi.DpiHelper.GetPrimaryMonitorDpi();
            var (resW, resH) = Platform.Windows.Dpi.DpiHelper.GetPrimaryScreenResolution();
            var region = new RegionConfig(
                rect.X, rect.Y, rect.Width, rect.Height,
                dpi, new ResolutionConfig(resW, resH), "PRIMARY");
            _vm.SetPendingChat(_vm.PendingConfig.Chat with { Region = region });
            UpdateRegionLabel(region);
        }
    }

    private void UpdateRegionLabel(RegionConfig region)
    {
        RegionLabel.Text = region.IsEmpty ? "No region selected"
                                          : $"{region.X},{region.Y} {region.Width}x{region.Height}";
    }

    // --- Event-repost rules list ---

    private void RefreshRulesList()
    {
        if (_vm is null) return;
        var selected = RulesList.SelectedIndex;
        RulesList.Items.Clear();
        foreach (var r in _vm.PendingConfig.Chat.Rules)
            RulesList.Items.Add(FormatRuleSummary(r));
        if (selected >= 0 && selected < RulesList.Items.Count)
            RulesList.SelectedIndex = selected;
        UpdateRuleButtons();
    }

    private static string FormatRuleSummary(StructuredChatRule r)
    {
        var channels = r.Channels.Count == 0 ? "all channels" : string.Join(", ", r.Channels);
        var keywords = r.Keywords.Count == 0 ? "all messages" : $"{r.Keywords.Count} keywords";
        var mode = r.MatchMode == MatchMode.Regex ? " (regex)" : "";
        return $"{r.Label}  —  {channels}  —  {keywords}{mode}  —  {r.CooldownSec}s";
    }

    private void UpdateRuleButtons()
    {
        bool hasSelection = RulesList.SelectedIndex >= 0;
        EditRuleButton.IsEnabled   = hasSelection;
        RemoveRuleButton.IsEnabled = hasSelection;
    }

    private void OnRulesListSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateRuleButtons();

    private void OnRulesListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RulesList.SelectedIndex >= 0) OnEditRule(sender, e);
    }

    private void OnAddRule(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var window = Window.GetWindow(this)!;
        var rule = RuleEditorWindow.Show(window, existing: null, _vm.PendingConfig.Chat.DefaultCooldownSec);
        if (rule is null) return;
        var newRules = new List<StructuredChatRule>(_vm.PendingConfig.Chat.Rules) { rule };
        _vm.SetPendingChat(_vm.PendingConfig.Chat with { Rules = newRules });
        RefreshRulesList();
    }

    private void OnEditRule(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var idx = RulesList.SelectedIndex;
        var rules = _vm.PendingConfig.Chat.Rules;
        if (idx < 0 || idx >= rules.Count) return;
        var window = Window.GetWindow(this)!;
        var rule = RuleEditorWindow.Show(window, existing: rules[idx], _vm.PendingConfig.Chat.DefaultCooldownSec);
        if (rule is null) return;
        var newRules = new List<StructuredChatRule>(rules);
        newRules[idx] = rule;
        _vm.SetPendingChat(_vm.PendingConfig.Chat with { Rules = newRules });
        RefreshRulesList();
        RulesList.SelectedIndex = idx;
    }

    private void OnRemoveRule(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var idx = RulesList.SelectedIndex;
        var rules = _vm.PendingConfig.Chat.Rules;
        if (idx < 0 || idx >= rules.Count) return;
        var newRules = new List<StructuredChatRule>(rules);
        newRules.RemoveAt(idx);
        _vm.SetPendingChat(_vm.PendingConfig.Chat with { Rules = newRules });
        RefreshRulesList();
    }

    private void OnLoadTemplate(object sender, RoutedEventArgs e)
    {
        if (_vm is null || TemplateCombo.SelectedItem is not string name) return;
        if (!RuleTemplates.BuiltIn.TryGetValue(name, out var templateRules)) return;
        var current = _vm.PendingConfig.Chat.Rules;
        var newOnes = templateRules.Where(r => !current.Any(er => er.Id == r.Id)).ToList();
        if (newOnes.Count == 0) return;
        var newRules = new List<StructuredChatRule>(current);
        newRules.AddRange(newOnes);
        _vm.SetPendingChat(_vm.PendingConfig.Chat with { Rules = newRules });
        RefreshRulesList();
    }

    // --- Counter rules list ---

    private void RefreshCountersList()
    {
        if (_vm is null) return;
        var selected = CountersList.SelectedIndex;
        CountersList.Items.Clear();
        foreach (var c in _vm.PendingConfig.Chat.CounterRules)
            CountersList.Items.Add(FormatCounterSummary(c));
        if (selected >= 0 && selected < CountersList.Items.Count)
            CountersList.SelectedIndex = selected;
        UpdateCounterButtons();
    }

    private static string FormatCounterSummary(CounterRule c)
    {
        var channels = c.Channels.Count == 0 ? "all channels" : string.Join(", ", c.Channels);
        var mode = c.MatchMode == CounterMatchMode.Regex ? "regex" : "template";
        return $"{c.Label}  —  {channels}  —  {mode}  —  \"{c.Pattern}\"";
    }

    private void UpdateCounterButtons()
    {
        bool hasSelection = CountersList.SelectedIndex >= 0;
        EditCounterButton.IsEnabled   = hasSelection;
        RemoveCounterButton.IsEnabled = hasSelection;
    }

    private void OnCountersListSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCounterButtons();

    private void OnCountersListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CountersList.SelectedIndex >= 0) OnEditCounter(sender, e);
    }

    private void OnAddCounter(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var window = Window.GetWindow(this)!;
        var rule = CounterRuleEditorWindow.Show(window, existing: null);
        if (rule is null) return;
        var newCounters = new List<CounterRule>(_vm.PendingConfig.Chat.CounterRules) { rule };
        _vm.SetPendingChat(_vm.PendingConfig.Chat with { CounterRules = newCounters });
        RefreshCountersList();
    }

    private void OnEditCounter(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var idx = CountersList.SelectedIndex;
        var counters = _vm.PendingConfig.Chat.CounterRules;
        if (idx < 0 || idx >= counters.Count) return;
        var window = Window.GetWindow(this)!;
        var rule = CounterRuleEditorWindow.Show(window, existing: counters[idx]);
        if (rule is null) return;
        var newCounters = new List<CounterRule>(counters);
        newCounters[idx] = rule;
        _vm.SetPendingChat(_vm.PendingConfig.Chat with { CounterRules = newCounters });
        RefreshCountersList();
        CountersList.SelectedIndex = idx;
    }

    private void OnRemoveCounter(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var idx = CountersList.SelectedIndex;
        var counters = _vm.PendingConfig.Chat.CounterRules;
        if (idx < 0 || idx >= counters.Count) return;
        var newCounters = new List<CounterRule>(counters);
        newCounters.RemoveAt(idx);
        _vm.SetPendingChat(_vm.PendingConfig.Chat with { CounterRules = newCounters });
        RefreshCountersList();
    }

    private void OnOpenStats(object sender, RoutedEventArgs e)
        => ((App)Application.Current).OpenStatsFromConfig();

    // --- Live view ---

    private void OnOpenLiveView(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var chatFeature = _vm.Host.Registry.Get("chat") as ChatWatcher;
        if (chatFeature is null) return;

        if (_debugWindow is null || !_debugWindow.IsLoaded)
        {
            _debugWindow = new DebugLiveView();
            _debugWindow.Attach(chatFeature);
            _debugWindow.Show();
        }
        else _debugWindow.Activate();
    }

    // --- Test message ---

    private void OnTestMessage(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var message = TestMessageBox.Text;
        if (string.IsNullOrWhiteSpace(message))
        {
            TestResultText.Text = "Enter a message to test.";
            TestResultText.Foreground = Brushes.Gray;
            return;
        }
        var rules = _vm.PendingConfig.Chat.Rules;
        if (rules.Count == 0)
        {
            TestResultText.Text = "No rules defined. Add rules above first.";
            TestResultText.Foreground = Brushes.Gray;
            return;
        }
        var normalized = TextNormalizer.Normalize(message);
        var parsed = ChatLineParser.Parse(normalized);
        var matcher = new ChannelMatcher(rules);
        var match = matcher.FindMatch(parsed);
        if (match is not null)
        {
            TestResultText.Text = $"MATCH: rule \"{match.Rule.Label}\" on channel [{parsed.Channel}].  Body: \"{parsed.Body}\"";
            TestResultText.Foreground = Brushes.LimeGreen;
        }
        else
        {
            TestResultText.Text = $"No match.  Channel: [{parsed.Channel ?? "none"}]  Body: \"{parsed.Body}\"";
            TestResultText.Foreground = Brushes.OrangeRed;
        }
    }
}
```

- [ ] **Step 2: Add a public method on `App` for the config-tab Stats button**

In `src/GuildRelay.App/App.xaml.cs`, add a public method:
```csharp
internal void OpenStatsFromConfig() => OpenStats();
```

(Place this right after the existing `OpenStats` method.)

- [ ] **Step 3: Build (still fails — `CounterRuleEditorWindow` is referenced but not yet created)**

Run: `dotnet build`
Expected: COMPILE FAIL — `CounterRuleEditorWindow` does not exist. Move on to Task 24 to bring the build green.

(No commit yet.)

### Task 24: `CounterRuleEditorWindow` dialog

**Files:**
- Create: `src/GuildRelay.App/Config/CounterRuleEditorWindow.xaml`
- Create: `src/GuildRelay.App/Config/CounterRuleEditorWindow.xaml.cs`

- [ ] **Step 1: Create XAML**

`src/GuildRelay.App/Config/CounterRuleEditorWindow.xaml`:
```xml
<ui:FluentWindow x:Class="GuildRelay.App.Config.CounterRuleEditorWindow"
                 xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                 xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
                 Title="Counter Rule"
                 Height="440" Width="560"
                 ExtendsContentIntoTitleBar="True"
                 WindowBackdropType="Mica"
                 WindowStartupLocation="CenterOwner">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <ui:TitleBar x:Name="TitleBarControl" Grid.Row="0" Title="Counter Rule"/>

        <StackPanel Grid.Row="1" Margin="16">
            <TextBlock Text="Label" FontWeight="SemiBold"/>
            <TextBox x:Name="LabelBox" Margin="0,4,0,12"/>

            <TextBlock Text="Channels" FontWeight="SemiBold"/>
            <WrapPanel x:Name="ChannelPanel" Margin="0,4,0,4"/>
            <TextBlock x:Name="WildcardHint" Text="No channels selected → matches all channels."
                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" Margin="0,0,0,12"/>

            <TextBlock Text="Match mode" FontWeight="SemiBold"/>
            <StackPanel Orientation="Horizontal" Margin="0,4,0,12">
                <RadioButton x:Name="TemplateRadio" Content="Template" IsChecked="True" Margin="0,0,16,0"/>
                <RadioButton x:Name="RegexRadio"    Content="Regex"/>
            </StackPanel>

            <TextBlock Text="Pattern (use {value} where the number appears)" FontWeight="SemiBold"/>
            <TextBox x:Name="PatternBox" FontFamily="Consolas" Margin="0,4,0,8"/>
            <TextBlock Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" TextWrapping="Wrap"
                       Text="Example: 'You gained {value} Glory.' captures the integer/decimal where {value} sits. Omit the placeholder for a count-only counter (each match = 1)."/>
        </StackPanel>

        <DockPanel Grid.Row="2" Margin="16,0,16,16" LastChildFill="False">
            <ui:Button DockPanel.Dock="Right" Content="Save"   Click="OnSaveClick" Margin="8,0,0,0"/>
            <ui:Button DockPanel.Dock="Right" Content="Cancel" Click="OnCancelClick"/>
        </DockPanel>
    </Grid>
</ui:FluentWindow>
```

- [ ] **Step 2: Create code-behind**

`src/GuildRelay.App/Config/CounterRuleEditorWindow.xaml.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GuildRelay.Core.Config;
using GuildRelay.Features.Chat;

namespace GuildRelay.App.Config;

public partial class CounterRuleEditorWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly Dictionary<string, CheckBox> _channelChecks = new();
    private CounterRule? _result;

    private CounterRuleEditorWindow() { InitializeComponent(); }

    public static CounterRule? Show(Window owner, CounterRule? existing)
    {
        var dlg = new CounterRuleEditorWindow { Owner = owner };
        dlg.Initialize(existing);
        return dlg.ShowDialog() == true ? dlg._result : null;
    }

    private void Initialize(CounterRule? existing)
    {
        ChannelPanel.Children.Clear();
        _channelChecks.Clear();
        foreach (var ch in ChatLineParser.KnownChannelNames)
        {
            var cb = new CheckBox { Content = ch, Margin = new Thickness(0, 0, 12, 4) };
            cb.Checked   += (_, _) => UpdateWildcardHint();
            cb.Unchecked += (_, _) => UpdateWildcardHint();
            ChannelPanel.Children.Add(cb);
            _channelChecks[ch] = cb;
        }

        if (existing is null)
        {
            Title = "Add Counter Rule";
            TitleBarControl.Title = "Add Counter Rule";
            LabelBox.Text = string.Empty;
            PatternBox.Text = string.Empty;
            // Default channel: Game (most common for counter rules).
            if (_channelChecks.TryGetValue("Game", out var gameCb)) gameCb.IsChecked = true;
            TemplateRadio.IsChecked = true;
        }
        else
        {
            Title = $"Edit Counter Rule — {existing.Label}";
            TitleBarControl.Title = $"Edit Counter Rule — {existing.Label}";
            LabelBox.Text = existing.Label;
            PatternBox.Text = existing.Pattern;
            foreach (var (ch, cb) in _channelChecks)
                cb.IsChecked = existing.Channels.Contains(ch, StringComparer.OrdinalIgnoreCase);
            TemplateRadio.IsChecked = existing.MatchMode == CounterMatchMode.Template;
            RegexRadio.IsChecked    = existing.MatchMode == CounterMatchMode.Regex;
        }

        UpdateWildcardHint();
    }

    private void UpdateWildcardHint()
    {
        bool any = _channelChecks.Values.Any(cb => cb.IsChecked == true);
        WildcardHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var label = LabelBox.Text?.Trim() ?? string.Empty;
        if (label.Length == 0)
        {
            MessageBox.Show("Label is required.", "Counter Rule", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var pattern = PatternBox.Text ?? string.Empty;
        if (pattern.Length == 0)
        {
            MessageBox.Show("Pattern is required.", "Counter Rule", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var channels = _channelChecks
            .Where(kv => kv.Value.IsChecked == true)
            .Select(kv => kv.Key)
            .ToList();
        var mode = RegexRadio.IsChecked == true ? CounterMatchMode.Regex : CounterMatchMode.Template;

        _result = new CounterRule(
            Id: Guid.NewGuid().ToString("N"),
            Label: label,
            Channels: channels,
            Pattern: pattern,
            MatchMode: mode);
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
```

- [ ] **Step 3: Build solution**

Run: `dotnet build`
Expected: success.

- [ ] **Step 4: Run all tests**

Run: `dotnet test`
Expected: all tests still pass.

- [ ] **Step 5: Commit (single commit covering Task 22 + 23 + 24)**

```bash
git add src/GuildRelay.App/Config/ChatConfigTab.xaml src/GuildRelay.App/Config/ChatConfigTab.xaml.cs src/GuildRelay.App/Config/CounterRuleEditorWindow.xaml src/GuildRelay.App/Config/CounterRuleEditorWindow.xaml.cs src/GuildRelay.App/App.xaml.cs
git commit -m "feat(config-ui): split Chat tab into Capture/Event Repost/Stats sections with counter editor"
```

---

## Phase 10 — Architecture doc + smoke test

### Task 25: Update architecture doc

**Files:**
- Modify: `docs/superpowers/specs/2026-04-11-guild-event-relay-architecture.md`

- [ ] **Step 1: Append a sub-section to §5 ("Chat Watcher internals")**

The architecture doc's section heading layout (verified by `grep '^##' docs/superpowers/specs/2026-04-11-guild-event-relay-architecture.md`):
- §5 Chat Watcher internals (line 136)
- §6 Audio Watcher internals (line 178)

Add the following block at the END of §5 (right before the §6 heading):

```markdown
### 5.X Dual pipeline (added 2026-05-01)

Chat Watcher hosts two parallel post-dedup consumers of every parsed chat line: an **Event Repost** matcher (drives the Discord publisher) and a **Stats** matcher (drives an in-memory `IStatsAggregator`). Both share the same OCR work — region capture, preprocessing, OCR, normalization, message assembly, and dedup are performed once per tick. The two consumers are independent: a line that matches both rule lists fires both pipelines.

The Chat Watcher feature is started whenever `EventRepostEnabled || StatsEnabled` is true (replacing the old single `Enabled` flag). See [`2026-05-01-chat-stats-counters-design.md`](./2026-05-01-chat-stats-counters-design.md) for full details.
```

Renumber `5.X` to the next available sub-section number based on existing §5 sub-headings (use `grep '^###' docs/superpowers/specs/2026-04-11-guild-event-relay-architecture.md` between lines 136 and 178 to count).

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/specs/2026-04-11-guild-event-relay-architecture.md
git commit -m "docs(arch): note Chat Watcher dual pipeline (event repost + stats)"
```

### Task 26: Manual smoke test

**Files:** none — runtime check.

- [ ] **Step 1: Build and run**

Run:
```bash
dotnet build
dotnet run --project src/GuildRelay.App
```

The tray icon appears.

- [ ] **Step 2: Open config from tray, verify Chat tab restructure**

Right-click tray → Open Config. Expected:
- Chat tab shows Capture (top, with region + Live View), Event Repost (toggle + rules, "Active rules" list), Stats (toggle + counter rules list, "Open Stats Window" button).
- "Glory" counter rule is pre-populated in the Stats counter rules list.

- [ ] **Step 3: Toggle Stats on, save, open Stats window from tray**

- Toggle Stats ON, click Save.
- Right-click tray → View Stats.
- Window opens with one row: Glory | 0 | 0. Badge reads "Stats: ON".

- [ ] **Step 4: Reset, Reset all, Always-on-top**

- Click per-row Reset → row stays visible (no events to clear), no error.
- Click Reset all → no error.
- Toggle "Always on top" → window stays in front of other windows.

- [ ] **Step 5: Verify Live View shows COUNTED entries**

(Requires actual game OCR; if that's unavailable in dev, mark this step as deferred with a comment in the manual test log.)

- [ ] **Step 6: Verify config persistence**

- Quit app, reopen.
- Open Config — Stats toggle still ON, Glory counter rule still present.
- Open Stats window — counter values are zero (session-only as designed).

- [ ] **Step 7: Final clean commit (only if any fixes were needed)**

If the smoke test surfaced any defects, fix and commit. Otherwise, no further commits.

---

## Self-review checklist (post-write)

After this plan is written, sweep the spec one last time:

- [ ] Spec §3.1 UI restructure → Tasks 22, 23 ✓
- [ ] Spec §3.2 Counter rule editor → Task 24 ✓
- [ ] Spec §3.3 Stats window → Tasks 19, 20, 21 ✓
- [ ] Spec §4.1 Config schema → Task 12 ✓
- [ ] Spec §4.2 Pattern compilation → Tasks 8, 9 ✓
- [ ] Spec §4.3 Built-in Glory default → Task 12 ✓
- [ ] Spec §4.4 Aggregator → Tasks 3-7 ✓
- [ ] Spec §5 Capture loop integration → Tasks 14, 15 ✓
- [ ] Spec §5.3 Live View diagnostics → Task 14 (COUNTED entries added) ✓
- [ ] Spec §6 Stats window behaviour → Tasks 17, 18, 19, 20, 21 ✓
- [ ] Spec §7 Testing strategy → Tasks 3-11, 15, 17, 18 ✓
- [ ] Spec §8.1 Config field migration → Task 13 ✓
- [ ] Spec §8.2 Equality and dirty tracking → Task 12 (covers ConfigEquality + ConfigDirty) ✓
- [ ] Spec §8.3 Architecture doc update → Task 25 ✓
- [ ] Spec §9 Anti-cheat compliance → no work needed (re-uses existing capture path) ✓
