# CLAUDE.md — Project instructions for Claude Code

This file tells Claude Code how to work in this repository. Read it at the start of any session.

## What this project is

**GuildRelay** — a Windows-only desktop app (C# / .NET 8 / WPF) that observes a player's Mortal Online 2 session via external Windows APIs and posts notable events to a shared guild Discord webhook. Detection features in v1: **Chat Watcher**, **Audio Watcher**, **Status Watcher**. Planned for v2: **Horizon Watcher**.

Source of truth for what the app must do: [`requirements.md`](./requirements.md).
Source of truth for how the app is built: [`docs/superpowers/specs/2026-04-11-guild-event-relay-architecture.md`](./docs/superpowers/specs/2026-04-11-guild-event-relay-architecture.md).

**Status (as of this commit):** design phase complete, implementation not started. No `src/` or `tests/` yet.

## Non-negotiable constraint — anti-cheat safety

Mortal Online 2 ships with EasyAntiCheat. **The app must never interact with the MO2 process in any way.** See requirements §3.6 and architecture §13. Concretely:

- **No** `OpenProcess`, `ReadProcessMemory`, `WriteProcessMemory`, `CreateRemoteThread`, or any other process-memory API targeting MO2.
- **No** `SetWindowsHookEx`, `SendInput`, `keybd_event`, `mouse_event`, or any input injection targeting the MO2 window.
- **No** DLL injection, IAT hooking, or any in-process instrumentation.
- **No** `FindWindow("MortalOnline…")` / `EnumWindows` lookups that identify and interact with the MO2 HWND. Window enumeration is permitted only for the "is MO2 running?" check.
- **No** file-system reads or writes inside MO2's install directory.
- **No** packet capture or network inspection of MO2 traffic.

Observation is strictly via:

- **GDI BitBlt from the desktop DC** (for screen capture of user-marked regions).
- **WASAPI loopback** via NAudio (for system audio, never the microphone).
- **Standard Win32 DPI / monitor enumeration** for drift detection.
- **Outbound HTTPS** to `discord.com` for webhook posts.

If a task could be implemented by reading MO2 memory or hooking its process, **do not implement it that way**. Propose an external-observation alternative or flag the task as out of scope.

## Tech stack (locked in by architecture §2)

| Layer | Choice |
|---|---|
| Runtime | .NET 8, single-file self-contained publish |
| UI | WPF + MVVM |
| Tray | `Hardcodet.NotifyIcon.Wpf` |
| Screen capture | GDI BitBlt from desktop DC (default); `Windows.Graphics.Capture` as fallback |
| OCR | `Windows.Media.Ocr` behind `IOcrEngine`; Tesseract reserved as swap-in |
| Audio capture | NAudio `WasapiLoopbackCapture` |
| Audio matching | NWaves MFCC features + sliding cosine similarity |
| Logging | Serilog rolling file sink |
| Config format | JSON via `System.Text.Json` |
| HTTP | `HttpClient` with `SocketsHttpHandler` |

**Do not introduce new top-level dependencies without updating the architecture doc first.**

## Project structure (target)

```
GuildRelay.sln
├── src/
│   ├── GuildRelay.Core/            // contracts, domain types, no WPF, no Windows APIs
│   ├── GuildRelay.Platform.Windows/ // all Windows API wrappers (capture, OCR, audio, DPI)
│   ├── GuildRelay.Features.Chat/   // ChatWatcher : IFeature
│   ├── GuildRelay.Features.Audio/  // AudioWatcher : IFeature
│   ├── GuildRelay.Features.Status/ // StatusWatcher : IFeature
│   ├── GuildRelay.Publisher/       // DiscordPublisher + template engine
│   ├── GuildRelay.Logging/         // Serilog setup + EventLog JSONL writer
│   └── GuildRelay.App/             // WPF entrypoint, tray, config window, region picker
└── tests/
    ├── GuildRelay.Core.Tests/
    ├── GuildRelay.Publisher.Tests/
    ├── GuildRelay.Features.Chat.Tests/
    ├── GuildRelay.Features.Audio.Tests/
    ├── GuildRelay.Features.Status.Tests/
    └── GuildRelay.Platform.Windows.Tests/
```

**Dependency rule (enforce this):** `Core` depends on nothing. Features and Platform depend on `Core`. `App` depends on everything. `Core` must remain testable on any OS — no `Windows.*` namespaces, no P/Invoke, no NAudio.

## Branching

All work must be committed on a dedicated branch whose prefix matches the kind of work:

- `feature/<name>` — new features or feature changes
- `fix/<name>` — bug fixes
- `chore/<name>` — chores, docs, maintenance

Before the first commit of any task, verify the current branch matches the work. If it doesn't, create a new branch with the correct prefix from `main`. Do **not** piggyback unrelated work on an existing branch just because it happens to be checked out.

## Coding conventions

- **Test-driven development.** Use the `superpowers:test-driven-development` skill when implementing any feature or bugfix. Write a failing test, make it pass, refactor. The exception is UI code in `GuildRelay.App` where pure unit tests add little value — for those, favor extracting logic into testable view models and testing those instead.
- **Swap-out interfaces.** `IOcrEngine`, `IAudioMatcher`, `IScreenCapture`, `IDiscordPublisher` are the boundaries between pure logic and platform code. Features depend on these interfaces, never on concrete Windows APIs. Tests use fakes.
- **No Windows APIs in `Core` or `Features.*`.** All Windows calls live in `GuildRelay.Platform.Windows`. This is what lets the features and the publisher be unit-tested without a Windows host.
- **Secret redaction is mandatory.** The Discord webhook URL must never appear in any log sink or template render path. It is accessed only via `SecretStore`. When wrapping or logging `HttpRequestException`, always redact `request.RequestUri` before writing. Add a test whenever you touch code that handles the webhook URL.
- **Small, focused files.** When a file grows past ~300 lines, ask whether it's doing too much. Features should be decomposable into capture / preprocess / detect / emit pieces that can each be understood in isolation.
- **One feature, one watchdog.** Features run inside `WatchdogTask` instances owned by Core Host. A feature's unhandled exceptions must not propagate beyond its watchdog. If you catch an exception in a feature's hot path, log it via Serilog with structured fields and let the watchdog decide whether to restart.

## When in doubt

- **Requirements question?** Check [`requirements.md`](./requirements.md). If it's genuinely ambiguous, ask the user — do not guess.
- **Architecture question?** Check [`docs/superpowers/specs/2026-04-11-guild-event-relay-architecture.md`](./docs/superpowers/specs/2026-04-11-guild-event-relay-architecture.md). The architecture doc is the contract; if you need to deviate, update the doc first and get approval.
- **Anti-cheat question?** The default answer is "don't do it that way." Propose an external-observation alternative. If nothing external-only works, the feature is out of scope.

## Build / test commands

These will be added once the solution is scaffolded. Expected:

```
dotnet restore
dotnet build  --configuration Release
dotnet test
dotnet publish src/GuildRelay.App -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

## Superpowers usage

This repo uses the Superpowers plugin. Expected skill usage:

- **Before any creative/feature work:** `superpowers:brainstorming` (already done for v1; see `docs/superpowers/specs/`).
- **Before implementation of any multi-step task:** `superpowers:writing-plans` to produce a plan, then `superpowers:executing-plans` to work through it.
- **While implementing:** `superpowers:test-driven-development` for every feature and bugfix.
- **Before claiming work is done:** `superpowers:verification-before-completion` — run the tests, read the output, then make claims.
- **When debugging:** `superpowers:systematic-debugging`.

Plans live in `docs/superpowers/plans/`, specs in `docs/superpowers/specs/`.
