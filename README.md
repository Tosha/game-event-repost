# GuildRelay

A Windows desktop companion app for **Mortal Online 2** guilds. GuildRelay watches your game session from the outside and posts notable events to a shared Discord channel, giving guildmates real-time situational awareness even when they are offline or focused elsewhere.

## What it does

GuildRelay runs quietly in your system tray and monitors three things:

- **Chat Watcher** -- Captures a region of your screen where the in-game chat window is displayed, reads it with OCR, and posts to Discord when it spots keywords you care about (e.g. "incoming", "zerg", a player name).
- **Audio Watcher** -- Listens to your system audio output and matches it against short reference clips you provide (e.g. a horse whinny, combat music sting). When a match is detected, Discord gets a notification.
- **Status Watcher** -- Monitors a screen region for disconnect dialogs. If you lose connection to the server, your guild sees it immediately. When you reconnect, they see that too.

Each feature is independently toggled on or off. A single shared Discord webhook URL (distributed by your guild leader) is all that is needed -- no bots, no server infrastructure, no accounts to create.

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
- No `FindWindow` / `EnumWindows` lookups that identify or interact with the MO2 window (window enumeration is only used for the "is MO2 running?" check)

GuildRelay captures what your monitor shows and what your speakers play. It is equivalent to having a friend watch your screen over your shoulder and type into Discord for you.

## Quick start

1. Download `GuildRelay-win-x64.zip` from the [latest release](https://github.com/Tosha/game-event-repost/releases) and extract it.
2. **First time only -- install the signing certificate** to remove the Windows SmartScreen warning:
   - Double-click `GuildRelay-CodeSigning.cer` (included in the zip).
   - Click **Install Certificate** > **Current User** > **Place all certificates in the following store** > **Browse** > select **Trusted Publishers** > **OK** > **Finish**.
   - You only need to do this once. After that, Windows will trust `GuildRelay.App.exe` without showing a warning.
3. Run `GuildRelay.App.exe`. A green icon appears in your system tray.
3. The config window opens automatically on first run. Paste your guild's Discord webhook URL and enter your player name.
4. Click **Test webhook** to verify the connection -- you should see a message appear in your Discord channel.
5. Switch to the **Chat Watcher** tab, click **Pick region**, and drag a rectangle over your in-game chat window.
6. Add at least one rule (e.g. `Incoming|(inc|incoming|enemies)|regex`).
7. Enable the feature and click **Save**.
8. Play the game. When someone types "inc north" in chat, Discord gets a notification.

## Configuration

All settings are stored locally at `%APPDATA%\GuildRelay\config.json`. The webhook URL is treated as a secret -- it never appears in log files. Logs live at `%APPDATA%\GuildRelay\logs\`.

### Chat Watcher

- **Region**: The screen rectangle to capture and OCR. Picked interactively via an overlay.
- **Capture interval**: How often to capture and OCR (default: 1000 ms).
- **OCR confidence threshold**: Lines below this confidence are silently dropped (default: 0.65).
- **Rules**: Each rule has a label, a pattern (plain text or regex), and a type. First matching rule per line wins.
- **Deduplication**: The same chat line will only be posted once per session, no matter how many times OCR re-reads it.

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
  GuildRelay.Features.Chat/     Chat Watcher feature (OCR + dedup + rules)
  GuildRelay.Features.Audio/    Audio Watcher feature (MFCC matching + cooldowns)
  GuildRelay.Features.Status/   Status Watcher feature (debounced state machine)
  GuildRelay.Publisher/         Discord webhook posting + template engine
  GuildRelay.Logging/           Serilog setup + JSONL event log + webhook URL redaction
  GuildRelay.App/               WPF tray app, config window, region picker
tests/
  6 test projects with 71 tests covering all feature logic
```

The Core project has zero Windows dependencies and is testable on any platform. All Windows API calls are isolated in `Platform.Windows` behind swap-out interfaces (`IScreenCapture`, `IOcrEngine`, `IAudioMatcher`, `IAudioSource`).

## Privacy

- **No telemetry, no analytics, no cloud backend.** GuildRelay communicates only with Discord via the webhook URL you provide.
- **No microphone access.** Audio capture is WASAPI loopback only (system output).
- **No auto-update.** You control when and whether to update.
- **Webhook URL is a secret.** It is never written to logs, never embedded in events, and never transmitted anywhere except Discord.

## License

This project is provided as-is for guild use. See the repository for license details.
