# Single Save Button + Live Apply Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the four per-section save flows in the GuildRelay settings UI with one global Save (plus Revert) that persists the pending config, applies every running watcher's changes via `ApplyConfig`, and restarts only the watchers whose timer/state-machine-bound fields changed — fixing the capture-interval-not-applying bug as a byproduct.

**Architecture:** Core gains an `IFeatureRegistry` interface and a pure `ConfigApplyPipeline` that diffs old vs new `AppConfig` and dispatches per-feature (`ApplyConfig`, stop, start, or restart). `FeatureRegistry` implements the interface and exposes `ApplyConfigAsync` as a thin wrapper over the already-present but unused `IFeature.ApplyConfig`. `ConfigViewModel` in the App gains `PendingConfig` / `SavedConfig` / dirty flags / `SaveAsync` / `Revert`. `ConfigWindow` gets a sticky footer with `Save`/`Revert` and an auto-save on close; each tab is refactored to read from / write to the VM's `PendingConfig` instead of holding local mirror state and having its own save handler.

**Tech Stack:** .NET 8, WPF + WPF-UI, xUnit + FluentAssertions, `System.Text.Json`.

**Spec:** [docs/superpowers/specs/2026-04-15-single-save-button-settings-design.md](../specs/2026-04-15-single-save-button-settings-design.md)

---

### Task 1: `IFeatureRegistry` interface + `ApplyConfigAsync`

Add a minimal interface Core can depend on and test against. Extend `FeatureRegistry` to implement it and to expose `ApplyConfigAsync` that delegates to the existing `IFeature.ApplyConfig`.

**Files:**
- Create: `src/GuildRelay.Core/Features/IFeatureRegistry.cs`
- Modify: `src/GuildRelay.Core/Features/FeatureRegistry.cs`
- Create: `tests/GuildRelay.Core.Tests/Features/FeatureRegistryApplyConfigTests.cs`

- [ ] **Step 1: Create `IFeatureRegistry.cs`**

Path: `src/GuildRelay.Core/Features/IFeatureRegistry.cs`

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GuildRelay.Core.Features;

public interface IFeatureRegistry
{
    Task StartAsync(string id, CancellationToken ct);
    Task StopAsync(string id);
    Task ApplyConfigAsync(string id, JsonElement featureConfig);
}
```

- [ ] **Step 2: Write a failing test for `ApplyConfigAsync`**

Path: `tests/GuildRelay.Core.Tests/Features/FeatureRegistryApplyConfigTests.cs`

```csharp
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Features;
using Xunit;

namespace GuildRelay.Core.Tests.Features;

public class FeatureRegistryApplyConfigTests
{
    private sealed class SpyFeature : IFeature
    {
        public string Id { get; }
        public SpyFeature(string id) { Id = id; }
        public string DisplayName => Id;
        public FeatureStatus Status => FeatureStatus.Idle;
        public JsonElement? LastApplied { get; private set; }
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public void ApplyConfig(JsonElement featureConfig) => LastApplied = featureConfig.Clone();
        public event EventHandler<StatusChangedArgs>? StatusChanged { add { } remove { } }
    }

