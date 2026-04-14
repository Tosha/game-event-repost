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

Each feature can be toggled on or off with a switch. A green dot on the tab header shows which features are currently active. A single shared Discord webhook URL (distributed by your guild leader) is all that is needed -- no bots, no server infrastructure, no accounts to create.

## Chat Watcher setup

Chat Watcher understands MO2's chat channel structure natively. Instead of writing regex patterns, you pick channels with checkboxes and enter keywords as a simple comma-separated list.

### Quick start

1. Switch to the **Chat Watcher** tab and flip the toggle switch to enable it.
2. Click **Pick region** and drag a rectangle over your in-game chat window. MO2 must be in **borderless or windowed** mode.
3. The **MO2 Game Events** rule template is pre-loaded by default -- it watches the GAME channel for events at 45 known locations (Sylvan Sanctum, Tindremic Heartlands, Tindrem Sewers, and more).
4. Click **Save Chat Settings**.
5. Play the game. When a game event fires at a known location, Discord gets a notification.

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

### Creating rules

Each rule consists of:

- **Channels** -- which MO2 channels to watch (checkboxes)
- **Keywords** -- what to look for in the message body (comma-separated list, or a regex pattern)
- **Match mode** -- "Contains any" (simple keyword matching) or "Regex" (for power users)
- **Cooldown** -- minimum seconds between repeated notifications for this rule (default: 600 = 10 minutes)

**Example rules:**

| Rule name | Channels | Keywords | Mode | What it catches |
|---|---|---|---|---|
| MO2 Game Events | GAME | Sylvan Sanctum, Dire Wolf, Tindremic Heartlands, ... | Contains any | Game events at known locations |
| Incoming alerts | NAVE, YELL | inc, incoming, enemies | Contains any | Player callouts about incoming threats |
| Guild relay | GUILD | *(empty = all messages)* | Contains any | Every guild chat message |
| Server notices | SERVER | *(empty = all messages)* | Contains any | All server announcements |

An empty keywords list means "match all messages on the selected channels" -- useful for relaying entire channels to Discord.

### Rule templates

Click the **Load Template** button to add pre-built rules. The **MO2 Game Events** template comes pre-loaded for new installations and watches the GAME channel for events at 45 MO2 locations.

### Testing rules

Use the **Test a message** field at the bottom of the Chat Watcher tab to verify your rules work. Paste any chat message (e.g. `[Game] A large band of Profiteers has been seen pillaging the Sylvan Sanctum!`) and click **Test**. The result shows which rule matched, or why it didn't (wrong channel, no keyword match, etc.).

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

- **Region**: The screen rectangle to capture and OCR. Picked interactively via an overlay.
- **Capture interval**: How often to capture and OCR (default: 1000 ms).
- **OCR confidence threshold**: Lines below this confidence are silently dropped (default: 0.65).
- **Rules**: Channel-aware rules with checkboxes for MO2 channels and comma-separated keywords. See "Creating rules" above.
- **Per-rule cooldown**: Each rule can only fire once per cooldown period (default: 10 minutes). Prevents OCR noise from spamming Discord.
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
  6 test projects with 100 tests covering all feature logic
```

The Core project has zero Windows dependencies and is testable on any platform. All Windows API calls are isolated in `Platform.Windows` behind swap-out interfaces (`IScreenCapture`, `IOcrEngine`, `IAudioMatcher`, `IAudioSource`).

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feat/my-feature`)
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
