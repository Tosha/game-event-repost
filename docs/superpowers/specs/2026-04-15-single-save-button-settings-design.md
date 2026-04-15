# Single Save Button + Live Apply for Settings

## Problem

Three problems with the current Settings UX motivate this redesign:

1. **Capture-interval changes don't apply at runtime.** The Chat Watcher's `CaptureIntervalSec` lives on the **Settings** tab. `ConfigWindow.OnSaveClick` (ConfigWindow.xaml.cs:67) persists the new value and updates `_host.Config`, but never calls `Registry.StopAsync("chat") / StartAsync("chat")`. The running `ChatWatcher` keeps its `PeriodicTimer` baked in with the old interval (ChatWatcher.cs:104). Saving "worked" but produced no observable change.
2. **Save buttons are scattered and inconsistent.** The Chat, Audio, and Status tabs each have their own "Save Chat/Audio/Status Settings" button. The Settings tab has a separate `Save` at the top that also saves Chat Watcher advanced fields below a separator — but that Save button is visually above the Chat Watcher section, not under it, making the relationship unclear.
3. **Enabled toggles bypass Save entirely.** `OnToggleChanged` on every tab calls `SaveAsync` + `Stop/StartAsync` the moment a switch flips. Every other input is deferred until Save. The "what does Save actually cover" contract is split along invisible lines.

## Goal

One global Save button that saves every setting at once and applies changes to running watchers, with minimal observation downtime.

## Non-goals

- Changing field layout, the set of editable fields, or which tab hosts which field. Every current control stays exactly where it is.
- Hot-swapping fields that today require a watcher restart (capture interval, debounce samples). These keep their stop+start behavior; the design just gates that restart behind the global Save. Region and rule/pattern edits are hot-applied via the existing `ApplyConfig` hook on each watcher.
- Any change to the Discord webhook secret handling or to `ConfigStore`'s on-disk format.

## Architecture

Three units change:

1. **`ConfigViewModel`** gains a `PendingConfig` (working copy of `AppConfig`), a `SavedConfig` snapshot, and dirty-flag observables per feature. All tab controls bind to `PendingConfig` instead of holding local mirror state.
2. **`ConfigWindow`** gets a sticky footer bar with `Save` + `Revert` buttons and a combined dirty indicator. The footer drives a single pipeline: `ConfigStore.SaveAsync` → `CoreHost.UpdateConfig` → per-feature decision (stop+start / `ApplyConfig` / no-op) → update `SavedConfig`.
3. **`Registry`** gets one new method, `ApplyConfigAsync(name, JsonElement)`, which delegates to the named feature's already-existing `IFeature.ApplyConfig`. This closes the loop that today leaves `ApplyConfig` unreachable from the host.

Per-tab `OnSave` handlers, `OnToggleChanged` side-effects, and local mirror fields (`_rules`, `_currentRegion`, `_channelChecks`, etc.) are deleted. Tabs become thin views.

## State model

When `ConfigWindow` opens:

- `vm.SavedConfig = vm.Host.Config` (reference snapshot).
- `vm.PendingConfig = DeepClone(vm.Host.Config)`.

Tabs bind two-way to `PendingConfig`. Editing a field never mutates `SavedConfig` or `_host.Config`.

Two layers of dirty flags are computed by structural comparison. The **per-section** flags drive the Save pipeline's diff-dispatch; the **per-tab** flags drive the tab header dots.

Per-section flags — used by Save:

- `IsDirtyChatSection   = !Equals(PendingConfig.Chat,    SavedConfig.Chat)`
- `IsDirtyAudioSection  = !Equals(PendingConfig.Audio,   SavedConfig.Audio)`
- `IsDirtyStatusSection = !Equals(PendingConfig.Status,  SavedConfig.Status)`
- `IsDirtyGeneralSection = !Equals(PendingConfig.General, SavedConfig.General)`

Per-tab flags — used by tab header dots. The Settings tab is a mixed tab that edits both `General` fields and a subset of `Chat` fields; the Chat tab edits the rest of `Chat`.

- `IsDirtyChatTab = Enabled / Region / Rules subset of PendingConfig.Chat differs from SavedConfig.Chat`
- `IsDirtyAudioTab = IsDirtyAudioSection`
- `IsDirtyStatusTab = IsDirtyStatusSection`
- `IsDirtySettingsTab = IsDirtyGeneralSection OR (CaptureIntervalSec / OcrConfidenceThreshold / DefaultCooldownSec subset of PendingConfig.Chat differs)`

Global: `IsDirty = IsDirtyChatSection || IsDirtyAudioSection || IsDirtyStatusSection || IsDirtyGeneralSection`.

All flags raise `INotifyPropertyChanged` whenever `PendingConfig` is mutated.

**Deep-clone strategy:** `AppConfig` and its nested records are `record`s with `List<T>` fields (e.g. `Rules`, `DisconnectPatterns`). `with` expressions share list references; a true clone serializes to JSON and deserializes, which `System.Text.Json` can already do using the existing `ConfigStore` options.

