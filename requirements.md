# Requirements — Mortal Online 2 Guild Event Relay

**Status:** Draft v1 (requirements only — architecture and implementation to follow)
**Date:** 2026-04-10

---

## 1. Purpose, users, and scope

### 1.1 Purpose

A Windows desktop application that observes what is happening in a player's Mortal Online 2 (MO2) session and posts notable events to a shared guild Discord channel, so guildmates who are offline or focused elsewhere get real-time situational awareness — chat mentions, audio cues, and eventually visual horizon alerts from scouts.

### 1.2 Primary users

Members of a single Mortal Online 2 guild. Each guildmate installs and runs their own copy on their own Windows PC. All instances post into **one shared guild Discord channel** via a single Discord webhook URL distributed by the guild leader. Users are assumed to be PC-literate but not developers — the app must be usable without editing code.

### 1.3 Version scope

- **v1 (this document):** Chat keyword detection (via OCR on the in-game chat window) and audio reference-clip detection (via system audio loopback), posting to one Discord channel. Each detection source is an independently enable-able feature in the app's config, so a user can run with chat only, audio only, or both.
- **v2 (sketched, not fully specified):** Horizon Watcher — change-detection on a user-marked screen region, for scout scenarios. See §4.1.
- **Explicitly out of scope:** see §4.2.

### 1.4 Non-negotiable constraint — anti-cheat safety

The app must only use **external observation**: standard user-level Windows screen-capture and audio-loopback APIs. It must **never** read from, inject into, or otherwise interact with the Mortal Online 2 process. This is a hard requirement to avoid any conflict with EasyAntiCheat. See §3.6 for the full policy.

---

## 2. Functional requirements (v1)

### 2.1 Feature toggles and configuration model

- The app ships with a set of **detection features**. In v1 these are **Chat Watcher** and **Audio Watcher**. v2 will add **Horizon Watcher**.
- Each feature is independently enabled or disabled via the config window. A disabled feature consumes no CPU, GPU, screen-capture, or audio-capture resources.
- Each feature has its own configuration sub-section (rules, regions, reference clips, templates, etc.) and its own runtime status indicator in the tray UI — e.g., "Chat: on (3 rules)", "Audio: off".
- A single top-level config covers shared settings: Discord webhook URL, local player/character name (used to tag posts), and a global on/off.

### 2.2 Chat Watcher

- The user marks a screen region corresponding to the in-game chat window. Selection is done through an interactive region picker in the config UI. Coordinates are stored as pixel coordinates tied to the current display resolution. A "re-pick region" button in the config window is the supported way to recover from resolution or DPI changes.
- While enabled, the Chat Watcher periodically captures that region and runs OCR on it. Capture interval is configurable; see §3.2 for the default.
- The user defines a list of **chat rules**, each consisting of a **plain-text or regular-expression pattern** plus a human-readable label.
- When an OCR'd chat line matches a rule, the Chat Watcher emits a **Chat Event** containing: the matched text, the matching rule's label, a timestamp, and the local player name.
- **Local deduplication is mandatory.** OCR will re-read the same chat lines on many successive captures. The Chat Watcher must recognize previously-seen lines within the current session and not re-emit events for them.
- OCR confidence below a user-configurable threshold is discarded silently, to reduce garbage matches and keep Discord quiet.

### 2.3 Audio Watcher