    [Fact]
    public async Task ApplyConfigAsyncForwardsToRegisteredFeature()
    {
        var feature = new SpyFeature("chat");
        var registry = new FeatureRegistry();
        registry.Register(feature);
        using var doc = JsonDocument.Parse("""{"Enabled":true}""");

        await registry.ApplyConfigAsync("chat", doc.RootElement);

        feature.LastApplied.HasValue.Should().BeTrue();
        feature.LastApplied!.Value.GetProperty("Enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ApplyConfigAsyncNoOpsForUnknownFeature()
    {
        var registry = new FeatureRegistry();
        using var doc = JsonDocument.Parse("{}");

        Func<Task> act = async () => await registry.ApplyConfigAsync("missing", doc.RootElement);

        await act.Should().NotThrowAsync();
    }
}
```

- [ ] **Step 3: Run test — expect compile errors (no `ApplyConfigAsync` yet)**

```
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~FeatureRegistryApplyConfigTests" --nologo
```

Expected: compile error — `FeatureRegistry` has no `ApplyConfigAsync` method.

- [ ] **Step 4: Update `FeatureRegistry.cs` to add `ApplyConfigAsync` and implement `IFeatureRegistry`**

Replace the entire body of `src/GuildRelay.Core/Features/FeatureRegistry.cs` with:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GuildRelay.Core.Features;

public sealed class FeatureRegistry : IFeatureRegistry
{
    private readonly List<IFeature> _features = new();

    public IReadOnlyList<IFeature> All => _features;

    public void Register(IFeature feature) => _features.Add(feature);

    public IFeature? Get(string id) => _features.FirstOrDefault(f => f.Id == id);

    public async Task StartAsync(string id, CancellationToken ct)
    {
        var feature = Get(id);
        if (feature is null) return;
        await feature.StartAsync(ct).ConfigureAwait(false);
    }

    public async Task StopAsync(string id)
    {
        var feature = Get(id);
        if (feature is null) return;
        await feature.StopAsync().ConfigureAwait(false);
    }

    public Task ApplyConfigAsync(string id, JsonElement featureConfig)
    {
        var feature = Get(id);
        if (feature is null) return Task.CompletedTask;
        feature.ApplyConfig(featureConfig);
        return Task.CompletedTask;
    }

    public async Task StopAllAsync()
    {
        foreach (var f in _features)
            await f.StopAsync().ConfigureAwait(false);
    }
}
```

- [ ] **Step 5: Run tests — expect pass**

```
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~FeatureRegistryApplyConfigTests" --nologo
```

Expected: 2 PASS.

- [ ] **Step 6: Run the full Core test project — catch regressions**

```
dotnet test tests/GuildRelay.Core.Tests --nologo
```

Expected: all PASS.

- [ ] **Step 7: Commit**

```
git add src/GuildRelay.Core/Features/IFeatureRegistry.cs src/GuildRelay.Core/Features/FeatureRegistry.cs tests/GuildRelay.Core.Tests/Features/FeatureRegistryApplyConfigTests.cs
git commit -m "feat(core): add IFeatureRegistry + FeatureRegistry.ApplyConfigAsync"
```

---

### Task 2: `ConfigApplyPipeline` (pure diff + dispatch)

Core-level pure logic that takes old and new `AppConfig` plus an `IFeatureRegistry` and performs the correct sequence of `ApplyConfig` / `Start` / `Stop` calls per feature. Lives in `GuildRelay.Core` so it can be unit-tested without WPF.

**Design note:** The pipeline always calls `ApplyConfigAsync` before any `StartAsync` or stop+start, so the feature's internal `_config` is up to date for the restart. This fixes a latent bug where prior tab-level Saves restarted features but left their internal config stale.

**Files:**
- Create: `src/GuildRelay.Core/Config/ConfigApplyPipeline.cs`
- Create: `tests/GuildRelay.Core.Tests/Config/ConfigApplyPipelineTests.cs`

- [ ] **Step 1: Write failing tests**

Path: `tests/GuildRelay.Core.Tests/Config/ConfigApplyPipelineTests.cs`

```csharp
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Config;
using GuildRelay.Core.Features;
using Xunit;

namespace GuildRelay.Core.Tests.Config;

public class ConfigApplyPipelineTests
{
    private sealed class SpyRegistry : IFeatureRegistry
    {
        public List<string> Calls { get; } = new();

        public Task StartAsync(string id, CancellationToken ct)
        {
            Calls.Add($"start:{id}");
            return Task.CompletedTask;
        }

        public Task StopAsync(string id)
        {
            Calls.Add($"stop:{id}");
            return Task.CompletedTask;
        }

        public Task ApplyConfigAsync(string id, JsonElement featureConfig)
        {
            Calls.Add($"apply:{id}");
            return Task.CompletedTask;
        }
    }

    private static AppConfig BaselineEnabled() => AppConfig.Default with
    {
        Chat = ChatConfig.Default with
        {
            Enabled = true,
            Region = new RegionConfig(0, 0, 100, 100, 1.0, new ResolutionConfig(1920, 1080), "PRIMARY")
        },
        Audio = AudioConfig.Default with { Enabled = true },
        Status = StatusConfig.Default with
        {
            Enabled = true,
            Region = new RegionConfig(0, 0, 50, 50, 1.0, new ResolutionConfig(1920, 1080), "PRIMARY")
        }
    };

    [Fact]
    public async Task IdenticalConfigsProduceNoCalls()
    {
        var registry = new SpyRegistry();
        var cfg = BaselineEnabled();

        await ConfigApplyPipeline.DispatchAsync(cfg, cfg, registry, CancellationToken.None);

        registry.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ChatEnabledTrueToFalseStopsOnly()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        var newCfg = oldCfg with { Chat = oldCfg.Chat with { Enabled = false } };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("stop:chat");
    }

    [Fact]
    public async Task ChatEnabledFalseToTrueAppliesThenStarts()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled() with { Chat = BaselineEnabled().Chat with { Enabled = false } };
        var newCfg = BaselineEnabled();

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("apply:chat", "start:chat");
    }

    [Fact]
    public async Task ChatCaptureIntervalChangeForcesRestart()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        var newCfg = oldCfg with { Chat = oldCfg.Chat with { CaptureIntervalSec = oldCfg.Chat.CaptureIntervalSec + 1 } };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("apply:chat", "stop:chat", "start:chat");
    }

    [Fact]
    public async Task ChatRulesChangeHotAppliesWithoutRestart()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        var newCfg = oldCfg with
        {
            Chat = oldCfg.Chat with
            {
                Rules = new List<StructuredChatRule>
                {
                    new("r1", "Test", new List<string>(), new List<string> { "inc" }, MatchMode.ContainsAny)
                }
            }
        };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("apply:chat");
    }

    [Fact]
    public async Task ChatRegionChangeHotAppliesWithoutRestart()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        var newCfg = oldCfg with
        {
            Chat = oldCfg.Chat with
            {
                Region = new RegionConfig(10, 10, 200, 200, 1.0, new ResolutionConfig(1920, 1080), "PRIMARY")
            }
        };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("apply:chat");
    }

    [Fact]
    public async Task StatusCaptureIntervalChangeForcesRestart()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        var newCfg = oldCfg with { Status = oldCfg.Status with { CaptureIntervalSec = oldCfg.Status.CaptureIntervalSec + 1 } };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("apply:status", "stop:status", "start:status");
    }

    [Fact]
    public async Task StatusDebounceSamplesChangeForcesRestart()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        var newCfg = oldCfg with { Status = oldCfg.Status with { DebounceSamples = oldCfg.Status.DebounceSamples + 1 } };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("apply:status", "stop:status", "start:status");
    }

    [Fact]
    public async Task AudioRulesChangeHotAppliesWithoutRestart()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        var newCfg = oldCfg with
        {
            Audio = oldCfg.Audio with
            {
                Rules = new List<AudioRuleConfig>
                {
                    new("boom", "Boom", "clips/boom.wav", 0.8, 30)
                }
            }
        };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("apply:audio");
    }

    [Fact]
    public async Task DisabledFeatureChangesProduceNoRuntimeCalls()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        oldCfg = oldCfg with { Chat = oldCfg.Chat with { Enabled = false } };
        var newCfg = oldCfg with { Chat = oldCfg.Chat with { CaptureIntervalSec = 42 } };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task MultipleFeaturesDispatchIndependently()
    {
        var registry = new SpyRegistry();
        var oldCfg = BaselineEnabled();
        var newCfg = oldCfg with
        {
            Chat = oldCfg.Chat with
            {
                Rules = new List<StructuredChatRule>
                {
                    new("r", "Lbl", new List<string>(), new List<string> { "x" }, MatchMode.ContainsAny)
                }
            },
            Status = oldCfg.Status with { CaptureIntervalSec = oldCfg.Status.CaptureIntervalSec + 1 }
        };

        await ConfigApplyPipeline.DispatchAsync(oldCfg, newCfg, registry, CancellationToken.None);

        registry.Calls.Should().Equal("apply:chat", "apply:status", "stop:status", "start:status");
    }
}
```

- [ ] **Step 2: Run tests — expect compile errors (no `ConfigApplyPipeline` yet)**

```
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~ConfigApplyPipelineTests" --nologo
```

Expected: compile error — `ConfigApplyPipeline` does not exist.

- [ ] **Step 3: Create `ConfigApplyPipeline.cs`**

Path: `src/GuildRelay.Core/Config/ConfigApplyPipeline.cs`

```csharp
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GuildRelay.Core.Features;

namespace GuildRelay.Core.Config;

/// <summary>
/// Diffs two <see cref="AppConfig"/> snapshots and dispatches the correct
/// sequence of ApplyConfig / Start / Stop calls for each feature so that
/// running watchers pick up changes with minimal downtime.
/// </summary>
public static class ConfigApplyPipeline
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public static async Task DispatchAsync(
        AppConfig oldConfig,
        AppConfig newConfig,
        IFeatureRegistry registry,
        CancellationToken ct)
    {
        await DispatchFeatureAsync(
            name: "chat",
            oldEnabled: oldConfig.Chat.Enabled, newEnabled: newConfig.Chat.Enabled,
            oldCfg: oldConfig.Chat, newCfg: newConfig.Chat,
            needsRestart: ChatNeedsRestart,
            registry: registry,
            ct: ct).ConfigureAwait(false);

        await DispatchFeatureAsync(
            name: "audio",
            oldEnabled: oldConfig.Audio.Enabled, newEnabled: newConfig.Audio.Enabled,
            oldCfg: oldConfig.Audio, newCfg: newConfig.Audio,
            needsRestart: static (_, _) => false,
            registry: registry,
            ct: ct).ConfigureAwait(false);

        await DispatchFeatureAsync(
            name: "status",
            oldEnabled: oldConfig.Status.Enabled, newEnabled: newConfig.Status.Enabled,
            oldCfg: oldConfig.Status, newCfg: newConfig.Status,
            needsRestart: StatusNeedsRestart,
            registry: registry,
            ct: ct).ConfigureAwait(false);
    }

    private static async Task DispatchFeatureAsync<T>(
        string name,
        bool oldEnabled, bool newEnabled,
        T oldCfg, T newCfg,
        System.Func<T, T, bool> needsRestart,
        IFeatureRegistry registry,
        CancellationToken ct)
        where T : notnull
    {
        // Case 1: stays disabled — nothing runtime.
        if (!oldEnabled && !newEnabled) return;

        // Case 2: enabled -> disabled. Stop, no config push needed.
        if (oldEnabled && !newEnabled)
        {
            await registry.StopAsync(name).ConfigureAwait(false);
            return;
        }

        // Case 3: disabled -> enabled. Seed new config into the feature instance, then start.
        if (!oldEnabled && newEnabled)
        {
            await registry.ApplyConfigAsync(name, Serialize(newCfg)).ConfigureAwait(false);
            await registry.StartAsync(name, ct).ConfigureAwait(false);
            return;
        }

        // Case 4: both enabled. If config didn't change, no-op.
        if (EqualityComparer<T>.Default.Equals(oldCfg, newCfg)) return;

        // Otherwise push the new config, then restart if a baked-in field changed.
        await registry.ApplyConfigAsync(name, Serialize(newCfg)).ConfigureAwait(false);
        if (needsRestart(oldCfg, newCfg))
        {
            await registry.StopAsync(name).ConfigureAwait(false);
            await registry.StartAsync(name, ct).ConfigureAwait(false);
        }
    }

    private static JsonElement Serialize<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOpts);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    // Restart classifiers per the spec's restart-trigger table.
    private static bool ChatNeedsRestart(ChatConfig old, ChatConfig cur)
        => old.CaptureIntervalSec != cur.CaptureIntervalSec;

    private static bool StatusNeedsRestart(StatusConfig old, StatusConfig cur)
        => old.CaptureIntervalSec != cur.CaptureIntervalSec
        || old.DebounceSamples   != cur.DebounceSamples;
}
```

- [ ] **Step 4: Run the pipeline tests — expect pass**

```
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~ConfigApplyPipelineTests" --nologo
```

Expected: 11 PASS.

- [ ] **Step 5: Run the full Core test project — catch regressions**

```
dotnet test tests/GuildRelay.Core.Tests --nologo
```

Expected: all PASS.

- [ ] **Step 6: Commit**

```
git add src/GuildRelay.Core/Config/ConfigApplyPipeline.cs tests/GuildRelay.Core.Tests/Config/ConfigApplyPipelineTests.cs
git commit -m "feat(core): add ConfigApplyPipeline for live-apply settings changes"
```

---

### Task 3: `ConfigDirty` helper in Core + unit tests

Extract per-tab dirty-diff logic into a pure static helper in Core so the per-tab predicates (which include a mixed-tab split for Chat) can be unit tested without a WPF test host. Per-section equality uses `record` auto-Equals and needs no helper.

**Files:**
- Create: `src/GuildRelay.Core/Config/ConfigDirty.cs`
- Create: `tests/GuildRelay.Core.Tests/Config/ConfigDirtyTests.cs`

- [ ] **Step 1: Write failing tests**

Path: `tests/GuildRelay.Core.Tests/Config/ConfigDirtyTests.cs`

```csharp
using FluentAssertions;
using GuildRelay.Core.Config;
using Xunit;

namespace GuildRelay.Core.Tests.Config;

public class ConfigDirtyTests
{
    [Fact]
    public void ChatTabIsCleanForIdenticalConfigs()
    {
        var c = AppConfig.Default;
        ConfigDirty.IsDirtyChatTab(c, c).Should().BeFalse();
    }

    [Fact]
    public void ChatTabDirtyWhenEnabledFlips()
    {
        var saved = AppConfig.Default;
        var pending = saved with { Chat = saved.Chat with { Enabled = !saved.Chat.Enabled } };
        ConfigDirty.IsDirtyChatTab(pending, saved).Should().BeTrue();
    }

    [Fact]
    public void ChatTabDirtyWhenRegionChanges()
    {
        var saved = AppConfig.Default;
        var pending = saved with
        {
            Chat = saved.Chat with
            {
                Region = new RegionConfig(5, 5, 50, 50, 1.0, new ResolutionConfig(1920, 1080), "PRIMARY")
            }
        };
        ConfigDirty.IsDirtyChatTab(pending, saved).Should().BeTrue();
    }

    [Fact]
    public void ChatTabNotDirtyForChatAdvancedSettingsChange()
    {
        var saved = AppConfig.Default;
        var pending = saved with { Chat = saved.Chat with { CaptureIntervalSec = saved.Chat.CaptureIntervalSec + 1 } };

        ConfigDirty.IsDirtyChatTab(pending, saved).Should().BeFalse();
        ConfigDirty.IsDirtySettingsTab(pending, saved).Should().BeTrue();
    }

    [Fact]
    public void SettingsTabDirtyForGeneralChange()
    {
        var saved = AppConfig.Default;
        var pending = saved with { General = saved.General with { PlayerName = "Tosh" } };
        ConfigDirty.IsDirtySettingsTab(pending, saved).Should().BeTrue();
    }

    [Fact]
    public void SettingsTabDirtyForEachChatAdvancedField()
    {
        var saved = AppConfig.Default;

        var pendingInterval = saved with { Chat = saved.Chat with { CaptureIntervalSec = saved.Chat.CaptureIntervalSec + 1 } };
        var pendingConfidence = saved with { Chat = saved.Chat with { OcrConfidenceThreshold = saved.Chat.OcrConfidenceThreshold + 0.1 } };
        var pendingCooldown = saved with { Chat = saved.Chat with { DefaultCooldownSec = saved.Chat.DefaultCooldownSec + 1 } };

        ConfigDirty.IsDirtySettingsTab(pendingInterval, saved).Should().BeTrue();
        ConfigDirty.IsDirtySettingsTab(pendingConfidence, saved).Should().BeTrue();
        ConfigDirty.IsDirtySettingsTab(pendingCooldown, saved).Should().BeTrue();
    }

    [Fact]
    public void AnyDirtyIsTrueWhenAnySectionDiffers()
    {
        var saved = AppConfig.Default;
        var pending = saved with { Audio = saved.Audio with { Enabled = !saved.Audio.Enabled } };
        ConfigDirty.AnyDirty(pending, saved).Should().BeTrue();
    }

    [Fact]
    public void AnyDirtyIsFalseForIdenticalConfigs()
    {
        var c = AppConfig.Default;
        ConfigDirty.AnyDirty(c, c).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests — expect compile errors**

```
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~ConfigDirtyTests" --nologo
```

Expected: compile error — `ConfigDirty` does not exist.

- [ ] **Step 3: Create `ConfigDirty.cs`**

Path: `src/GuildRelay.Core/Config/ConfigDirty.cs`

```csharp
namespace GuildRelay.Core.Config;

/// <summary>
/// Pure per-tab / whole-config dirty predicates used by the Settings UI's
/// ViewModel. Kept in Core so it can be unit tested without WPF.
/// </summary>
public static class ConfigDirty
{
    public static bool AnyDirty(AppConfig pending, AppConfig saved)
        => !Equals(pending.Chat,    saved.Chat)
        || !Equals(pending.Audio,   saved.Audio)
        || !Equals(pending.Status,  saved.Status)
        || !Equals(pending.General, saved.General);

    // Chat tab edits: Enabled, Region, Rules. (CaptureIntervalSec /
    // OcrConfidenceThreshold / DefaultCooldownSec are edited on the Settings
    // tab even though they live in ChatConfig.)
    public static bool IsDirtyChatTab(AppConfig pending, AppConfig saved)
        => pending.Chat.Enabled != saved.Chat.Enabled
        || !Equals(pending.Chat.Region, saved.Chat.Region)
        || !Equals(pending.Chat.Rules,  saved.Chat.Rules);

    public static bool IsDirtyAudioTab(AppConfig pending, AppConfig saved)
        => !Equals(pending.Audio, saved.Audio);

    public static bool IsDirtyStatusTab(AppConfig pending, AppConfig saved)
        => !Equals(pending.Status, saved.Status);

    // Settings tab edits: the whole General section plus the Chat advanced subset.
    public static bool IsDirtySettingsTab(AppConfig pending, AppConfig saved)
        => !Equals(pending.General, saved.General)
        || pending.Chat.CaptureIntervalSec     != saved.Chat.CaptureIntervalSec
        || pending.Chat.OcrConfidenceThreshold != saved.Chat.OcrConfidenceThreshold
        || pending.Chat.DefaultCooldownSec     != saved.Chat.DefaultCooldownSec;
}
```

- [ ] **Step 4: Run tests — expect pass**

```
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~ConfigDirtyTests" --nologo
```

Expected: 8 PASS.

- [ ] **Step 5: Run the full Core test project — catch regressions**

```
dotnet test tests/GuildRelay.Core.Tests --nologo
```

Expected: all PASS.

- [ ] **Step 6: Commit**

```
git add src/GuildRelay.Core/Config/ConfigDirty.cs tests/GuildRelay.Core.Tests/Config/ConfigDirtyTests.cs
git commit -m "feat(core): add ConfigDirty per-tab/whole-config diff helper"
```

---

### Task 4: Extend `ConfigViewModel` with pending/saved state and dirty flags

Give the ViewModel a `PendingConfig` (working copy) and `SavedConfig` (snapshot), plus `INotifyPropertyChanged` flags that delegate to `ConfigDirty`. Add `SaveAsync`, `Revert`, and per-section `SetPending*` helpers.

**Files:**
- Modify: `src/GuildRelay.App/Config/ConfigViewModel.cs`

- [ ] **Step 1: Replace the entire body of `ConfigViewModel.cs`**

```csharp
using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GuildRelay.Core.Config;

namespace GuildRelay.App.Config;

public sealed class ConfigViewModel : INotifyPropertyChanged
{
    private AppConfig _savedConfig;
    private AppConfig _pendingConfig;

    public ConfigViewModel(CoreHost host)
    {
        Host = host;
        _savedConfig  = host.Config;
        _pendingConfig = DeepClone(host.Config);
    }

    public CoreHost Host { get; }

    public AppConfig SavedConfig => _savedConfig;
    public AppConfig PendingConfig => _pendingConfig;

    // --- Convenience accessors used by the Settings tab (webhook + player) ---

    public string WebhookUrl
    {
        get => _pendingConfig.General.WebhookUrl;
        set
        {
            if (string.Equals(_pendingConfig.General.WebhookUrl, value, StringComparison.Ordinal)) return;
            SetPendingGeneral(_pendingConfig.General with { WebhookUrl = value });
        }
    }

    public string PlayerName
    {
        get => _pendingConfig.General.PlayerName;
        set
        {
            if (string.Equals(_pendingConfig.General.PlayerName, value, StringComparison.Ordinal)) return;
            SetPendingGeneral(_pendingConfig.General with { PlayerName = value });
        }
    }

    // --- Section setters raise IsDirty* events ---

    public void SetPendingChat(ChatConfig value)
    {
        if (Equals(_pendingConfig.Chat, value)) return;
        _pendingConfig = _pendingConfig with { Chat = value };
        RaiseAllDirtyFlags();
    }

    public void SetPendingAudio(AudioConfig value)
    {
        if (Equals(_pendingConfig.Audio, value)) return;
        _pendingConfig = _pendingConfig with { Audio = value };
        RaiseAllDirtyFlags();
    }

    public void SetPendingStatus(StatusConfig value)
    {
        if (Equals(_pendingConfig.Status, value)) return;
        _pendingConfig = _pendingConfig with { Status = value };
        RaiseAllDirtyFlags();
    }

    public void SetPendingGeneral(GeneralConfig value)
    {
        if (Equals(_pendingConfig.General, value)) return;
        _pendingConfig = _pendingConfig with { General = value };
        RaiseAllDirtyFlags();
    }

    // --- Dirty flags (delegate to ConfigDirty in Core for testability) ---

    public bool IsDirty            => ConfigDirty.AnyDirty(_pendingConfig, _savedConfig);
    public bool IsDirtyChatTab     => ConfigDirty.IsDirtyChatTab(_pendingConfig, _savedConfig);
    public bool IsDirtyAudioTab    => ConfigDirty.IsDirtyAudioTab(_pendingConfig, _savedConfig);
    public bool IsDirtyStatusTab   => ConfigDirty.IsDirtyStatusTab(_pendingConfig, _savedConfig);
    public bool IsDirtySettingsTab => ConfigDirty.IsDirtySettingsTab(_pendingConfig, _savedConfig);

    // --- Save / Revert ---

    public async Task SaveAsync(CancellationToken ct = default)
    {
        var oldConfig = _savedConfig;
        var newConfig = _pendingConfig;

        await Host.ConfigStore.SaveAsync(newConfig).ConfigureAwait(false);
        Host.UpdateConfig(newConfig);

        await ConfigApplyPipeline.DispatchAsync(oldConfig, newConfig, Host.Registry, ct).ConfigureAwait(false);

        _savedConfig  = newConfig;
        _pendingConfig = DeepClone(newConfig);
        RaiseAllDirtyFlags();
    }

    public void Revert()
    {
        _pendingConfig = DeepClone(_savedConfig);
        RaiseAllDirtyFlags();
    }

    // --- Plumbing ---

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseAllDirtyFlags()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingConfig)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirtyChatTab)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirtyAudioTab)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirtyStatusTab)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirtySettingsTab)));
    }

    private static readonly JsonSerializerOptions CloneOpts = new() { WriteIndented = false };

    private static AppConfig DeepClone(AppConfig source)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(source, CloneOpts);
        return JsonSerializer.Deserialize<AppConfig>(bytes, CloneOpts)
            ?? throw new InvalidOperationException("AppConfig round-trip failed.");
    }
}
```

- [ ] **Step 2: Build the App**

```
dotnet build src/GuildRelay.App --nologo
```

Expected: 0 errors. (The old `Apply()` and `SaveAsync()` callers in `ConfigWindow.xaml.cs` still compile because we're about to rewrite them in Task 5.)

If the build shows errors like `'ConfigViewModel' does not contain a definition for 'Apply'`, that's expected — proceed; Task 5 removes the callers.

If the build fails for any other reason, stop and investigate.

- [ ] **Step 3: Commit**

```
git add src/GuildRelay.App/Config/ConfigViewModel.cs
git commit -m "feat(app): extend ConfigViewModel with pending/saved state and dirty flags"
```

---

### Task 5: `ConfigWindow` — footer Save/Revert, auto-save on close, dirty dots

Add the sticky footer, wire it to the ViewModel, handle `Ctrl+S`, auto-save on close, and bind the existing tab-header dots to `IsDirty*Tab` (keeping the current green "running" dot logic unchanged but repurposing the colour to indicate "unsaved" — the current enabled indicator will move into the tab label text style later; for this plan the enabled indicator is dropped in favour of the dirty dot, as per the spec's UI section). Delete the Settings tab's inline `Save` and `Close` buttons and `StatusText`.

**Files:**
- Modify: `src/GuildRelay.App/Config/ConfigWindow.xaml`
- Modify: `src/GuildRelay.App/Config/ConfigWindow.xaml.cs`

- [ ] **Step 1: Rewrite `ConfigWindow.xaml`**

Replace the entire file contents with:

```xml
<ui:FluentWindow x:Class="GuildRelay.App.Config.ConfigWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        xmlns:local="clr-namespace:GuildRelay.App.Config"
        Title="GuildRelay — Config" Width="600" Height="620"
        WindowStartupLocation="CenterScreen"
        ExtendsContentIntoTitleBar="True"
        Closing="OnClosing"
        PreviewKeyDown="OnPreviewKeyDown">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <ui:TitleBar Grid.Row="0" Title="GuildRelay — Config"/>

        <TabControl Grid.Row="1" Margin="12,4,12,0" x:Name="MainTabs">
            <TabItem x:Name="ChatTabItem">
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock x:Name="ChatDot" Text="●" Foreground="#FFB54C" Margin="0,0,4,0" FontSize="10" VerticalAlignment="Center" Visibility="Collapsed"/>
                        <TextBlock Text="Chat Watcher"/>
                    </StackPanel>
                </TabItem.Header>
                <local:ChatConfigTab x:Name="ChatTab"/>
            </TabItem>
            <TabItem x:Name="AudioTabItem">
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock x:Name="AudioDot" Text="●" Foreground="#FFB54C" Margin="0,0,4,0" FontSize="10" VerticalAlignment="Center" Visibility="Collapsed"/>
                        <TextBlock Text="Audio Watcher"/>
                    </StackPanel>
                </TabItem.Header>
                <local:AudioConfigTab x:Name="AudioTab"/>
            </TabItem>
            <TabItem x:Name="StatusTabItem">
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock x:Name="StatusDot" Text="●" Foreground="#FFB54C" Margin="0,0,4,0" FontSize="10" VerticalAlignment="Center" Visibility="Collapsed"/>
                        <TextBlock Text="Status Watcher"/>
                    </StackPanel>
                </TabItem.Header>
                <local:StatusConfigTab x:Name="StatusTab"/>
            </TabItem>
            <TabItem x:Name="SettingsTabItem">
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock x:Name="SettingsDot" Text="●" Foreground="#FFB54C" Margin="0,0,4,0" FontSize="10" VerticalAlignment="Center" Visibility="Collapsed"/>
                        <ui:SymbolIcon Symbol="Settings24" Margin="0,0,6,0" FontSize="14"/>
                        <TextBlock Text="Settings"/>
                    </StackPanel>
                </TabItem.Header>
                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <StackPanel Margin="12">
                        <TextBlock Text="Discord webhook URL" FontWeight="SemiBold"/>
                        <PasswordBox x:Name="WebhookBox" Height="32" Margin="0,4,0,12" PasswordChanged="OnWebhookChanged"/>
                        <TextBlock Text="Player name" FontWeight="SemiBold"/>
                        <TextBox x:Name="PlayerBox" Height="32" Margin="0,4,0,12" TextChanged="OnPlayerNameChanged"/>
                        <StackPanel Orientation="Horizontal">
                            <ui:Button Content="Test webhook" Click="OnTestWebhookClick" Appearance="Primary" Margin="0,0,8,0"/>
                        </StackPanel>

                        <Separator Margin="0,16,0,12"/>

                        <TextBlock Text="Chat Watcher" FontWeight="SemiBold" FontSize="14" Margin="0,0,0,8"/>

                        <TextBlock Text="Capture interval (seconds)" FontWeight="SemiBold"/>
                        <TextBox x:Name="IntervalBox" Width="120" HorizontalAlignment="Left" Margin="0,4,0,8" TextChanged="OnChatAdvancedChanged"/>

                        <TextBlock Text="OCR confidence threshold (0.0 - 1.0)" FontWeight="SemiBold"/>
                        <TextBox x:Name="ConfidenceBox" Width="120" HorizontalAlignment="Left" Margin="0,4,0,8" TextChanged="OnChatAdvancedChanged"/>

                        <TextBlock Text="Default rule cooldown (seconds)" FontWeight="SemiBold"/>
                        <TextBox x:Name="CooldownBox" Width="120" HorizontalAlignment="Left" Margin="0,4,0,4" TextChanged="OnChatAdvancedChanged"/>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>
        </TabControl>

        <!-- Sticky footer bar -->
        <Border Grid.Row="2" BorderThickness="0,1,0,0"
                BorderBrush="{ui:ThemeResource ControlStrokeColorDefaultBrush}"
                Background="{ui:ThemeResource SolidBackgroundFillColorSecondaryBrush}"
                Padding="12,8">
            <DockPanel>
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                    <ui:Button x:Name="RevertButton" Content="Revert" Click="OnRevertClick" Margin="0,0,8,0" IsEnabled="False"/>
                    <ui:Button x:Name="SaveButton"   Content="Save"   Click="OnSaveClick"   Appearance="Primary" IsEnabled="False"/>
                </StackPanel>
                <TextBlock x:Name="FooterStatusText" VerticalAlignment="Center"
                           Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"/>
            </DockPanel>
        </Border>
    </Grid>