## Save pipeline

Triggered by the footer `Save` button, by `Ctrl+S`, or by `ConfigWindow.OnClosing` when `IsDirty`.

1. **Persist.** `await vm.Host.ConfigStore.SaveAsync(vm.PendingConfig);`
2. **Swap in-memory config.** `vm.Host.UpdateConfig(vm.PendingConfig);` — this already propagates `Secrets.SetWebhookUrl` for the General/Settings webhook field, so no extra work is needed for the webhook.
3. **Per-feature dispatch.** For each of `chat`, `audio`, `status`:
   - `old = SavedConfig.<feature>`, `new = PendingConfig.<feature>`.
   - If `old.Enabled && !new.Enabled`: `Registry.StopAsync(name)`.
   - Else if `!old.Enabled && new.Enabled`: `Registry.StartAsync(name, CancellationToken.None)`.
   - Else if `old.Enabled && new.Enabled`:
     - If any **restart-trigger field** (see below) differs: `StopAsync` then `StartAsync`.
     - Else if anything else differs: `Registry.ApplyConfigAsync(name, SerializeFeatureSection(new))`.
     - Else: no-op.
   - Else (both disabled): no-op.
4. **Refresh snapshot.** `vm.SavedConfig = vm.PendingConfig; vm.PendingConfig = DeepClone(vm.SavedConfig);` — dirty flags collapse to false by construction.
5. **Refresh indicators.** `ConfigWindow.UpdateIndicators(vm)` (already exists for the enabled-dot) runs against the new `SavedConfig`.

Failure handling: if any step throws, abort without updating `SavedConfig`. Log via Serilog with structured fields. The footer status area shows `Save failed — see logs`; the `IsDirty` state is preserved so the user can retry or revert.

### `Registry.ApplyConfigAsync`

```csharp
public Task ApplyConfigAsync(string name, JsonElement featureConfig)
    => Get(name) is IFeature f ? f.ApplyConfig(featureConfig) : Task.CompletedTask;
```

The three watchers already have working `ApplyConfig` implementations (ChatWatcher.cs:94, AudioWatcher.cs:64, StatusWatcher.cs:73). They're currently dead code. This single method wakes them up.

### Restart-trigger classification

Fields that require a watcher stop+start because they're baked into a `PeriodicTimer` or a state machine at `StartAsync` time and are not touched by the existing `ApplyConfig`. Everything else is hot-applied via `ApplyConfig`.

| Feature | Restart triggers | Hot-apply |
|---|---|---|
| Chat | `CaptureIntervalSec` | `Region`, `Rules`, `DefaultCooldownSec`, `OcrConfidenceThreshold` |
| Status | `CaptureIntervalSec`, `DebounceSamples` | `Region`, `DisconnectPatterns` |
| Audio | (none besides the `Enabled` transition itself) | `Rules`, `Templates` |
| General (webhook / player name) | (none — `UpdateConfig` already routes the webhook secret) | all |

Why `Region` is *not* a restart trigger: both `ChatWatcher.ProcessOneTickAsync` and `StatusWatcher.CaptureOneFrame` read `_config.Region` fresh each tick (ChatWatcher.cs:124–127, StatusWatcher.cs:99–104). `ApplyConfig` replaces `_config` atomically, so the next tick uses the new region without any restart.

Why `DebounceSamples` is a restart trigger despite being "just a number": `StatusWatcher` constructs `ConnectionStateMachine` with `config.DebounceSamples` at `StartAsync` (StatusWatcher.cs:60). The current `ApplyConfig` updates `_config` and `_patterns` but does not rebuild the state machine. Restarting the watcher recreates the state machine with the new sample count. Expanding `ApplyConfig` to rebuild the state machine is a possible later improvement but is out of scope here.

Adding live hot-swap for capture intervals and debounce is possible in a later iteration but is explicitly out of scope.

### Revert

`Revert` button: `vm.PendingConfig = DeepClone(vm.SavedConfig);` — bindings refresh all controls. No disk or runtime work.

### Close-while-dirty

Per prior decision: silent auto-save. `ConfigWindow.OnClosing` runs the Save pipeline when `IsDirty`. No confirmation prompt.

## UI changes

All field positions are preserved. The only visible additions/removals:

**`ConfigWindow.xaml`:**

