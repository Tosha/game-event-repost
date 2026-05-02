# Architecture — Mortal Online 2 Guild Event Relay

**Status:** Design v1
**Date:** 2026-04-11
**Related:** [`requirements.md`](../../../requirements.md)

---

## 1. Overview

A single-process Windows desktop application, written in C# on .NET 8 with WPF, that observes a player's Mortal Online 2 session via strictly external Windows APIs and posts notable events to a shared guild Discord webhook. The v1 feature set is Chat Watcher (OCR of a user-marked chat region), Audio Watcher (WASAPI-loopback reference-clip matching), and Status Watcher (OCR of the disconnect/login dialog region, emitting `connected ↔ disconnected` transition events), with a pre-cleared extension path for a v2 Horizon Watcher (region change-detection with image attachments).

The design is shaped by three hard constraints from the requirements document:

1. **Anti-cheat safety (§3.6 of requirements):** no interaction of any kind with the MO2 process. Every component in this architecture is audited against this constraint in §13 below.
2. **Feature independence (§2.1, §3.3):** each detection feature enables/disables independently at runtime, has its own watchdog, and cannot take down the rest of the app when it fails.
3. **v2 extensibility (§2.4, §4.1):** the Discord publisher must support text + image payloads from day one so Horizon Watcher does not force a publisher rewrite.

## 2. Technology choices