</ui:FluentWindow>
```

- [ ] **Step 2: Rewrite `ConfigWindow.xaml.cs`**

Replace the entire file with:

```csharp
using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GuildRelay.Core.Events;

namespace GuildRelay.App.Config;

public partial class ConfigWindow : Wpf.Ui.Controls.FluentWindow
{
    private bool _loading;
    private bool _closing;

    public ConfigWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;

        _loading = true;
        WebhookBox.Password = vm.PendingConfig.General.WebhookUrl;
        PlayerBox.Text      = vm.PendingConfig.General.PlayerName;

        IntervalBox.Text   = vm.PendingConfig.Chat.CaptureIntervalSec.ToString();
        ConfidenceBox.Text = vm.PendingConfig.Chat.OcrConfidenceThreshold.ToString("F2");
        CooldownBox.Text   = vm.PendingConfig.Chat.DefaultCooldownSec.ToString();
        _loading = false;

        ChatTab.DataContext   = vm;
        AudioTab.DataContext  = vm;
        StatusTab.DataContext = vm;

        vm.PropertyChanged += OnViewModelPropertyChanged;
        UpdateDirtyUi(vm);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        UpdateDirtyUi(vm);
    }

    private const string UnsavedText = "● Unsaved changes";

    private void UpdateDirtyUi(ConfigViewModel vm)
    {
        SaveButton.IsEnabled   = vm.IsDirty;
        RevertButton.IsEnabled = vm.IsDirty;

        if (vm.IsDirty)
            FooterStatusText.Text = UnsavedText;
        else if (FooterStatusText.Text == UnsavedText)
            FooterStatusText.Text = "";

        ChatDot.Visibility     = vm.IsDirtyChatTab     ? Visibility.Visible : Visibility.Collapsed;
        AudioDot.Visibility    = vm.IsDirtyAudioTab    ? Visibility.Visible : Visibility.Collapsed;
        StatusDot.Visibility   = vm.IsDirtyStatusTab   ? Visibility.Visible : Visibility.Collapsed;
        SettingsDot.Visibility = vm.IsDirtySettingsTab ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S
            && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && DataContext is ConfigViewModel vm
            && vm.IsDirty)
        {
            e.Handled = true;
            await DoSaveAsync(vm);
        }
    }

    // --- Settings tab edit handlers ---

    private void OnWebhookChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || DataContext is not ConfigViewModel vm) return;
        vm.WebhookUrl = WebhookBox.Password;
    }

    private void OnPlayerNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || DataContext is not ConfigViewModel vm) return;
        vm.PlayerName = PlayerBox.Text;
    }

    private void OnChatAdvancedChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || DataContext is not ConfigViewModel vm) return;

        var iv = int.TryParse(IntervalBox.Text, out var i) ? i : vm.PendingConfig.Chat.CaptureIntervalSec;
        var ct = double.TryParse(ConfidenceBox.Text, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out var c)
                 ? c : vm.PendingConfig.Chat.OcrConfidenceThreshold;
        var cd = int.TryParse(CooldownBox.Text, out var d) ? d : vm.PendingConfig.Chat.DefaultCooldownSec;

        vm.SetPendingChat(vm.PendingConfig.Chat with
        {
            CaptureIntervalSec    = iv,
            OcrConfidenceThreshold = ct,
            DefaultCooldownSec    = cd
        });
    }

    // --- Footer actions ---

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        await DoSaveAsync(vm);
    }

    private void OnRevertClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        vm.Revert();
        ReloadLocalUiFields(vm);
        FooterStatusText.Text = "Reverted to saved.";
    }

    private async System.Threading.Tasks.Task DoSaveAsync(ConfigViewModel vm)
    {
        try
        {
            await vm.SaveAsync();
            FooterStatusText.Text = "Saved.";
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = "Save failed — see logs";
            vm.Host.Logger.Error(ex, "Saving config failed");
        }
    }

    private void ReloadLocalUiFields(ConfigViewModel vm)
    {
        _loading = true;
        WebhookBox.Password = vm.PendingConfig.General.WebhookUrl;
        PlayerBox.Text      = vm.PendingConfig.General.PlayerName;
        IntervalBox.Text    = vm.PendingConfig.Chat.CaptureIntervalSec.ToString();
        ConfidenceBox.Text  = vm.PendingConfig.Chat.OcrConfidenceThreshold.ToString("F2");
        CooldownBox.Text    = vm.PendingConfig.Chat.DefaultCooldownSec.ToString();
        _loading = false;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        if (DataContext is not ConfigViewModel vm) return;
        if (!vm.IsDirty) return;

        e.Cancel = true;
        _closing = true;
        await DoSaveAsync(vm);
        Close();
    }

    // --- Test webhook (operates on PendingConfig, does not persist) ---

    private async void OnTestWebhookClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        FooterStatusText.Text = "Testing webhook...";
        try
        {
            vm.Host.Secrets.SetWebhookUrl(WebhookBox.Password);

            await vm.Host.Publisher.PublishAsync(new DetectionEvent(
                FeatureId: "test",
                RuleLabel: "Connection test",
                MatchedContent: $"GuildRelay connected - hello from {PlayerBox.Text}",
                TimestampUtc: DateTimeOffset.UtcNow,
                PlayerName: PlayerBox.Text,
                Extras: new System.Collections.Generic.Dictionary<string, string>(),
                ImageAttachment: null), CancellationToken.None);

            FooterStatusText.Text = "Test message sent.";
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = "Test failed: " + ex.Message;
            vm.Host.Logger.Error(ex, "Test webhook post failed");
        }
        finally
        {
            // Restore the currently-saved webhook URL so an un-saved test doesn't
            // linger as the active secret for running features.
            vm.Host.Secrets.SetWebhookUrl(vm.SavedConfig.General.WebhookUrl);
        }
    }
}
```

- [ ] **Step 3: Build the App — expect errors from tabs still calling removed methods**

```
dotnet build src/GuildRelay.App --nologo
```

Expected: build errors from `ChatConfigTab.xaml.cs`, `AudioConfigTab.xaml.cs`, `StatusConfigTab.xaml.cs` because they still reference `_host.ConfigStore.SaveAsync`, `_host.Registry.StopAsync`, `window.UpdateIndicators`, etc. These will disappear once Tasks 6–8 rewrite the tabs.

If there are unexpected errors in `ConfigWindow.xaml.cs` itself (not from the tab files), stop and investigate.

- [ ] **Step 4: Commit (partial — tabs still broken)**

```
git add src/GuildRelay.App/Config/ConfigWindow.xaml src/GuildRelay.App/Config/ConfigWindow.xaml.cs
git commit -m "feat(app-ui): sticky footer Save/Revert + auto-save on close (WIP: tabs broken)"
```

The commit message flags the WIP state so reviewers know the build will stay red until Tasks 6–8 land.

---

### Task 6: Refactor `ChatConfigTab` — drop OnSave, push edits into VM

Remove the "Save Chat Settings" button, `OnSave`, `OnToggleChanged` live-save path, and all local mirror state (`_rules`, `_currentRegion`, `_loading`, `DebugLiveView` stays). Load initial values from `vm.PendingConfig.Chat`; push updates to `vm.SetPendingChat(...)` on every edit.

**Files:**
- Modify: `src/GuildRelay.App/Config/ChatConfigTab.xaml`
- Modify: `src/GuildRelay.App/Config/ChatConfigTab.xaml.cs`

- [ ] **Step 1: Rewrite `ChatConfigTab.xaml`**

Remove the `Save Chat Settings` button and the `StatusText` TextBlock. No other field layout changes.

```xml
<UserControl x:Class="GuildRelay.App.Config.ChatConfigTab"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="12">
            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                <ui:ToggleSwitch x:Name="EnabledToggle" IsChecked="False" Checked="OnEnabledChanged" Unchecked="OnEnabledChanged"/>
                <TextBlock Text="Chat Watcher" VerticalAlignment="Center" FontWeight="SemiBold" FontSize="14" Margin="8,0,0,0"/>
                <ui:Button Content="Live View" Click="OnOpenLiveView" Margin="16,0,0,0" FontSize="11"/>
            </StackPanel>

            <TextBlock Text="Chat region" FontWeight="SemiBold"/>
            <StackPanel Orientation="Horizontal" Margin="0,4,0,12">
                <ui:Button Content="Pick region" Click="OnPickRegion" Margin="0,0,8,0"/>
                <TextBlock x:Name="RegionLabel" VerticalAlignment="Center"
                           Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                           Text="No region selected"/>
            </StackPanel>

            <!-- Rule templates -->
            <TextBlock Text="Rule templates" FontWeight="SemiBold"/>
            <StackPanel Orientation="Horizontal" Margin="0,4,0,12">
                <ComboBox x:Name="TemplateCombo" Width="220" Margin="0,0,8,0"/>
                <ui:Button Content="Load Template" Click="OnLoadTemplate"/>
            </StackPanel>

            <!-- Active rules list with action buttons -->
            <DockPanel Margin="0,0,0,4">
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                    <ui:Button Content="+" Click="OnAddRule" Width="32" Margin="4,0,0,0" ToolTip="Add rule"/>
                    <ui:Button x:Name="EditRuleButton" Content="✎" Click="OnEditRule" Width="32" Margin="4,0,0,0" IsEnabled="False" ToolTip="Edit selected rule"/>
                    <ui:Button x:Name="RemoveRuleButton" Content="—" Click="OnRemoveRule" Width="32" Margin="4,0,0,0" IsEnabled="False" ToolTip="Remove selected rule"/>
                </StackPanel>
                <TextBlock Text="Active rules" FontWeight="SemiBold" VerticalAlignment="Center"/>
            </DockPanel>
            <ListBox x:Name="RulesList" Height="140" Margin="0,4,0,12"
                     SelectionChanged="OnRulesListSelectionChanged"
                     MouseDoubleClick="OnRulesListDoubleClick"/>

            <!-- Test message -->
            <TextBlock Text="Test a message against your rules" FontWeight="SemiBold" Margin="0,8,0,0"/>
            <StackPanel Orientation="Horizontal" Margin="0,4,0,4">
                <TextBox x:Name="TestMessageBox" Width="350" Margin="0,0,8,0" FontFamily="Consolas"/>
                <ui:Button Content="Test" Click="OnTestMessage"/>
            </StackPanel>
            <TextBlock x:Name="TestResultText" Margin="0,4,0,0"
                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" TextWrapping="Wrap"/>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