- The user adds one or more **audio rules**, each consisting of: a reference audio clip (a short `.wav` file the user provides, typically recorded from the game — e.g., a horse whinny, a combat-music sting), a human-readable label, and a match-sensitivity setting.
- While enabled, the Audio Watcher captures the **default output device via WASAPI loopback** (i.e., what the game is playing; **not** the user's microphone) and continuously compares the incoming audio stream to every enabled reference clip.
- When a match is detected with sufficient confidence, the Audio Watcher emits an **Audio Event** containing: the matching rule's label, a timestamp, the local player name, and the detected-at offset.
- **Per-rule cooldown is mandatory**, to avoid a single sound event firing several posts in rapid succession. Default cooldown is 15 seconds per rule, configurable per rule.
- The config UI must warn the user that other audio playing on the system — music, Discord voice, browser tabs — can interfere with detection.

### 2.4 Discord publishing

- All events from all enabled features go through a single **Discord publisher** that posts to the one configured Discord webhook URL.
- **Post format is configurable per feature** via user-editable templates (with optional per-rule overrides). Templates are strings with placeholders such as `{player}`, `{rule_label}`, `{matched_text}`, `{timestamp}`, `{feature}`. Sensible defaults are shipped for every feature so that a first-time user gets a working, readable feed without configuring anything.
- **Post payloads support both plain text and text + image attachment.** v1 features are text-only; v2 Horizon Watcher will use text + image. This requirement is captured in v1 so that v2 can be added without architectural rework of the publisher.
- Webhook URL is stored only in the local config file. It is never transmitted anywhere except Discord and is treated as a secret (see §3.4).
- Failed webhook calls are retried with exponential backoff for a short bounded period, then dropped and recorded in the local event log. Persistent webhook failures surface as a tray-icon warning state.

### 2.5 Local event log

- All detected events — whether successfully posted to Discord or not — are written to a **rolling local log file** (bounded by retention days and/or size).
- Each log entry records: timestamp, feature (chat/audio), rule label, matched content (chat line or audio rule), and Discord post status (success, failed, retried, dropped).
- The log is intended for after-session review and debugging. It lives under the app's per-user data directory; see §3.5.

### 2.6 App lifecycle and tray UI

- The app is started **manually** by the user. There is no Windows autostart and no automatic launch when MO2 is detected in v1.
- Once started, the app minimizes to the **Windows system tray**. The tray icon reflects overall status (OK / warning / error / paused).
- The tray context menu provides at minimum: **Open Config**, **Pause/Resume all features**, **View Logs folder**, **Quit**.
- Configuration changes saved in the config window take effect without requiring an app restart, to the extent practical.

---

## 3. Non-functional requirements

### 3.1 Platform

- Windows 10 and Windows 11, 64-bit, on reasonably recent builds.
- Must coexist with MO2 running in the foreground on a typical gaming rig.
- No cross-platform support. Linux, macOS, Proton/Wine are all out of scope.

### 3.2 Performance and resource budget

- **The app must not meaningfully impact game FPS.** Target steady-state CPU usage is under roughly 5% on a typical modern CPU with both Chat Watcher and Audio Watcher enabled. GPU usage should be negligible.
- Chat Watcher capture interval is configurable. The default is chosen to balance responsiveness and cost — approximately one capture per second.
- Audio Watcher processes incoming audio in streaming mode, not batch. End-to-end latency from sound occurring to Discord post should average under ~3 seconds.
- Idle memory footprint should stay under ~300 MB with default settings.

### 3.3 Reliability

- The app must tolerate: MO2 not being running, MO2 being Alt-Tabbed or minimized, display resolution or DPI changes, temporary network loss (resolved via webhook retry/backoff), and temporary loss of the default audio device.
- No unhandled exception should cause the app to die silently. Errors must surface via the tray icon state and in the local log.
- Each feature has its own internal watchdog, so a single feature failure can be restarted by the core without restarting the whole process.

### 3.4 Security and privacy

- Webhook URL, reference audio clips, templates, and all other configuration live only on the user's machine. **No telemetry, no remote config, no cloud backend, no analytics.**
- The webhook URL is treated as a secret: it is never written to logs and never embedded in event content. Log entries that reference HTTP errors redact the URL.
- The app does not capture or process the user's microphone. Audio capture is loopback only.
- No auto-update mechanism in v1. Updates are delivered by the user manually reinstalling.

### 3.5 Installability and distribution

- Distributed as a standard Windows installer (e.g., MSI) or a portable zip — either is acceptable. Guildmates should be able to go from "download" to "running with a first event posted" in under 5 minutes.
- First-run experience opens the config window, prompts for webhook URL and player name, and offers quickstart templates for a first chat rule and a first audio rule.
- Uninstall (or delete-the-folder, for portable) removes the app cleanly. Per-user config and logs live under `%APPDATA%\<AppName>\` and can optionally be preserved on uninstall.

### 3.6 Anti-cheat compatibility policy

The app must coexist safely with EasyAntiCheat by never interacting with the MO2 process. Concretely:

- **No memory reads** from the MO2 process.
- **No DLL injection** or any in-process hooking.
- **No process handles** beyond what a standard "is MO2 running?" enumeration needs.
- **No keyboard or mouse injection** into the MO2 window.
- **No file-system reads or writes** inside MO2's install directory.
- Observation is strictly via user-level Windows APIs: DXGI/GDI/Win32 screen capture and WASAPI loopback audio capture.

### 3.7 Observability

- Beyond the per-event local log (§2.5), the app maintains a structured application log covering startup, shutdown, feature enable/disable, configuration reloads, errors, and webhook failures. This log is the primary source of useful bug reports.
- The tray menu provides a **View Logs folder** option that opens Explorer on the logs directory.

---

## 4. v2 preview, out-of-scope, risks, and open questions

### 4.1 v2 preview — Horizon Watcher

The Horizon Watcher is planned as a separate toggleable feature alongside Chat Watcher and Audio Watcher. Its full requirements will be gathered as a separate exercise when v2 work begins. The v1 requirements only ensure that nothing blocks a future v2 implementation (notably: the Discord publisher supports image attachments).

Known shape of the feature:

- The user marks a fixed screen region to watch — e.g., a dungeon entrance, a road, or a horizon.
- The app establishes a baseline image of the region while it is static (no one in view).
- When significant change is detected in the region — new moving objects entering the region, not just lighting/noise — the app emits a **Horizon Event** containing a captured screenshot of the region (or the full game window), plus timestamp and player name.
- The event is posted via the same Discord publisher as v1 features, using a configurable template and a text + image payload. Default message is along the lines of "movement detected".
- Horizon Watcher uses its own per-rule cooldown, expected to default higher than audio (e.g., 60 s), to avoid spamming during active engagements.

Sensitivity tuning, baseline-refresh strategy, per-region rules, and related details are intentionally deferred to v2 requirements gathering.

### 4.2 Out of scope (v1 and v2)

- Any form of interaction with the MO2 process (memory reads, injection, packet capture, file-system reads inside the install directory).
- Cross-user event deduplication or any shared coordination backend.
- Voice transcription or speech-to-text on game or voice-chat audio.
- Parsing killfeeds, inventories, market data, or crafting outcomes.
- Automatically performing any in-game action (keyboard/mouse injection) in response to an event.
- Telemetry, analytics, or crash reporting to any remote service.
- Linux, macOS, or MO2 running under Proton/Wine.
- Mobile companion app, web dashboard, or any non-Windows-desktop component.
- Auto-update mechanism.

### 4.3 Risks and known hard problems

- **OCR reliability.** MO2 chat fonts, transparency, and game UI scaling may produce noisy OCR output. Mitigations include a user-tunable confidence threshold and capture-region preprocessing (contrast boost, scaling). Residual risk: some MO2 fonts may be hostile to Tesseract-class OCR engines, and will need to be characterized during the architecture phase.
- **Audio matching false positives.** System audio can include music, Discord voice, browser tabs, and other sounds that partially resemble reference clips. Per-rule sensitivity and cooldowns mitigate but cannot eliminate this. Users should expect to tune their rules.
- **Display resolution and UI scaling changes.** Chat region pixel coordinates become invalid if the user changes resolution or DPI. The "re-pick region" button in the config window (§2.2) is the supported recovery path; the architecture phase should consider whether a tray-menu shortcut is also worth adding.
- **EasyAntiCheat behavior changes over time.** The app uses only externally-observable APIs that should be uncontroversial, but EAC behavior is opaque. Staying compatible across MO2 updates is an ongoing user responsibility; the architecture doc should plainly list the observation techniques used so EAC compatibility remains auditable.
- **Webhook URL leak via guildmate carelessness.** The shared webhook URL is only as safe as the least-careful guildmate. This is a social risk, not a technical one, but the user-facing docs should call it out.

### 4.4 Open questions (to be resolved in the architecture phase)

- Which OCR engine to use — Tesseract (via a .NET or Rust binding), Windows.Media.Ocr, or another cloud-free engine.
- Which audio-matching approach — FFT cross-correlation on fixed-length windows, MFCC distance, constellation-map fingerprinting, or another technique.
- Language and UI framework for the Windows app. Candidates worth comparing later: C# + WinUI/WPF, Rust + Tauri, and others.
- Config file format: JSON, YAML, or TOML.
- Exact default capture interval and OCR preprocessing pipeline.
- How the "re-pick region" interactive picker overlays the game without violating the anti-cheat policy (e.g., a transparent click-through overlay created while the user is in windowed or borderless mode).

---

## 5. Glossary

- **Chat Watcher** — v1 feature that captures a screen region, OCRs it, and fires events when recognized text matches a user-defined pattern.
- **Audio Watcher** — v1 feature that captures system audio via WASAPI loopback and fires events when the incoming sound matches a user-provided reference clip.
- **Horizon Watcher** — v2 feature that watches a fixed screen region for visual change and posts a screenshot to Discord when movement is detected.
- **Rule** — a single configurable detection trigger inside a feature (a regex/text pattern for Chat Watcher, a reference clip for Audio Watcher, a watched region for Horizon Watcher). Each rule has a user-facing label.
- **Event** — the internal message a feature emits when a rule fires. Events carry enough metadata (feature, rule label, matched content, timestamp, player name) for the Discord publisher to render a post via a template.
- **Discord publisher** — the single component responsible for taking events from any feature and posting them to the configured Discord webhook using the feature-specific template. Supports both text and text + image payloads.
- **Webhook URL** — the shared Discord webhook URL distributed by the guild leader; treated as a secret by the app.
- **EAC** — EasyAntiCheat, the anti-cheat system shipped with Mortal Online 2.