- New `DockPanel` footer below the existing `TabControl`, spanning full width:
  - Left: a small `●` dot + text `Unsaved changes`, visibility bound to `IsDirty`.
  - Right: `Revert` (secondary) and `Save` (primary) buttons. Both `IsEnabled` bound to `IsDirty`.
  - Below the row: a status `TextBlock` for save success/failure messages (replaces the Settings tab's `StatusText`).
- Tab headers gain a small `●` glyph whose visibility is bound to the matching `IsDirtyChatTab / IsDirtyAudioTab / IsDirtyStatusTab / IsDirtySettingsTab` — rendered inline next to the current `SymbolIcon + TextBlock` header.

**`ChatConfigTab.xaml` / `.xaml.cs`:**

- Delete `Save Chat Settings` button.
- Delete `OnSave` handler, `OnToggleChanged` handler, `_rules`, `_currentRegion`, `_channelChecks`, and the local `_loading` flag.
- Replace with two-way bindings from controls to `PendingConfig.Chat`.
- `RulesList` binds `ItemsSource` to a CollectionView over `PendingConfig.Chat.Rules`; `+` / `✎` / `—` buttons mutate the pending list directly.
- `Pick region` updates `PendingConfig.Chat.Region`, no save side-effect.

**`AudioConfigTab.xaml` / `.xaml.cs`:**

- Delete `Save Audio Settings` button and associated `OnSave`/`OnToggleChanged` handlers.
- Bind `RulesBox.Text` and the toggle to `PendingConfig.Audio`.

**`StatusConfigTab.xaml` / `.xaml.cs`:**

- Delete `Save Status Settings` button and associated `OnSave`/`OnToggleChanged` handlers.
- Bind `IntervalBox`, `DebounceBox`, `PatternsBox`, `RegionLabel`, the toggle to `PendingConfig.Status`.

**Settings tab (the last `TabItem` in `ConfigWindow.xaml`):**

- Delete inline `Save` and `Close` buttons (the footer covers Save; the window's X covers Close).
- Keep `Test webhook`. It operates on `PendingConfig` in place — it does *not* persist or restart anything. Pure side-effectful webhook post using the currently-typed URL + player name.
- Delete the separate `StatusText` — the footer status area now hosts save/test results.

Keyboard: `Ctrl+S` invokes Save. No other shortcut changes.

## Testing

- **Unit tests** (new file `tests/GuildRelay.App.Tests/ConfigSavePipelineTests.cs`, or if that project doesn't exist, in an existing suitable one): pure logic for the diff + dispatch decision. Abstract `Registry` behind an interface so the pipeline can be tested without actually starting watchers. Cases:
  1. Enabled true→false triggers Stop only.
  2. Enabled false→true triggers Start only.
  3. Enabled remains true, `CaptureIntervalSec` changed → Stop + Start.
  4. Enabled remains true, only `Rules` changed → `ApplyConfigAsync` (no restart).
  5. Enabled remains true, nothing changed → no dispatch.
  6. Enabled remains false → no dispatch regardless of other changes.
  7. Multiple features dirty → each dispatches independently.
- **Unit tests** for `ConfigViewModel.IsDirty*` flags: toggling a field flips the flag, reverting the field clears it, per-feature isolation.
- **Regression tests** for `ChannelMatcher`, `ChatLineParser`, `RuleEditorLogic`, disconnect pattern parsing — already present, must stay green.
- **Manual smoke tests** (operator runs after implementation):
  1. Change Capture interval from 5 to 2 while Chat is enabled with a region set, click Save. OCR thumbnails in Live View refresh every ~2s. *(Regression check for the original bug.)*
  2. Edit a rule keyword, Save. Live View shows the new keyword matching on the next tick without any visible restart.
  3. Flip Chat Enabled off, Save. Watcher stops.
  4. Flip Chat Enabled on with a valid region, Save. Watcher starts.
  5. Edit Webhook URL, click Test webhook — a test post fires using the typed URL. Cancel (click Revert) — the old URL is restored and Save does nothing.
  6. Close the window mid-edit — changes silently persist and apply (per close-silent-save decision).
  7. Open two tabs' worth of pending changes (Chat rule + Status interval), click Save — both dispatch: Chat hot-applies, Status restarts.

## Risks & mitigations

- **Silent auto-save on close can persist accidental edits.** Documented trade-off, accepted in prior decision. Revert remains available until the user closes.
- **`ApplyConfig` has been dead code since inception.** Mitigation: unit-test it explicitly via the pipeline tests above; add integration-style tests that hand a real `ChatWatcher` a new rule set via `ApplyConfig` and assert the internal matcher rebuilt.
- **`ConfigStore.SaveAsync` races between concurrent saves.** Not a new risk (already present today), and now less likely because saves originate from one code path. Out of scope to fix here, but noted.
- **Two-way binding to `Region`.** The region picker workflow (dialog → returns rect) needs to push into `PendingConfig.<feature>.Region` rather than a local field. Straightforward but worth flagging in the plan.

## Acceptance

- No per-tab Save buttons remain. Settings tab's inline Save and Close buttons are removed.
- Footer `Save` and `Revert` buttons appear below the tab control, enabled only when `IsDirty`.
- Each dirty tab header shows a `●` glyph.
- Clicking Save persists once, applies per-feature via `ApplyConfig` or stop+start as classified, and clears dirty state.
- Changing Capture interval on the Settings tab and clicking Save causes the running Chat Watcher to use the new interval on the next tick (via stop+start).
- Changing only Rules and clicking Save applies live without visible restart.
- Closing the window with pending changes silently runs the save pipeline.
- All existing tests remain green; new pipeline + dirty-flag tests pass.