- [ ] **Step 2: Rewrite `ChatConfigTab.xaml.cs`**

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
        EnabledToggle.IsChecked = chat.Enabled;
        UpdateRegionLabel(chat.Region);
        RefreshRulesList();

        TemplateCombo.ItemsSource = RuleTemplates.BuiltInNames;
        if (RuleTemplates.BuiltInNames.Count > 0)
            TemplateCombo.SelectedIndex = 0;
        _loading = false;
    }

    private void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _vm is null) return;
        _vm.SetPendingChat(_vm.PendingConfig.Chat with { Enabled = EnabledToggle.IsChecked ?? false });
    }

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
        RegionLabel.Text = region.IsEmpty
            ? "No region selected"
            : $"{region.X},{region.Y} {region.Width}x{region.Height}";
    }

    // --- Rules list ---

    private void RefreshRulesList()
    {
        if (_vm is null) return;
        var selected = RulesList.SelectedIndex;
        RulesList.Items.Clear();
        foreach (var r in _vm.PendingConfig.Chat.Rules)
            RulesList.Items.Add(FormatRuleSummary(r));
        if (selected >= 0 && selected < RulesList.Items.Count)
            RulesList.SelectedIndex = selected;
        UpdateActionButtons();
    }

    private static string FormatRuleSummary(StructuredChatRule r)
    {
        var channels = r.Channels.Count == 0
            ? "all channels"
            : string.Join(", ", r.Channels);
        var keywords = r.Keywords.Count == 0 ? "all messages" : $"{r.Keywords.Count} keywords";
        var mode = r.MatchMode == MatchMode.Regex ? " (regex)" : "";
        return $"{r.Label}  —  {channels}  —  {keywords}{mode}  —  {r.CooldownSec}s";
    }

    private void UpdateActionButtons()
    {
        bool hasSelection = RulesList.SelectedIndex >= 0;
        EditRuleButton.IsEnabled   = hasSelection;
        RemoveRuleButton.IsEnabled = hasSelection;
    }

    private void OnRulesListSelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateActionButtons();

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
        else
        {
            _debugWindow.Activate();
        }
    }

    // --- Test message (uses PendingConfig's rules only) ---

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

