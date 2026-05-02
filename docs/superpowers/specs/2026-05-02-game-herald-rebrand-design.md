# Game Herald Rebrand — Design

**Status:** Design approved, implementation plan pending
**Date:** 2026-05-02
**Author:** Brainstormed with Claude Code

---

## 1. Goal

Replace the user-facing app name `GuildRelay` with **`Game Herald`** in window titles, the tray tooltip, the README, the release-artifact filename, and startup error dialogs. Replace the placeholder `tray.ico` with a heraldic pennant rendered by an in-repo icon generator.

## 2. Why "Game Herald"

The original `GuildRelay` undersold the app's identity in two ways:

- **Relay** suggests passive forwarding (like a mail relay). The app does much more — it actively *watches* the player's session via OCR, audio loopback, and screen-region change detection, then *announces* what it sees to the guild. "Herald" captures both halves: a herald sees and tells.
- **Guild** is accurate but generic; "Game Herald" reads as a town crier for game events specifically, which fits the Mortal Online 2 medieval/fantasy register that the target audience already lives in.

The new name was chosen during brainstorming over alternatives like *Watchtower*, *Sentinel*, *Beacon*, *Vigil*, *Skald*. *Herald* won on the watch+announce duality and on being a single, evocative, MO2-flavored noun.

## 3. Scope — user-facing only

Code-level identifiers (assemblies, namespaces, install path, repo name, code-signing cert) all keep `GuildRelay`. The app's "internal codename" is `GuildRelay`; its "user-facing brand" is `Game Herald`. End users never see the codename, so renaming it would be churn for no benefit and would break:

- existing user `config.json` paths (`%APPDATA%\GuildRelay\`)
- historical spec/plan references in `docs/superpowers/`
- repo URL bookmarks and clone scripts
- code-signing cert subject (`CN=GuildRelay`)

A future "full rebrand" task could collapse codename → brand if desired, but it is outside this scope.

### 3.1 What changes

| File / location | Before | After |
|---|---|---|
| `src/GuildRelay.App/Config/ConfigWindow.xaml` `Title` + `<ui:TitleBar Title="…"/>` (lines 6 + 12 approximately) | `GuildRelay — Config` | `Game Herald — Config` |
| `src/GuildRelay.App/Stats/StatsWindow.xaml` `Title` + `<ui:TitleBar Title="…"/>` | `GuildRelay — Stats` | `Game Herald — Stats` |
| `src/GuildRelay.App/Tray/TrayView.xaml` `<tb:TaskbarIcon ToolTipText="…"/>` line 8 | `GuildRelay` | `Game Herald` |
| `src/GuildRelay.App/App.xaml.cs` lines 40-41, startup error `MessageBox.Show` | `"GuildRelay failed to start…"`, `"GuildRelay — Startup Error"` | `"Game Herald failed to start…"`, `"Game Herald — Startup Error"` |
| `src/GuildRelay.App/Config/ConfigWindow.xaml.cs` line 192, test-webhook `MatchedContent` | `"GuildRelay connected - hello from {player}"` | `"Game Herald connected - hello from {player}"` |
| `README.md` H1 (line 1), marketing prose (lines 7, 21, 55, 67, 124, 126, 128, 137, 146, 159, 217) | `GuildRelay` (in prose) | `Game Herald` |
| `README.md` ZIP filename references (lines 13, 63 of `release.yml`) | `GuildRelay-…-win-x64.zip` | `GameHerald-…-win-x64.zip` |
| `.github/workflows/release.yml` lines 63 + 68 | `GuildRelay-${{ github.ref_name }}-win-x64.zip` | `GameHerald-${{ github.ref_name }}-win-x64.zip` |

**README.md references that DO NOT change** (these are code paths, exe filenames, project structure, or data-dir paths that all live under the `GuildRelay` codename):
- `Run GuildRelay.App.exe.` (line 14) — the executable filename, which is the assembly name.
- `dotnet publish src/GuildRelay.App` (line 183) — code path.
- `src/GuildRelay.App/bin/.../publish/GuildRelay.App.exe` (line 186) — code path.
- The "Build / project structure" block (lines 191-200) listing `GuildRelay.Core/`, `GuildRelay.Platform.Windows/`, etc. — code-level project names.
- `%APPDATA%\GuildRelay\config.json` and `%APPDATA%\GuildRelay\logs\` (line 150) — data dir, stays per §3.

The other secondary windows (`RuleEditorWindow`, `CounterRuleEditorWindow`, `DebugLiveView`, `RegionPickerWindow`) do not contain `GuildRelay` in their titles today (verified by grep) — they use feature-specific names like `"Add Rule"`, `"Counter Rule"`, `"Chat Watcher — Live Debug View"`. No changes required there.

### 3.2 What does not change

- All `GuildRelay.*` namespaces, project files, csproj/sln entries
- The `GuildRelay.App.exe` produced executable
- `%APPDATA%\GuildRelay\config.json` and `%APPDATA%\GuildRelay\logs\`
- Code-signing cert subject (`CN=GuildRelay, O=GuildRelay Open Source`)
- LICENSE file copyright text
- Repo URL on GitHub
- Historical specs and plans under `docs/superpowers/specs/` and `docs/superpowers/plans/`
- CLAUDE.md (the project codename for development context remains `GuildRelay`; one-line note may be added clarifying that the user-facing brand is `Game Herald`)

## 4. Logo design

### 4.1 Concept

A swallow-tail heraldic pennant on a vertical pole. Single solid color (gold `#D4AF37`) on a transparent background so the icon adapts to light and dark Windows themes.

```
   .
   |\___
   |    \____
   |    ____|
   |___/
   |
   |
   |
```

### 4.2 Visual spec

- **Resolutions packed into the `.ico`:** 16 × 16, 24 × 24, 32 × 32, 48 × 48, 64 × 64, 256 × 256.
- **Pole:** 1 px wide at 16 × 16, scaling linearly with size (so 16 px at 256 × 256). Full canvas height. Positioned at canvas-x ≈ 25%.
- **Pennant:** triangular flag attached at the pole's top. Width ≈ 65% of canvas. Height ≈ 35% of canvas. The fly end (right side) has a triangular V-notch cut in to make the swallow-tail shape.
- **Color:** `#D4AF37` (gold) for pole and pennant. Background fully transparent.
- **No text or interior detail.** At 16 × 16, even a single glyph is illegible.

### 4.3 Implementation tooling

A new console project `tools/IconGen/`:

- .NET 8, `<TargetFramework>net8.0-windows</TargetFramework>` (uses `System.Drawing.Common`, Windows-only — fine; the host app is Windows-only too).
- Renders the pennant at each resolution as a `Bitmap` with anti-aliasing disabled at the smallest sizes (sharp pixel edges read better than smudged ones at 16 × 16).
- Encodes each bitmap as PNG (in-memory) and packs them into the multi-resolution `.ico` format by writing the ICONDIR + ICONDIRENTRY headers and PNG payloads sequentially. The ICO format is well-documented; the writer is ~80 lines.
- Output path: overwrites `src/GuildRelay.App/Assets/tray.ico`.
- Run via `dotnet run --project tools/IconGen`. Not wired into CI; the regenerated `.ico` is committed by hand when the design changes.
- Excluded from the test run path (no test project references it).

### 4.4 Why an in-repo generator instead of a hand-edited binary

The current `tray.ico` is a placeholder copied in during the foundation work — there is no source for it. With a generator:

- Tweaking colour or proportions becomes a code diff, not a binary blob.
- New developers can regenerate from scratch if the binary is ever lost.
- The design intent is documented in code, not in someone's memory.

The cost is ~150 LOC and a one-time `dotnet new console` for the tools project. Acceptable for a maintained app.

## 5. Out of scope

- Renaming any code-level identifier (`GuildRelay.*` namespaces, project files, the `.exe` filename, the install dir, the repo).
- A larger marketing logo (1024 × 1024 banner for store listings or a fancy README header). The 256 × 256 size we render is sufficient for the README and release page; a separate marketing pass can produce more.
- Animated or state-themed icon variants (e.g. red pennant on webhook error, gray pennant when paused). Worth considering as a follow-up but not part of this rename.
- Updating historical specs and plans in `docs/superpowers/`. They reference `GuildRelay` because that is the codename they were written under; rewriting them rewrites history.
- Re-issuing the code-signing cert under a new subject. Existing builds remain signed under `CN=GuildRelay`.

## 6. Anti-cheat compliance

This change is purely cosmetic — UI strings, README, an icon file, and one console tool that runs at developer time. No new screen capture, audio capture, process interaction, or runtime behaviour. Fully compliant with the project's anti-cheat policy.

## 7. Open questions

None. All scope and design decisions resolved during brainstorming. The implementation plan can proceed directly from this spec.