| Decision | Choice | Rationale |
|---|---|---|
| Language / runtime | **C# / .NET 8**, single-file self-contained publish | Windows-only target; the Windows APIs we need (WASAPI loopback, Windows.Media.Ocr, Windows.Graphics.Capture, NotifyIcon) are all first-class on .NET. One `.exe` to distribute. |
| UI framework | **WPF** | Mature, low-friction for a tray app + config window + transparent overlay. Faster to ship than Rust + Tauri for a Windows-only guild tool. |
| OCR engine (default) | **Windows.Media.Ocr** | Free, no model deploy, ships with Windows 10+. Behind an `IOcrEngine` interface so Tesseract can replace it if MO2 fonts defeat it (the §4.3 residual risk from the requirements). |
| Audio matching | **MFCC features + sliding cosine similarity**, via [NWaves](https://github.com/ar1st0crat/NWaves) | Robust to small volume/time shifts for short stings (horse whinny, combat music). Simpler than fingerprinting, more robust than raw waveform correlation. Behind an `IAudioMatcher` interface. |
| Audio capture | **NAudio** (`WasapiLoopbackCapture`) | Idiomatic WASAPI loopback on .NET. Never microphone. |
| Screen capture (default) | **GDI BitBlt from the desktop DC** | ~1–2 ms per capture, DPI-aware, zero MO2 interaction. `Windows.Graphics.Capture` kept as a fallback `IScreenCapture` implementation for display-scaling edge cases. |
| Config format | **JSON** via `System.Text.Json` | Zero extra dependencies, users comfortable editing it. |
| App logging | **Serilog** → rolling file sink under `%APPDATA%\GuildRelay\logs\` | Structured, easy to read, well-understood. |
| Tray UI | **Hardcodet.NotifyIcon.Wpf** | Standard NotifyIcon wrapper for WPF. |
| HTTP | `HttpClient` with `SocketsHttpHandler` | Built-in, fine for low-rate webhook posts. |

All implementation choices (OCR engine, audio matcher, screen capture) are accessed through interfaces so they can be swapped without touching feature logic.

## 3. High-level component map

The app is a single .NET 8 process with a **Core Host** that owns cross-cutting services and a set of **Features** that plug into it through a shared contract. Events flow in one direction: Feature → Event Bus → Publisher + Event Log.

```
┌──────────────────────────── GuildRelay.App (single .NET 8 process) ────────────────────────────────┐
│                                                                                                    │
│  ┌──────────────┐   ┌──────────────┐   ┌──────────────┐   ┌───────────┐   ┌────────────────────┐   │
│  │ ChatWatcher  │   │ AudioWatcher │   │ StatusWatch. │   │ HorizonW. │   │    Core Host       │   │
│  │  (feature)   │   │  (feature)   │   │  (feature)   │   │ (v2,feat) │   │                    │   │
│  │              │   │              │   │              │   │           │   │ ┌────────────────┐ │   │
│  │ Capture ─┐   │   │ WASAPI ──┐   │   │ Capture ─┐   │   │           │   │ │   Event Bus    │ │   │
│  │ OCR  ────┤   │   │ Loopback ┤   │   │ OCR  ────┤   │   │           │──▶│ │ (in-proc queue)│─┼──▶│
│  │ Rules ───┤   │   │ Matcher ─┤   │   │ Patterns ┤   │   │           │   │ └──────┬─────────┘ │   │
│  │ Dedup ───┘   │   │ Cooldown ┘   │   │ State    ┤   │   │           │   │        │           │   │
│  │              │   │              │   │ machine ─┘   │   │           │   │ ┌──────▼─────────┐ │   │
│  │ IFeature     │   │ IFeature     │   │ IFeature     │   │ IFeature  │   │ │Discord         │ │   │
│  └──────▲───────┘   └──────▲───────┘   └──────▲───────┘   └─────▲─────┘   │ │Publisher       │─┼──▶│ Discord
│         │                  │                  │                  │         │ │(retry/backoff) │ │   │
│         │                  │                  │                  │         │ └──────┬─────────┘ │   │
│         │        Config / Lifecycle / Watchdog (shared)           │         │        │           │   │
│         └──────────────────┴──────────────────┴──────────────────┘          │ ┌──────▼─────────┐ │   │
│                                                                             │ │  Event Log     │─┼──▶│ %APPDATA%
│                                                                             │ │ (rolling file) │ │   │
│                                                                             │ └────────────────┘ │   │
│                                                                             │ ┌────────────────┐ │   │
│                                                                             │ │ Config Store   │ │   │
│                                                                             │ │ App Log (Seri) │ │   │
│                                                                             │ │ Feature Regis. │ │   │
│                                                                             │ └────────────────┘ │   │
│                                                                             └────────────────────┘   │
│  ┌────────────────────┐       ┌─────────────────────────────┐                                        │
│  │ Tray UI (WPF)      │◀─────▶│  Config Window (WPF)        │                                        │
│  │ NotifyIcon         │       │  Region Picker overlay      │                                        │
│  └────────────────────┘       └─────────────────────────────┘                                        │
└──────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

**Key properties:**

- Features never talk to each other or to Discord directly. They emit `DetectionEvent` objects to the Event Bus, and that is the only output boundary they have.
- The Publisher, Event Log, Config Store, and Feature Registry are the only cross-cutting singletons.
- Enabling or disabling a feature at runtime is `Registry.Start(featureId)` / `Stop(featureId)` — no process restart.
- Each Feature runs in its own background task(s) with its own watchdog (§10), so a crash in one feature never takes down the rest.

## 4. Core contracts

The Core Host exposes a small set of interfaces. Features depend only on these — never on other features' internals.

```csharp
// Every detection feature implements this.
public interface IFeature {
    string   Id            { get; }             // "chat", "audio", "horizon"
    string   DisplayName   { get; }
    FeatureStatus Status   { get; }              // Idle|Running|Warning|Error|Paused
    Task     StartAsync(CancellationToken ct);
    Task     StopAsync();
    void     ApplyConfig(JsonElement featureConfig);
    event EventHandler<StatusChangedArgs> StatusChanged;
}

// What features emit.
public sealed record DetectionEvent(
    string       FeatureId,
    string       RuleLabel,
    string       MatchedContent,     // chat line, audio rule key, etc.
    DateTimeOffset TimestampUtc,
    string       PlayerName,
    IReadOnlyDictionary<string,string> Extras,   // feature-specific metadata
    byte[]?      ImageAttachment     // null for v1 text-only features
);

// Swappable OCR backend.
public interface IOcrEngine {
    Task<OcrResult> RecognizeAsync(ReadOnlyMemory<byte> bgraPixels,
                                   int width, int height, int stride,
                                   CancellationToken ct);
}
public sealed record OcrLine(string Text, float Confidence, RectangleF Bounds);
public sealed record OcrResult(IReadOnlyList<OcrLine> Lines);

// Swappable audio matching backend.
public interface IAudioMatcher {
    void LoadReferences(IEnumerable<AudioRule> rules);
    IEnumerable<AudioMatch> Feed(ReadOnlySpan<float> monoSamples, int sampleRate);
}

// Swappable screen capture source.
public interface IScreenCapture {
    CapturedFrame CaptureRegion(Rectangle screenSpaceRect);
}

// Publisher consumes DetectionEvents from the bus.
public interface IDiscordPublisher {
    ValueTask PublishAsync(DetectionEvent evt, CancellationToken ct);
}
```

**Boundary rationale:** each feature, OCR engine, audio matcher, and capture source can be replaced or unit-tested in isolation. The `DetectionEvent.ImageAttachment` field is the single hook that lets v2 Horizon Watcher ship text+image posts without touching the publisher at all.

## 5. Chat Watcher internals

```
┌─ ChatWatcher ──────────────────────────────────────────────────────────┐
│                                                                         │
│  Timer (1s default)                                                     │
│      │                                                                  │
│      ▼                                                                  │
│  IScreenCapture.CaptureRegion(userRect)   ── DPI-aware, pure desktop   │
│      │     BGRA buffer                     BitBlt. Never touches MO2.  │
│      ▼                                                                  │
│  Preprocess  ── grayscale → contrast stretch → 2× upscale → threshold   │
│      │     (tunable pipeline; helps OCR on game fonts)                 │
│      ▼                                                                  │
│  IOcrEngine.RecognizeAsync  ── returns lines + per-line confidence      │
│      │                                                                  │
│      ▼                                                                  │
│  Drop lines with confidence < threshold                                 │
│      │                                                                  │
│      ▼                                                                  │
│  Normalize  ── trim, collapse whitespace, lowercase,                    │
│      │        strip known OCR-noise characters                          │
│      ▼                                                                  │
│  Dedup set  ── LRU of last N=256 hashes; already seen? drop.            │
│      │                                                                  │
│      ▼                                                                  │
│  Rule engine ── iterate compiled rules (plain or regex)                 │
│      │                                                                  │
│      ▼                                                                  │
│  For each match → emit DetectionEvent(FeatureId="chat", …) to bus       │
└─────────────────────────────────────────────────────────────────────────┘
```

**Design notes:**

- **Capture cadence** is a single `PeriodicTimer`, not a tight loop. One timer tick = one capture + OCR pass. If a pass takes longer than the interval, the next tick is coalesced — never queued up. CPU budget stays bounded even when OCR hiccups.
- **OCR runs on a background thread** so preprocessing for capture N+1 can overlap with OCR for capture N. Under the default 1 Hz cadence this barely matters; under 2 Hz it does.
- **Preprocessing pipeline is data-driven**, configured per-feature in JSON as an ordered list of named stages (grayscale, contrast stretch, upscale, adaptive threshold). Adding or tuning a stage is a config change, not a code change. This is the lever we pull when MO2 fonts fight the OCR engine (the §4.3 residual risk in the requirements).
- **Dedup key** is an FNV-1a hash of the normalized line, stored in a 256-entry LRU. That covers tens of minutes of normal chat pace. Cleared on feature stop/start.
- **Rules are compiled once** on config load (`Regex.Compile` for regex rules, plain `IndexOf` for literal rules) and reused each tick. Config hot-reload swaps the rule list atomically — the capture loop only ever sees a coherent snapshot.
- **Resolution/DPI drift detection:** on each capture, compare the current monitor's effective resolution and DPI to the values recorded when the region was picked. If they differ, the feature enters `Warning` status, logs a structured warning, and stops OCR (to avoid garbage matches) until the user re-picks the region. This is the recovery path for the §4.3 display-scaling risk.

### 5.1 Dual pipeline (added 2026-05-01)

Chat Watcher hosts two parallel post-dedup consumers of every parsed chat line: an **Event Repost** matcher (drives the Discord publisher) and a **Stats** matcher (drives an in-memory `IStatsAggregator`). Both share the same OCR work — region capture, preprocessing, OCR, normalization, message assembly, and dedup are performed once per tick. The two consumers are independent: a line that matches both rule lists fires both pipelines.

The Chat Watcher feature is started whenever `EventRepostEnabled || StatsEnabled` is true (replacing the old single `Enabled` flag). See [`2026-05-01-chat-stats-counters-design.md`](./2026-05-01-chat-stats-counters-design.md) for full details.

## 6. Audio Watcher internals

```
┌─ AudioWatcher ─────────────────────────────────────────────────────────┐
│                                                                         │
│  NAudio WasapiLoopbackCapture (default output device)                   │
│      │   32-bit float stereo → downmix to mono, resample to 16 kHz      │
│      ▼                                                                  │
│  Ring buffer (float[], ~4 s worth @ 16 kHz = 64k samples)              │
│      │                                                                  │
│      ▼                                                                  │
│  DSP worker (consumes fixed hop of 512 samples, ~32 ms)                │
│      │                                                                  │
│      ▼                                                                  │
│  MFCC frame extractor (NWaves) ── 13 coeffs, 25 ms frame, 10 ms hop     │
│      │                                                                  │
│      ▼                                                                  │
│  For each loaded reference rule:                                        │
│      │  sliding-window cosine similarity between                        │
│      │  reference's MFCC frame sequence and live buffer's MFCC frames   │
│      │  → per-rule score                                                │
│      ▼                                                                  │
│  if score ≥ rule.sensitivity and now - lastFire ≥ rule.cooldown         │
│      → emit DetectionEvent(FeatureId="audio", …) + update lastFire     │
└─────────────────────────────────────────────────────────────────────────┘
```

**Design notes:**

- **Loopback only.** WASAPI loopback via NAudio. Never the microphone. The config UI shows a prominent banner explaining that Discord voice, music, and browser audio all contribute to the loopback stream and can produce false matches, per §2.3 of the requirements.
- **Reference clips are pre-processed once** on load: resampled to 16 kHz mono, MFCC'd into a fixed 2D matrix (frames × 13 coeffs). The matrix is what matching runs against — not the raw waveform. Per-clip memory is ~10 KB for a 2 s clip.
- **Matching** is sliding-window cosine similarity between the reference MFCC matrix and the tail of the live MFCC stream, window length equal to the reference length. This is robust to small time shifts and volume changes, and cheap enough that matching a handful of rules against 16 kHz audio is well under 1% CPU on a typical modern box.
- **Normalization:** both reference and live MFCC frames are z-score normalized per coefficient before comparison, which is what makes the matcher robust to system volume changes and mild EQ.
- **Cooldown** is per-rule, tracked in a small dictionary keyed by rule id. Default 15 s, configurable per rule (§2.3).
- **Device loss handling:** if NAudio raises `RecordingStopped` due to default device change or unplug, the feature enters `Warning`, logs it, and tries to reacquire the default device every 5 s for up to 1 minute. Beyond that, `Error` until the user intervenes.
- **Sensitivity tuning:** the config UI exposes sensitivity as a 0.0–1.0 slider per rule and shows a live "recent max score" readout so users can tune visually. This is the mitigation for the §4.3 "audio false positives" risk.

## 7. Status Watcher internals

The Status Watcher tracks `connected ↔ disconnected` state for the local player by OCRing a user-marked region where MO2 renders its disconnect / "return to main menu" / login dialogs. It reuses the Chat Watcher's capture, preprocess, and OCR pipeline almost entirely — the only new code is a debounced state machine on top.

```
┌─ StatusWatcher ────────────────────────────────────────────────────────┐
│                                                                         │
│  Timer (3s default)                                                     │
│      │                                                                  │
│      ▼                                                                  │
│  IScreenCapture.CaptureRegion(disconnectDialogRect)                     │
│      │     BGRA buffer                                                  │
│      ▼                                                                  │
│  Preprocess  ── same pipeline type as Chat Watcher, configured          │
│      │        independently per this feature                            │
│      ▼                                                                  │
│  IOcrEngine.RecognizeAsync                                              │
│      │                                                                  │
│      ▼                                                                  │
│  Confidence below threshold OR capture error?                           │
│      │   yes ──▶ leave state unchanged (do not confirm, do not deny)   │
│      │   no                                                             │
│      ▼                                                                  │
│  Any compiled disconnect pattern matches any OCR line?                  │
│      │                                                                  │
│      ├─ yes ─▶ "disconnected" observation                               │
│      └─ no  ─▶ "connected"   observation                                │
│                                                                         │
│  Debounce buffer (last N=3 observations)                                │
│      │                                                                  │
│      ▼                                                                  │
│  State machine: Unknown → Connected → Disconnected → Connected → …     │
│      │                                                                  │
│      ▼                                                                  │
│  On Connected→Disconnected: emit DetectionEvent(                        │
│      FeatureId="status", RuleLabel="disconnected",                      │
│      MatchedContent=<phrase>, Extras["transition"]="connected->disc.")  │
│                                                                         │
│  On Disconnected→Connected: emit DetectionEvent(                        │
│      FeatureId="status", RuleLabel="reconnected",                       │
│      Extras["transition"]="disconnected->connected")                    │
└─────────────────────────────────────────────────────────────────────────┘
```

**State machine rules:**

- Three internal states: **`Unknown`** (no successful OCR since feature start), **`Connected`** (last N confirmed observations were "no disconnect pattern matched"), **`Disconnected`** (last N confirmed observations all matched some disconnect pattern).
- **First-run is silent.** Transitions out of `Unknown` never emit events, because the app cannot know what the player's actual pre-launch state was. Notifications begin with the first real `Connected ↔ Disconnected` transition after the feature has established a baseline.
- **Debounce depth `N`** is configurable (default **3**). A state transition requires `N` consecutive confirming observations. At the default 3 s capture cadence, that's a 9 s confirmation window — enough to swallow a single noisy OCR frame without adding noticeable notification lag.
- **OCR failure is not an observation.** If the capture errors out or OCR confidence is below the threshold, the observation is dropped entirely. The debounce buffer does not advance, and the state does not change. This prevents a run of bad frames from masquerading as a "connected" streak.
- **Emitted events** go to the same `EventBus` as every other feature. Both transitions share `FeatureId="status"`, differing by `RuleLabel` (`"disconnected"` or `"reconnected"`) and by the `Extras["transition"]` tag. The publisher's existing per-rule template resolution handles the two templates without any publisher changes.

**Reuse notes:**

- `IScreenCapture`, the preprocess pipeline, and `IOcrEngine` are all shared with Chat Watcher. The rule-compilation helper used by Chat Watcher for its text/regex rules is lifted into `GuildRelay.Core` so both features share a single `CompiledPattern` implementation rather than duplicating it.
- The tray "aggregate status" logic gains a small rule: `Disconnected` is a normal observed state, not an app error. The tray tooltip shows it (`Status: disconnected`), but the tray icon stays green — red is reserved for *the app itself* being broken.
- The Status Watcher has its own region and its own preprocess pipeline, independent of Chat Watcher. A user can have Chat Watcher disabled and Status Watcher enabled, or vice versa.

**Resolution/DPI drift detection:** identical behavior to the Chat Watcher (§5). A DPI or resolution change puts the feature in `Warning`, stops OCR, and waits for the user to re-pick the region. While in `Warning`, the connected/disconnected state is held at whatever it was immediately before the drift was detected — we do not flip to `Unknown`, because that would cause a spurious "reconnected" or "disconnected" event the next time OCR resumes.

## 8. Discord publisher and event log

```
Event Bus (Channel<DetectionEvent>, bounded, drop-newest on overflow)
       │
       ▼
┌─ PublisherWorker (one dedicated Task) ────────────────────────────────┐
│                                                                        │
│  await foreach evt in bus:                                             │
│      ├─ EventLog.Append(evt, PostStatus.Pending)                       │
│      ├─ template = Templates.Resolve(evt.FeatureId, evt.RuleLabel)     │
│      ├─ body     = template.Render(evt)          // placeholder fill   │
│      ├─ payload  = evt.ImageAttachment == null                         │
│      │                ? JsonPayload(body)                              │
│      │                : MultipartPayload(body, evt.ImageAttachment)    │
│      ├─ try:                                                           │
│      │     HttpClient.PostAsync(webhookUrl, payload)                   │
│      │     ├─ 2xx → EventLog.UpdateStatus(Success)                     │
│      │     ├─ 429 → honor Retry-After, re-enqueue                      │
│      │     └─ 5xx/network → exponential backoff (1,2,4,8,16 s)         │
│      │                       up to 5 attempts                          │
│      └─ exhausted retries → EventLog.UpdateStatus(Dropped);            │
│                             tray → Warning state                       │
└────────────────────────────────────────────────────────────────────────┘
```

**Design notes:**

- **One publisher worker, one `HttpClient`.** Single-threaded consumer keeps ordering predictable and rate-limiting simple. A burst of chat rules all firing at once is absorbed by the bounded channel + backoff, not by parallel POSTs.
- **The publisher never sees the webhook URL in a render path.** It pulls the URL from a `SecretStore` that returns a single-use accessor. The URL never appears in `ToString()`, never gets formatted into a template, and never lands in any log sink. Logging code that wraps `HttpRequestException` explicitly redacts `request.RequestUri` before writing anything. This satisfies the §3.4 requirement.
- **Templates** are stored in config as one default string per feature plus optional per-rule overrides. Placeholders are resolved with a simple `{name}` substitution against the event's fields plus its `Extras` dictionary. Missing placeholders render as empty string, so a typo in a template is a visual bug, not a crash.
- **Image payloads** use `multipart/form-data` with a single `file` part and a `payload_json` part for the text body. v1 never sets `ImageAttachment`, but the multipart code path is exercised from day one by the publisher's unit tests so v2 Horizon Watcher has nothing to add in the publisher itself.
- **Event log** is a rolling file at `%APPDATA%\GuildRelay\logs\events-YYYYMMDD.jsonl`, one JSON object per line. Retention is time-based (default 14 days). Each event is written twice: once as `Pending` when queued and once as a status update when the publisher finishes. On startup, any leftover `Pending` rows from a prior crash are reclassified as `Dropped (interrupted)`.
- **Tray warning state** is driven by a small "recent failures" ring buffer. If ≥3 consecutive publishes fail, the tray icon turns yellow; the next success clears it automatically.

## 9. UI: tray, config window, region picker

**Tray UI** (`Hardcodet.NotifyIcon.Wpf`)

- Single `NotifyIcon` bound to a `TrayViewModel` that subscribes to the Feature Registry's status events.
- Four aggregate states derived from all feature statuses + publisher health: **OK** (green), **Warning** (yellow), **Error** (red), **Paused** (grey). Tooltip shows a per-feature breakdown, e.g. `Chat: on (3 rules) • Audio: on (2 rules) • Status: connected • Publisher: ok`. A Status Watcher currently in `Disconnected` is shown in the tooltip but does NOT turn the tray icon yellow — disconnect is a normal observed state, not an app fault.
- Context menu per §2.7: **Open Config**, **Pause all / Resume all**, **View Logs folder**, **Quit**. "Pause all" stops capture, audio, and status workers but keeps them resident — no shutdown, no re-init on resume.

**Config window** (WPF, MVVM, one tab per feature + a `General` tab)

- **General:** webhook URL (masked textbox + "Test webhook" button that posts a `"GuildRelay connected"` message), player name, global on/off.
- **Chat Watcher:** region picker button with a thumbnail of the current capture region, capture-interval slider, OCR confidence slider, editable rule list (label + pattern + regex toggle), on-demand test panel that shows current OCR output.
- **Audio Watcher:** rule list (label + reference clip file picker + sensitivity slider + cooldown seconds), live "recent max score" readout per rule for tuning, warning banner about system audio contamination.
- **Status Watcher:** region picker button with a thumbnail of the current dialog region, capture-interval slider, OCR confidence slider, debounce-samples spinner, editable list of disconnect phrases (label + pattern + regex toggle), a live read-only indicator showing current state (`Unknown` / `Connected` / `Disconnected`) plus the last observation time. Same preprocess-pipeline editor as the Chat Watcher tab.
- **Templates:** per-feature default template with a live preview against a mock event; per-rule overrides listed under their feature. Status Watcher has two default templates, one per transition direction (`disconnected` / `reconnected`).
- **Config changes apply live** via a `ConfigStore.Apply(newConfig)` method that diff-patches running features. Rule edits are hot-reloaded; region and capture-interval changes restart just that feature's workers (§2.7).

**Region picker overlay**

- Triggered from either the Chat Watcher tab's or the Status Watcher tab's "Pick region" button. Same overlay, different target config field.
- Implementation: borderless `Window` with `WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`, sized to the full virtual screen (all monitors) via `SystemParameters.VirtualScreenWidth/Height/Left/Top`.
- Rendered with ~40% opacity black fill. User drags a rubber-band rectangle; the dragged area is punched out to full transparency so the game is visible behind it. Escape cancels, Enter confirms, click-drag-release confirms.
- Records the rectangle in **physical pixel coordinates** along with the monitor's DPI and effective resolution at time of pick. These are the drift-detection inputs that both the Chat Watcher and the Status Watcher compare against on each capture.
- **Anti-cheat audit for the picker:** the overlay is a normal WPF window. It does not find, enumerate, focus, or send messages to the MO2 window. The user must be in borderless/windowed mode to see the game behind the overlay; this is documented in the first-run experience rather than enforced by code.

## 10. Lifecycle, watchdogs, error handling

**Startup**

1. Load config from `%APPDATA%\GuildRelay\config.json`. If missing, generate defaults and open the first-run config window before continuing.
2. Initialize Core Host services: `SecretStore`, `EventBus`, `EventLog`, `AppLog` (Serilog → rolling file at `%APPDATA%\GuildRelay\logs\app-YYYYMMDD.log`), `DiscordPublisher`.
3. Build Feature Registry, register `ChatWatcher`, `AudioWatcher`, and `StatusWatcher` instances. For each feature whose config says `enabled: true`, call `StartAsync` and wire its `StatusChanged` event into the tray view model.
4. Show tray icon. Config window opens on first run only.

**Per-feature watchdog**

- Each feature's `StartAsync` spawns its worker task(s) inside a `WatchdogTask` owned by Core Host.
- The watchdog catches unhandled exceptions from the feature's task, logs them, sets feature status to `Error`, waits a backoff interval (5 s → 30 s → 2 min → stop and require manual restart), then calls the feature's own `StartAsync` again.
- After 3 restart attempts within 10 minutes the watchdog gives up, leaves the feature in `Error`, and surfaces it in the tray. This satisfies the §3.3 "feature failure restartable without process restart" requirement while still failing loud when a feature is genuinely broken.

**Shutdown**

- Tray **Quit** triggers `Host.StopAsync(graceful: true)`: Registry stops all features in parallel with a 3 s timeout, Publisher worker drains the event bus with a 2 s timeout (remaining events logged as `Dropped (shutdown)`), Serilog flushes, tray icon disposed, process exits.
- Forced shutdown (logoff, task kill) is handled by the Event Log's startup-side "leftover Pending → Dropped (interrupted)" sweep.

**Global exception handling**

- `Application.DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException` are all wired to the same handler: log the exception to the app log, flip the tray icon to red with a tooltip pointing at the log file, and — if the exception came from a known feature's task — route it through that feature's watchdog.

## 11. Config schema (JSON)

```json
{
  "schemaVersion": 1,
  "general": {
    "webhookUrl": "https://discord.com/api/webhooks/...",
    "playerName": "Tosh",
    "globalEnabled": true
  },
  "features": {
    "chat": {
      "enabled": true,
      "captureIntervalMs": 1000,
      "ocrConfidenceThreshold": 0.65,
      "region": {
        "x": 32, "y": 780, "width": 560, "height": 220,
        "capturedAtDpi": 120,
        "capturedAtResolution": { "width": 2560, "height": 1440 },
        "monitorDeviceId": "\\\\.\\DISPLAY1"
      },
      "preprocessPipeline": [
        { "stage": "grayscale" },
        { "stage": "contrastStretch", "low": 0.1, "high": 0.9 },
        { "stage": "upscale", "factor": 2 },
        { "stage": "adaptiveThreshold", "blockSize": 15 }
      ],
      "rules": [
        { "id": "incoming", "label": "Incoming callout",
          "pattern": "(inc|incoming|enemies)", "regex": true },
        { "id": "zerg",     "label": "Zerg spotted",
          "pattern": "zerg",                    "regex": false }
      ],
      "templates": {
        "default": "**{player}** saw chat match [{rule_label}]: `{matched_text}`"
      }
    },
    "audio": {
      "enabled": true,
      "rules": [
        { "id": "horseWhinny", "label": "Horse nearby",
          "clipPath": "%APPDATA%\\GuildRelay\\audio\\whinny.wav",
          "sensitivity": 0.82, "cooldownSec": 15 }
      ],
      "templates": {
        "default": "**{player}** heard [{rule_label}]",
        "perRule": { "horseWhinny": "**{player}** hears a horse nearby" }
      }
    },
    "status": {
      "enabled": true,
      "captureIntervalMs": 3000,
      "ocrConfidenceThreshold": 0.65,
      "debounceSamples": 3,
      "region": {
        "x": 960, "y": 400, "width": 720, "height": 280,
        "capturedAtDpi": 120,
        "capturedAtResolution": { "width": 2560, "height": 1440 },
        "monitorDeviceId": "\\\\.\\DISPLAY1"
      },
      "preprocessPipeline": [
        { "stage": "grayscale" },
        { "stage": "contrastStretch", "low": 0.1, "high": 0.9 },
        { "stage": "upscale", "factor": 2 },
        { "stage": "adaptiveThreshold", "blockSize": 15 }
      ],
      "disconnectPatterns": [
        { "id": "main_menu", "label": "Returned to main menu",
          "pattern": "return to main menu",             "regex": false },
        { "id": "lost_conn", "label": "Lost connection",
          "pattern": "(disconnected|lost connection)",  "regex": true }
      ],
      "templates": {
        "default": "**{player}** status: {rule_label}",
        "perRule": {
          "disconnected": ":warning: **{player}** lost connection to the server",
          "reconnected":  ":white_check_mark: **{player}** is back online"
        }
      }
    }
  },
  "logs": {
    "retentionDays": 14,
    "maxFileSizeMb": 50
  }
}
```

**Notes:**

- `schemaVersion` exists from v1 so a v2 Horizon Watcher section can migrate cleanly.
- `region` stores the DPI and resolution at pick time — the drift-detection inputs the Chat Watcher and Status Watcher each check on their own captures. The two features have independent regions; they do not share a rectangle.
- `preprocessPipeline` is an ordered list of named stages. Adding a stage is a config change, not a code change. Chat and Status each carry their own pipeline so they can be tuned independently for their respective regions.
- Status Watcher uses the same `default` + `perRule` template shape as Audio Watcher. Its rule labels happen to be the transition names (`disconnected`, `reconnected`), so the `perRule` lookup naturally selects the right template per transition direction with a sensible fallback via `default`.
- The webhook URL is stored in JSON (users need to paste it in), but the `SecretStore` wrapper is the only code path that ever returns it, and no log sink or template render path can observe it.

## 12. Project / solution layout

```
GuildRelay.sln
├── src/
│   ├── GuildRelay.Core/            // contracts, domain types, no WPF, no Windows APIs
│   │   ├── IFeature.cs
│   │   ├── DetectionEvent.cs
│   │   ├── IOcrEngine.cs / IAudioMatcher.cs / IScreenCapture.cs / IDiscordPublisher.cs
│   │   ├── EventBus.cs
│   │   ├── FeatureRegistry.cs
│   │   ├── WatchdogTask.cs
│   │   ├── SecretStore.cs
│   │   └── Config/   (ConfigStore, schema DTOs, hot-reload diffing)
│   │
│   ├── GuildRelay.Platform.Windows/ // all Windows API wrappers, isolated for testability
│   │   ├── Capture/  (BitBltCapture : IScreenCapture, GraphicsCaptureFallback)
│   │   ├── Ocr/      (WindowsMediaOcrEngine : IOcrEngine, [optional] TesseractOcrEngine)
│   │   ├── Audio/    (WasapiLoopbackSource, NWavesMfccMatcher : IAudioMatcher)
│   │   └── Dpi/      (MonitorInfo, DpiHelper)
│   │
│   ├── GuildRelay.Features.Chat/    // ChatWatcher : IFeature + preprocess pipeline
│   ├── GuildRelay.Features.Audio/   // AudioWatcher : IFeature
│   ├── GuildRelay.Features.Status/  // StatusWatcher : IFeature + debounced state machine
│   │
│   ├── GuildRelay.Publisher/        // DiscordPublisher : IDiscordPublisher, template engine
│   ├── GuildRelay.Logging/          // Serilog setup, EventLog (JSONL) writer
│   │
│   └── GuildRelay.App/              // WPF entrypoint
│       ├── App.xaml.cs              // composition root
│       ├── Tray/                    // NotifyIcon + TrayViewModel
│       ├── Config/                  // Config window, tabs, view models
│       └── RegionPicker/            // overlay window
│
└── tests/
    ├── GuildRelay.Core.Tests/        // bus, registry, watchdog, secret store
    ├── GuildRelay.Publisher.Tests/   // template rendering, retry/backoff, redaction, image path
    ├── GuildRelay.Features.Chat.Tests/   // dedup, rule engine, preprocess stages (pure)
    ├── GuildRelay.Features.Audio.Tests/  // MFCC matcher vs recorded fixtures
    ├── GuildRelay.Features.Status.Tests/ // debounce state machine, first-run silence, drift hold
    └── GuildRelay.Platform.Windows.Tests/ // manual/integration tier for capture + WASAPI
```

**Dependency rule:** `Core` depends on nothing. Features and Platform depend on `Core`. `App` depends on everything. This is what lets the publisher, features, and core logic be unit-tested without any Windows API on the test runner.

## 13. Anti-cheat compliance audit

Every component that could theoretically interact with the MO2 process, audited against §3.6 of the requirements. This table is intended to be copied verbatim into the shipped README so EAC compatibility stays auditable over time (the §4.3 recommendation).

| Component | What it does | MO2 process interaction | Compliant? |
|---|---|---|---|
| Chat Watcher capture | GDI BitBlt from desktop DC to a rectangle the user chose | None. Desktop DC, not MO2's HDC. No `FindWindow("MortalOnline")`, no HWND lookup. | ✅ |
| Audio Watcher capture | WASAPI loopback of the default output device | None. System-level audio endpoint. Identical to any music-visualizer app. | ✅ |
| Status Watcher capture + OCR | Same desktop BitBlt + OCR pipeline as Chat Watcher, applied to a different user-marked region; debounced state machine on top | None. Shares Chat Watcher's pipeline verbatim. Zero new Windows API surface. | ✅ |
| Region picker overlay | Borderless TopMost WPF window over virtual screen | None. Does not enumerate, focus, or message MO2. | ✅ |
| Discord publisher | HTTPS POST to `discord.com` | None. Outbound network only. | ✅ |
| Event log / app log | Writes to `%APPDATA%\GuildRelay\` | None. Never reads or writes inside MO2's install directory. | ✅ |
| Feature watchdog | Restarts internal tasks | None. No process handles opened on MO2. | ✅ |
| Tray UI | Standard NotifyIcon | None. | ✅ |

**Calls the app explicitly does NOT make:** `OpenProcess`, `ReadProcessMemory`, `CreateRemoteThread`, `SetWindowsHookEx`, `SendInput` / `keybd_event` / `mouse_event` targeting MO2, DLL injection, any file I/O under MO2's install directory, any packet capture. None of these are linked into the binary at all — the only Win32 P/Invokes are GDI for BitBlt, monitor/DPI helpers, and NotifyIcon plumbing.

**v2 Horizon Watcher pre-clearance:** uses the same `IScreenCapture` as Chat Watcher. No new API surface needed; no new compliance concerns.

## 14. Testing strategy

- **Core, Publisher, Features (pure):** unit tests on the test runner with no Windows API surface. Fakes for `IOcrEngine`, `IAudioMatcher`, `IScreenCapture`, and `IDiscordPublisher`. Covers dedup, rule matching, preprocess stages, template rendering, retry/backoff, secret redaction, and the multipart image path.
- **Audio matcher:** fixture-driven tests that feed pre-recorded reference clips plus negative clips through the matcher and assert score thresholds. The matcher is deterministic given a fixed input, so these are real unit tests, not manual ones.
- **Status Watcher state machine:** pure unit tests driven by a fake `IOcrEngine` that returns scripted observation sequences. Covers first-run silence (`Unknown → Connected`/`Disconnected` emits nothing), debounce depth (a transition only fires after N consecutive confirming observations), OCR-failure tolerance (dropped observations do not advance the buffer), and the drift-detection hold (state held across `Warning → Running`). No Windows APIs needed.
- **Platform.Windows:** tagged integration tests that must run on a Windows host. Capture tests hit a known bitmap rendered into a hidden window; WASAPI tests use a synthetic audio source. Not run in CI unless a Windows runner is available.
- **End-to-end smoke:** a manual checklist in the repo for "pick region → hit a test chat line → see Discord post" and "add a reference clip → play it → see Discord post". Short, recorded in the README alongside the anti-cheat audit.

## 15. Out of scope for this architecture

This document does not cover:

- **v2 Horizon Watcher internals** (baseline strategy, change-detection algorithm, per-region tuning). The only v1 commitment to v2 is the `DetectionEvent.ImageAttachment` field and the publisher's multipart code path. Horizon Watcher will get its own spec when v2 work begins.
- **Cloud features of any kind.** No telemetry, analytics, remote config, or auto-update — per §3.4 and §4.2 of the requirements.
- **Deduplication across multiple guildmates' instances.** Each instance is independent. If two players see the same event, they may both post; cross-user coordination is explicitly out of scope per §4.2.
- **Installer packaging.** MSI vs. portable zip is a delivery concern, not an architectural one. Both are viable with `dotnet publish --self-contained`.

## 16. Open items to resolve during implementation

These are small enough not to block design approval, but should be decided before or during implementation:

- **Windows.Media.Ocr vs Tesseract in practice.** The interface supports either; the architecture phase assumes Windows.Media.Ocr is sufficient. The first implementation milestone should include a short characterization pass against real MO2 chat screenshots to confirm.
- **Exact default preprocess pipeline parameters.** The pipeline shape is fixed; specific contrast-stretch low/high values and adaptive-threshold block size should be tuned against real captures before shipping.
- **Default sensitivity for shipped reference clips.** No reference clips are shipped in v1 (users bring their own), but the quickstart docs should recommend a starting sensitivity based on characterization.
- **Region-picker tray shortcut.** §4.3 suggests a tray-menu shortcut in addition to the config button. Not decided here — easy to add later without architectural impact.
- **Default disconnect phrases shipped with the app.** The architecture commits to shipping sensible defaults (`"return to main menu"`, `"disconnected"`, `"lost connection"`), but the exact wording is a UX/localization call that should be confirmed against real MO2 dialog screenshots during the first implementation milestone.