- [ ] **Step 3: Build the App (Audio and Status tabs still broken — expected)**

```
dotnet build src/GuildRelay.App --nologo
```

Expected: build errors only from `AudioConfigTab.xaml.cs` and `StatusConfigTab.xaml.cs`. Chat-specific and ConfigWindow errors should be gone. If any Chat-specific errors remain, stop and fix.

- [ ] **Step 4: Commit**

```
git add src/GuildRelay.App/Config/ChatConfigTab.xaml src/GuildRelay.App/Config/ChatConfigTab.xaml.cs
git commit -m "feat(chat-ui): drive ChatConfigTab via PendingConfig, remove inline Save"
```

---

### Task 7: Refactor `AudioConfigTab`

**Files:**
- Modify: `src/GuildRelay.App/Config/AudioConfigTab.xaml`
- Modify: `src/GuildRelay.App/Config/AudioConfigTab.xaml.cs`

- [ ] **Step 1: Rewrite `AudioConfigTab.xaml`**

```xml
<UserControl x:Class="GuildRelay.App.Config.AudioConfigTab"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="12">
            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                <ui:ToggleSwitch x:Name="EnabledToggle" IsChecked="False" Checked="OnEnabledChanged" Unchecked="OnEnabledChanged"/>
                <TextBlock Text="Audio Watcher" VerticalAlignment="Center" FontWeight="SemiBold" FontSize="14" Margin="8,0,0,0"/>
            </StackPanel>
            <ui:InfoBar Title="System audio warning" IsOpen="True" IsClosable="False" Severity="Warning" Margin="0,0,0,12"
                        Message="Audio detection uses WASAPI loopback (system audio output). Discord voice, music, and browser audio can cause false matches."/>
            <TextBlock Text="Rules (one per line: label|path-to-wav|sensitivity|cooldown-sec)" FontWeight="SemiBold"/>
            <TextBox x:Name="RulesBox" AcceptsReturn="True" Height="120"
                     VerticalScrollBarVisibility="Auto" Margin="0,4,0,8"
                     FontFamily="Consolas" TextWrapping="Wrap"
                     TextChanged="OnRulesChanged"/>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

- [ ] **Step 2: Rewrite `AudioConfigTab.xaml.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GuildRelay.Core.Config;

