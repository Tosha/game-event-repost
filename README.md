# GuildRelay

[![CI](https://github.com/Tosha/game-event-repost/actions/workflows/ci.yml/badge.svg)](https://github.com/Tosha/game-event-repost/actions/workflows/ci.yml)
[![Release](https://github.com/Tosha/game-event-repost/actions/workflows/release.yml/badge.svg)](https://github.com/Tosha/game-event-repost/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A Windows desktop companion app for **Mortal Online 2** guilds. GuildRelay watches your game session from the outside and posts notable events to a shared Discord channel, giving guildmates real-time situational awareness even when they are offline or focused elsewhere.

## Download

**[Download the latest release](https://github.com/Tosha/game-event-repost/releases/latest)** -- a single `.exe`, no installer required.

1. Download `GuildRelay-win-x64.zip` from the link above and extract it.
2. Run `GuildRelay.App.exe`.
   - Windows may show a SmartScreen warning for the first launch. Click **"More info"** then **"Run anyway"**. This only happens once.
3. The config window opens automatically. Paste your guild's Discord webhook URL and enter your player name.
4. Click **Test webhook** to verify -- a message should appear in your Discord channel.

## What it does

GuildRelay runs quietly in your system tray and monitors three things:

- **Chat Watcher** -- Captures a region of your screen where the in-game chat window is displayed, reads it with OCR, and posts to Discord when it spots keywords on specific chat channels.
- **Audio Watcher** -- Listens to your system audio output and matches it against short reference clips you provide (e.g. a horse whinny, combat music sting). When a match is detected, Discord gets a notification.
- **Status Watcher** -- Monitors a screen region for disconnect dialogs. If you lose connection to the server, your guild sees it immediately. When you reconnect, they see that too.

Each feature can be toggled on or off with a switch. A green dot on a tab header means that feature is running; an orange dot means the tab has unsaved changes. A single shared Discord webhook URL (distributed by your guild leader) is all that is needed -- no bots, no server infrastructure, no accounts to create.

## Using the Chat Watcher

Chat Watcher captures a region of your screen, runs OCR on it, and posts to Discord when a recognized line matches one of your rules. It understands MO2's chat channel structure natively, so you pick channels with checkboxes and enter keywords as a simple comma-separated list -- no regex required for the common cases.

### First-time setup

1. Open the config window (right-click the system tray icon → **Open Config**, or it auto-opens on first launch).
2. On the **Settings** tab, paste your guild's Discord webhook URL, enter your in-game character name, and click **Test webhook** to make sure your channel receives the test message.
3. Switch to the **Chat Watcher** tab and flip the toggle switch to enable the feature.

> 📷 _Screenshot: Chat Watcher tab -- enable toggle, Pick region button, rule list with +/✎/− action buttons, Live View button, and the test-message field at the bottom._

4. Click **Pick region** and drag a rectangle over your in-game chat window. MO2 must be running in **borderless** or **windowed** mode -- exclusive fullscreen hides the window from desktop capture.

> 📷 _Screenshot: region picker overlay covering the screen while the user drags a selection rectangle around the chat window._

5. The **MO2 Game Events** rule template is pre-loaded by default -- it watches the GAME channel for events at 45 known locations (Sylvan Sanctum, Tindremic Heartlands, Tindrem Sewers, and more). For most users this is enough to start with.
6. Click **Save** in the sticky footer at the bottom of the config window. The Chat Watcher starts immediately, the orange "unsaved changes" dot disappears, and a green "running" dot appears on the tab header.
7. Play the game. When chat matches a rule, your guild's Discord channel gets the notification.

> Toggling the switch only dirties the configuration -- nothing actually starts or stops until you click **Save**. The same applies when you toggle a feature off.

### Live View

The **Live View** button on the Chat Watcher tab opens a debug window that lets you see exactly what GuildRelay is doing every capture interval:

- **Captured region (raw)** -- the exact pixels just grabbed from your chat region, with nearest-neighbor scaling so OCR-relevant detail isn't blurred away.
- **OCR output → parsed channel → normalized** -- every line OCR produced, tagged as a channel header (e.g. `[GAME]`, `[GUILD]`), a continuation of the previous line (`↳`), or skipped, and shown alongside its normalized form (the text actually used for matching).
- **Match results** -- which rules matched on this tick, or "No matches this tick".

> 📷 _Screenshot: Live View window showing the captured chat region thumbnail, the OCR / channel / normalized output, and a recent match result._

Use Live View whenever you're tuning the captured region or trying to figure out why a rule isn't firing. You can see at a glance whether OCR is reading clean text, whether channel tags are being parsed correctly, and whether your keywords are matching what's on screen. Close the window when you're done -- it stops receiving updates as soon as it closes and adds no overhead while shut.

### MO2 chat channels

GuildRelay recognizes all MO2 chat channels:

| Channel | Typical content |
|---|---|
| **GAME** | System events -- dungeon spawns, tasks, player online/offline |
| **SERVER** | Server announcements and restarts |
| **GUILD** | Guild-only chat |
| **NAVE** | Global player chat |
| **SAY** / **YELL** | Local and ranged player chat |
| **TRADE** | Trade chat |
| **COMBAT** | Combat log entries |
| **SKILL** | Skill-up notifications |
| **WHISPER** | Private messages |
| **HELP** | Help requests |

### Adding and editing rules

Manage rules from the buttons next to the **Active rules** list on the Chat Watcher tab:

- **+** -- opens the rule editor in *add* mode.
- **✎** -- opens the rule editor in *edit* mode for the selected rule.
- **—** -- removes the selected rule (no undo, but you can still **Revert** in the footer until you Save).

Double-clicking a rule in the list also opens the editor.

> 📷 _Screenshot: rule editor dialog showing the Label field, Channels checkbox panel, Keywords box, match-mode radios, and the "Pause between Discord notifications" field._

Each rule has:

- **Label** -- shows up in Discord notifications and in the rule list. Make it descriptive ("Sylvan Sanctum events", "Guild chat relay").
- **Channels** -- check the MO2 channels this rule should watch. Leave every box unchecked to match every channel (a hint underneath confirms this).
- **Keywords** -- comma-separated list for "Contains any keyword" mode, or a single pattern for "Regex" mode. Leave empty to match every message on the selected channels (useful for relaying an entire channel).
- **Match mode** -- **Contains any keyword** (default, case-insensitive substring match against any item in the list) or **Regex** (for power users who need lookarounds, alternation, etc.).
- **Pause between Discord notifications (seconds)** -- after this rule fires once, it stays silent for this many seconds before it can fire again. Defaults to the value you set on the Settings tab; the editor pre-fills that default but you can override it per rule. Prevents OCR noise (or a chat message that lingers on screen) from spamming the channel.

Click **Save** in the rule editor to commit the rule into the pending config, then click **Save** in the config window's sticky footer to persist everything to disk and reapply it to the running Chat Watcher.

**Example rules:**

| Rule name | Channels | Keywords | Mode | What it catches |
|---|---|---|---|---|
| MO2 Game Events | GAME | Sylvan Sanctum, Tindremic Heartlands, … | Contains any | Game events at known locations |
| Guild relay | GUILD | *(empty = all messages)* | Contains any | Every guild chat message |
| Server notices | SERVER | *(empty = all messages)* | Contains any | All server announcements |

### Rule templates

Click **Load Template** to add a pre-built bundle of rules from the dropdown. The **MO2 Game Events** template comes pre-loaded for new installations. Load Template is additive and idempotent -- it only adds rules whose IDs aren't already present, so loading the same template twice is safe.

### Testing rules without playing

Use the **Test a message against your rules** field at the bottom of the Chat Watcher tab to dry-run a chat line. Paste any message (e.g. `[Game] A large band of Profiteers has been seen pillaging the Sylvan Sanctum!`) and click **Test**. The result tells you either which rule matched, or -- if none did -- which channel was parsed out and what the message body looked like after normalization. This is the fastest way to see whether your keywords spell something the parser actually sees.

## Anti-cheat safety

**GuildRelay does not read, modify, or interact with the Mortal Online 2 process in any way.**

Mortal Online 2 ships with EasyAntiCheat. GuildRelay is designed from the ground up to be fully compatible by using only standard, external Windows APIs that any normal desktop application would use:

| What GuildRelay does | How it works | Touches MO2? |
|---|---|---|
| Screen capture | GDI BitBlt from the Windows desktop -- the same API screenshot tools use | No |
| OCR | Windows.Media.Ocr (built into Windows 10/11) reading pixel buffers | No |
| Audio capture | WASAPI loopback of the default audio output device -- the same API music visualizers use | No |
| Audio matching | NWaves MFCC feature extraction + cosine similarity, running on captured system audio | No |
| Discord posting | Standard HTTPS POST to a Discord webhook URL | No |
| DPI/resolution detection | Standard Win32 monitor enumeration for drift detection | No |

**What GuildRelay explicitly does NOT do:**

- No `OpenProcess`, `ReadProcessMemory`, `WriteProcessMemory`, or any process-memory API targeting MO2
- No `SetWindowsHookEx`, `SendInput`, `keybd_event`, `mouse_event`, or any input injection
- No DLL injection, IAT hooking, or any in-process instrumentation
- No reading or writing files inside the MO2 installation directory
- No packet capture or network inspection of MO2 traffic
- No `FindWindow` / `EnumWindows` lookups that identify or interact with the MO2 window

GuildRelay captures what your monitor shows and what your speakers play. It is equivalent to having a friend watch your screen over your shoulder and type into Discord for you.

## Configuration

All settings are stored locally at `%APPDATA%\GuildRelay\config.json`. The webhook URL is treated as a secret -- it never appears in log files. Logs live at `%APPDATA%\GuildRelay\logs\`.

### Chat Watcher

- **Region**: The screen rectangle to capture and OCR. Picked interactively via an overlay (Chat Watcher tab → **Pick region**).
- **Capture interval**: How often to capture and OCR, in seconds (default: 5). Set on the Settings tab. Lower values detect events sooner but use more CPU.
- **OCR confidence threshold**: Lines below this OCR confidence are silently dropped (default: 0.65). Set on the Settings tab.
- **Rules**: Channel-aware rules with checkboxes for MO2 channels and comma-separated keywords. See "Adding and editing rules" above.
- **Pause between Discord notifications** (per rule): After a rule fires, it stays silent this many seconds before it can fire again (default: 600 = 10 minutes). The Settings tab carries the default; each rule can override it in the rule editor. Prevents OCR noise or a lingering chat message from spamming Discord.
- **Line joining**: OCR sometimes splits a long chat message across two lines. GuildRelay automatically joins adjacent lines when matching, so keywords like "Dire Wolf" work even if OCR splits them across lines.

### Audio Watcher

- **Rules**: Each rule has a label, a path to a reference `.wav` clip, a sensitivity threshold (0.0-1.0), and a cooldown in seconds.
- **Sensitivity**: Lower values match more loosely; higher values require a closer match. Start around 0.80 and adjust.
- **Cooldown**: Minimum seconds between repeated notifications for the same rule (default: 15).
- **System audio warning**: Discord voice, music, and browser audio all feed into the loopback stream and can cause false matches.

### Status Watcher

- **Region**: The screen area where MO2 shows its disconnect / login dialog.
- **Disconnect phrases**: Patterns to look for (defaults: "return to main menu", "disconnected", "lost connection").
- **Debounce**: Number of consecutive confirming captures before a state transition fires (default: 3). At 3-second intervals, that is a 9-second confirmation window.
- **Notifications**: Posts once on disconnect, once on reconnect. No spam during extended outages.

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows 10 or later.

```
dotnet restore
dotnet build --configuration Release
dotnet test
dotnet publish src/GuildRelay.App -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

The published binary will be at `src/GuildRelay.App/bin/Release/net8.0-windows10.0.22621.0/win-x64/publish/GuildRelay.App.exe`.

## Project structure

```
GuildRelay.sln
src/
  GuildRelay.Core/              Contracts, config, events, rules -- no Windows APIs
  GuildRelay.Platform.Windows/  BitBlt capture, Windows.Media.Ocr, WASAPI, NWaves MFCC, DPI
  GuildRelay.Features.Chat/     Chat Watcher (channel parser, keyword matcher, OCR, dedup)
  GuildRelay.Features.Audio/    Audio Watcher (MFCC matching + cooldowns)
  GuildRelay.Features.Status/   Status Watcher (debounced state machine)
  GuildRelay.Publisher/         Discord webhook posting + template engine
  GuildRelay.Logging/           Serilog setup + JSONL event log + webhook URL redaction
  GuildRelay.App/               WPF tray app (Fluent dark theme), config window, region picker
tests/
  7 test projects with 165 tests covering all feature logic
```

The Core project has zero Windows dependencies and is testable on any platform. All Windows API calls are isolated in `Platform.Windows` behind swap-out interfaces (`IScreenCapture`, `IOcrEngine`, `IAudioMatcher`, `IAudioSource`).

## Contributing

1. Fork the repository
2. Create a branch using the project's prefix convention: `feature/<name>` for new features, `fix/<name>` for bug fixes, or `chore/<name>` for chores and docs (e.g. `git checkout -b feature/my-feature`)
3. Make your changes with tests
4. Push and open a Pull Request
5. CI must pass before merge

## Privacy

- **No telemetry, no analytics, no cloud backend.** GuildRelay communicates only with Discord via the webhook URL you provide.
- **No microphone access.** Audio capture is WASAPI loopback only (system output).
- **No auto-update.** You control when and whether to update.
- **Webhook URL is a secret.** It is never written to logs, never embedded in events, and never transmitted anywhere except Discord.

## License

This project is licensed under the [MIT License](LICENSE).