namespace GuildRelay.App.Config;

public partial class AudioConfigTab : UserControl
{
    private ConfigViewModel? _vm;
    private bool _loading;

    public AudioConfigTab() { InitializeComponent(); Loaded += OnLoaded; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as ConfigViewModel;
        if (_vm is null) return;

        _loading = true;
        var audio = _vm.PendingConfig.Audio;
        EnabledToggle.IsChecked = audio.Enabled;

        var ruleLines = audio.Rules.Select(r =>
            $"{r.Label}|{r.ClipPath}|{r.Sensitivity.ToString("F2", CultureInfo.InvariantCulture)}|{r.CooldownSec}");
        RulesBox.Text = string.Join(Environment.NewLine, ruleLines);
        _loading = false;
    }

    private void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _vm is null) return;
        _vm.SetPendingAudio(_vm.PendingConfig.Audio with { Enabled = EnabledToggle.IsChecked ?? false });
    }

    private void OnRulesChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _vm is null) return;
        var rules = ParseRules(RulesBox.Text);
        _vm.SetPendingAudio(_vm.PendingConfig.Audio with { Rules = rules });
    }

    private static List<AudioRuleConfig> ParseRules(string text)
    {
        var rules = new List<AudioRuleConfig>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('|', 4);
            if (parts.Length < 4) continue;
            var label = parts[0].Trim();
            var clipPath = parts[1].Trim();
            var sensitivity = double.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0.8;
            var cooldown = int.TryParse(parts[3].Trim(), out var c) ? c : 15;
            rules.Add(new AudioRuleConfig(
                Id: label.ToLowerInvariant().Replace(' ', '_'),
                Label: label,
                ClipPath: clipPath,
                Sensitivity: sensitivity,
                CooldownSec: cooldown));
        }
        return rules;
    }
}
```

- [ ] **Step 3: Build the App (Status tab still broken — expected)**

```
dotnet build src/GuildRelay.App --nologo
```

Expected: build errors only from `StatusConfigTab.xaml.cs`.

- [ ] **Step 4: Commit**

```
git add src/GuildRelay.App/Config/AudioConfigTab.xaml src/GuildRelay.App/Config/AudioConfigTab.xaml.cs
git commit -m "feat(audio-ui): drive AudioConfigTab via PendingConfig, remove inline Save"
```

---

### Task 8: Refactor `StatusConfigTab`

**Files:**
- Modify: `src/GuildRelay.App/Config/StatusConfigTab.xaml`
- Modify: `src/GuildRelay.App/Config/StatusConfigTab.xaml.cs`

- [ ] **Step 1: Rewrite `StatusConfigTab.xaml`**

```xml
<UserControl x:Class="GuildRelay.App.Config.StatusConfigTab"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="12">
            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                <ui:ToggleSwitch x:Name="EnabledToggle" IsChecked="False" Checked="OnEnabledChanged" Unchecked="OnEnabledChanged"/>
                <TextBlock Text="Status Watcher" VerticalAlignment="Center" FontWeight="SemiBold" FontSize="14" Margin="8,0,0,0"/>
            </StackPanel>

            <TextBlock Text="Disconnect dialog region" FontWeight="SemiBold"/>
            <StackPanel Orientation="Horizontal" Margin="0,4,0,8">
                <ui:Button Content="Pick region" Click="OnPickRegion" Margin="0,0,8,0"/>
                <TextBlock x:Name="RegionLabel" VerticalAlignment="Center"
                           Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                           Text="No region selected"/>
            </StackPanel>

            <TextBlock Text="Capture interval (seconds)" FontWeight="SemiBold"/>
            <TextBox x:Name="IntervalBox" Width="120" HorizontalAlignment="Left" Margin="0,4,0,8"
                     TextChanged="OnFieldChanged"/>

            <TextBlock Text="Debounce samples (consecutive confirmations)" FontWeight="SemiBold"/>
            <TextBox x:Name="DebounceBox" Width="120" HorizontalAlignment="Left" Margin="0,4,0,8"
                     TextChanged="OnFieldChanged"/>

            <TextBlock Text="Disconnect phrases (one per line: label|pattern|regex)" FontWeight="SemiBold"/>
            <TextBox x:Name="PatternsBox" AcceptsReturn="True" Height="80"
                     VerticalScrollBarVisibility="Auto" Margin="0,4,0,8"
                     FontFamily="Consolas" TextWrapping="Wrap"
                     TextChanged="OnFieldChanged"/>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

- [ ] **Step 2: Rewrite `StatusConfigTab.xaml.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GuildRelay.App.RegionPicker;
using GuildRelay.Core.Config;

namespace GuildRelay.App.Config;

public partial class StatusConfigTab : UserControl
{
    private ConfigViewModel? _vm;
    private bool _loading;

    public StatusConfigTab() { InitializeComponent(); Loaded += OnLoaded; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as ConfigViewModel;
        if (_vm is null) return;

        _loading = true;
        var status = _vm.PendingConfig.Status;
        EnabledToggle.IsChecked = status.Enabled;
        IntervalBox.Text  = status.CaptureIntervalSec.ToString();
        DebounceBox.Text  = status.DebounceSamples.ToString();
        UpdateRegionLabel(status.Region);

        var lines = status.DisconnectPatterns.Select(p =>
            $"{p.Label}|{p.Pattern}|{(p.Regex ? "regex" : "literal")}");
        PatternsBox.Text = string.Join(Environment.NewLine, lines);
        _loading = false;
    }

    private void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _vm is null) return;
        _vm.SetPendingStatus(_vm.PendingConfig.Status with { Enabled = EnabledToggle.IsChecked ?? false });
    }

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
            _vm.SetPendingStatus(_vm.PendingConfig.Status with { Region = region });
            UpdateRegionLabel(region);
        }
    }

    private void UpdateRegionLabel(RegionConfig region)
    {
        RegionLabel.Text = region.IsEmpty
            ? "No region selected"
            : $"{region.X},{region.Y} {region.Width}x{region.Height}";
    }

    private void OnFieldChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _vm is null) return;
        var current = _vm.PendingConfig.Status;
        var interval = int.TryParse(IntervalBox.Text, out var iv) ? iv : current.CaptureIntervalSec;
        var debounce = int.TryParse(DebounceBox.Text, out var db) ? db : current.DebounceSamples;
        var patterns = ParsePatterns(PatternsBox.Text);
        _vm.SetPendingStatus(current with
        {
            CaptureIntervalSec  = interval,
            DebounceSamples     = debounce,
            DisconnectPatterns  = patterns
        });
    }

    private static List<DisconnectPatternConfig> ParsePatterns(string text)
    {
        var patterns = new List<DisconnectPatternConfig>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('|');
            if (parts.Length < 2) continue;
            var label = parts[0].Trim();
            var pattern = parts[1].Trim();
            var isRegex = parts.Length > 2 && parts[2].Trim().Equals("regex", StringComparison.OrdinalIgnoreCase);
            patterns.Add(new DisconnectPatternConfig(
                Id: label.ToLowerInvariant().Replace(' ', '_'),
                Label: label,
                Pattern: pattern,
                Regex: isRegex));
        }
        return patterns;
    }
}
```

- [ ] **Step 3: Build the whole App — expect clean build**

```
dotnet build src/GuildRelay.App --nologo
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Commit**

```
git add src/GuildRelay.App/Config/StatusConfigTab.xaml src/GuildRelay.App/Config/StatusConfigTab.xaml.cs
git commit -m "feat(status-ui): drive StatusConfigTab via PendingConfig, remove inline Save"
```

---

### Task 9: Remove stale `ConfigWindow.UpdateIndicators` call sites

The old `UpdateIndicators(vm)` public method is no longer called because `OnToggleChanged` in tabs was removed. The method itself may still be present — and it conflicted with the new dirty-dot rendering by mutating `ChatDot/AudioDot/StatusDot`. Remove it entirely so nothing else calls it.

**Files:**
- Modify: `src/GuildRelay.App/Config/ConfigWindow.xaml.cs` (only if `UpdateIndicators` is still present)

- [ ] **Step 1: Check for residual `UpdateIndicators` references**

```
grep -rn "UpdateIndicators" src/ tests/
```

Expected output: no matches. The ConfigWindow rewrite in Task 5 already excluded it; the tab rewrites in Tasks 6–8 removed the callers.

If any reference remains, open that file and delete it, then re-run the grep.

- [ ] **Step 2: Build & full test suite**

```
dotnet build --nologo
dotnet test --nologo
```

Expected build: `0 Error(s)`. Expected tests: all PASS (baseline 140 + 2 `FeatureRegistryApplyConfigTests` + 11 `ConfigApplyPipelineTests` + 8 `ConfigDirtyTests` = 161 total).

- [ ] **Step 3: Commit (only if files changed)**

```
git status
```

If clean, skip. Otherwise:

```
git add -A
git commit -m "chore: remove stale UpdateIndicators references"
```

---

### Task 10: Manual smoke verification

Runtime-verifiable behaviour the automated tests cannot cover. Operator performs these; the plan lists them so the implementing agent knows what "done" looks like.

- [ ] **Step 1: Build the final single-file publish to smoke the XAML load**

```
dotnet publish src/GuildRelay.App -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true --nologo
```

Expected: publish succeeds with 0 errors. XAML parsing failures would surface as `MarkupException` at runtime — a clean publish plus a one-shot run in the next step catches them.

- [ ] **Step 2: Launch the published app and confirm the settings window opens**

Run the produced `GuildRelay.App.exe` from `src\GuildRelay.App\bin\Release\net8.0-windows10.0.22621.0\win-x64\publish\`. Open the config window from the tray icon.

Expected: no exception dialog, footer bar visible with `Save` + `Revert` (both disabled), no dirty dots on tab headers.

If a `MarkupException` or similar pops up, stop and fix.

- [ ] **Step 3: Manually run the acceptance checklist from the spec**

Perform each of the seven manual smoke tests from `docs/superpowers/specs/2026-04-15-single-save-button-settings-design.md` §Testing. Record the result inline here:

1. Change Capture interval 5 → 2 on Settings tab while Chat is enabled with a region set, click Save. OCR thumbnails in Live View should refresh every ~2s afterwards. *(regression for original bug)*
2. Edit a rule keyword on Chat tab, click Save. Live View shows new keyword matching on next tick with no visible restart.
3. Flip Chat Enabled off, Save → watcher stops.
4. Flip Chat Enabled on with a valid region, Save → watcher starts.
5. Edit webhook URL on Settings tab, click Test webhook — test post fires using the typed URL. Click Revert — the old URL is restored and Save stays disabled.
6. Close the window mid-edit — changes persist and apply.
7. Edit a Chat rule AND a Status interval → Save → Chat hot-applies, Status restarts.

If any fail, stop and investigate.

- [ ] **Step 4: Commit any residual fixes from manual testing**

```
git status
```

If clean, skip. Otherwise commit with a targeted message describing each fix.

---

## Acceptance

- Core: `IFeatureRegistry`, `FeatureRegistry.ApplyConfigAsync`, `ConfigApplyPipeline`, and `ConfigDirty` exist and are unit-tested (21 new tests in Core.Tests).
- App: `ConfigViewModel` exposes `PendingConfig`, `SavedConfig`, `IsDirty*`, `SaveAsync`, `Revert`.
- `ConfigWindow` has a sticky footer with Save + Revert + dirty dots on each tab header; inline Save/Close on the Settings tab are gone.
- Per-tab Save buttons (`Save Chat/Audio/Status Settings`) are gone from all four tabs.
- Enabled toggles no longer call SaveAsync/Stop/Start side effects; they mutate `PendingConfig` only.
- Closing the window with unsaved changes triggers the Save pipeline automatically.
- `Ctrl+S` saves.
- Full test suite passes (baseline 140 + 21 new = 161).
- Manual smoke test §Step 3 case 1 passes (capture-interval bug fix verified end-to-end).
