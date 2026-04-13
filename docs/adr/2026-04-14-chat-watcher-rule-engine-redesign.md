# ADR: Chat Watcher Rule Engine Redesign

**Status:** Proposed
**Date:** 2026-04-14
**Author:** GuildRelay team

## Context

The current Chat Watcher rule engine requires users to write pipe-delimited rules with regex patterns in a raw text box:

```
MO2 Game Events|\[game\].*sylvan sanctum|regex|600
Incoming|(inc|incoming|enemies)|regex|120
```

This has several problems:

1. **Hard to understand.** Users must know regex syntax, escape brackets, and understand the pipe-delimited format. Most guild members are gamers, not developers.
2. **Error-prone.** A misplaced `|` breaks the rule. Forgetting to escape `[` silently fails.
3. **Doesn't leverage MO2's fixed channel structure.** MO2 has a known, finite set of chat channels (see below). The rule engine treats every message as an opaque string, forcing users to manually construct patterns like `\[game\].*dire wolf` when the app could understand channel structure natively.
4. **No visual feedback.** Users can't see at a glance which channels are being watched or what keywords are active per channel.

### MO2 chat channel structure

From the MO2 chat settings screen, the game has exactly these channels:

| Channel | Typical content | Example message |
|---|---|---|
| **SAY** | Local player chat | `[Say] [PlayerName] hello there` |
| **YELL** | Longer-range player chat | `[Yell] [PlayerName] enemies incoming!` |
| **WHISPER** | Private messages | `[Whisper] [PlayerName] meet me at the gate` |
| **GUILD** | Guild-only chat | `[Guild] [PlayerName] heading to dungeon` |
| **SKILL** | Skill-up notifications | `[Skill] Your Mining increased to 45` |
| **COMBAT** | Combat log entries | `[Combat] You dealt 35 damage` |
| **GAME** | System events (dungeon spawns, tasks) | `[Game] A large band of Profiteers has been seen pillaging the Sylvan Sanctum!` |
| **SERVER** | Server announcements | `[Server] Server restart in 10 minutes` |
| **NAVE** | Global player chat | `[Nave] [PlayerName] anyone selling iron?` |
| **TRADE** | Trade chat | `[Trade] [PlayerName] WTS iron ingots` |
| **HELP** | Help requests | `[Help] [PlayerName] how do I craft?` |

Each message from OCR follows this structure:
```
[OptionalTimestamp][Channel] [OptionalPlayerName] MessageBody
```

Examples from real OCR output:
```
[21:02:15][Game] A large band of Profiteers has been seen pillaging the Sylvan Sanctum!
[21:07:35][Game] You received a task to kill Dire Wolf (8)
[Nave] [Stormbrew] they should just add it
[Nave] [Leyawiin] nah it would make blokes easy to hide
[21:03:40][Game] YourdrunkPapa has come online.
```

## Decision

Replace the raw text-box rule editor with a **structured, channel-aware rule builder** that maps directly to MO2's chat system.

### New rule model

```
Channel filter  +  Keyword filter  +  Options
```

Each rule becomes:

| Field | Type | Description |
|---|---|---|
| **Channels** | Multi-select checkboxes | Which MO2 channels to watch. Default: none (user must pick). |
| **Keywords** | Comma-separated list OR regex | What to look for in the message body. Empty = match all messages on that channel. |
| **Match mode** | Toggle: `Contains any` / `Regex` | Whether keywords are simple substring matches or a regex pattern. Default: `Contains any`. |
| **Label** | Text | Human-readable name shown in Discord posts (e.g., "Game Events"). |
| **Cooldown** | Number (seconds) | Min time between posts for this rule. Default: from global setting. |

### How matching works internally

When OCR produces a line, the new engine:

1. **Parses the channel tag** from the OCR text. A simple prefix extraction: look for `[Channel]` at the start (after optional `[timestamp]`). The parser knows the fixed set of MO2 channel names.
2. **Routes to matching rules.** Only rules whose channel filter includes the detected channel are evaluated. This is a fast O(1) lookup, not a regex scan of every rule.
3. **Applies keyword filter.** If the rule has keywords, check if the message body (after the channel tag) contains any of the keywords (substring mode) or matches the regex (regex mode). If no keywords, the rule matches all messages on that channel.
4. **Adjacent-line joining** still applies — if a match isn't found on a single line, the engine tries joining with the next line (same as today, for OCR line-splitting).

### UI design

The raw text box is replaced with a structured form:

```
┌──────────────────────────────────────────────────────────────┐
│  Rule: [Game Events                                    ]     │
│                                                              │
│  Channels:                                                   │
│  [x] GAME   [ ] SAY    [ ] YELL   [ ] WHISPER               │
│  [ ] GUILD  [ ] SKILL  [ ] COMBAT [x] SERVER                │
│  [ ] NAVE   [ ] TRADE  [ ] HELP                              │
│                                                              │
│  Keywords (comma-separated, or regex):                       │
│  [ Sylvan Sanctum, Dire Wolf, Profiteers              ]      │
│                                                              │
│  Match mode:  ( ) Contains any   (•) Regex                   │
│  Cooldown:    [ 600 ] seconds                                │
│                                                              │
│  [+ Add Rule]  [Remove Rule]                                 │
├──────────────────────────────────────────────────────────────┤
│  Active rules:                                               │
│  ● Game Events     — GAME, SERVER — 3 keywords — 600s       │
│  ● Guild Chat      — GUILD        — all messages — 60s      │
│  ● Incoming Alerts — NAVE, YELL   — "inc, incoming" — 120s  │
└──────────────────────────────────────────────────────────────┘
```

Key UI properties:
- **Channel checkboxes** are always visible — the user never has to type `[Game]` or escape brackets.
- **Keywords in "Contains any" mode** are just comma-separated words. No regex knowledge needed for the common case. "Dire Wolf, Sylvan Sanctum" just works.
- **Regex mode** is available for power users who need pattern matching, but it's opt-in.
- **Active rules list** shows a summary of all rules at a glance — which channels, how many keywords, cooldown.
- **Templates** still work. "Load Template" populates the rule list with pre-built rules. The MO2 Game Events template would create a rule with GAME channel checked and all 45 locations as keywords in "Contains any" mode — much more readable than the current regex blob.

### New config schema

```json
{
  "features": {
    "chat": {
      "rules": [
        {
          "id": "game_events",
          "label": "Game Events",
          "channels": ["GAME", "SERVER"],
          "keywords": ["Sylvan Sanctum", "Dire Wolf", "Profiteers"],
          "matchMode": "containsAny",
          "cooldownSec": 600
        },
        {
          "id": "guild_chat",
          "label": "Guild Chat",
          "channels": ["GUILD"],
          "keywords": [],
          "matchMode": "containsAny",
          "cooldownSec": 60
        }
      ]
    }
  }
}
```

Note: `keywords: []` (empty) means "match all messages on the selected channels" — useful for relaying entire channels to Discord.

### Channel parser implementation

A new `ChatLineParser` class in `GuildRelay.Features.Chat`:

```csharp
public sealed record ParsedChatLine(
    string? Timestamp,      // "21:02:15" or null
    string? Channel,        // "Game", "Nave", etc. or null if not recognized
    string? PlayerName,     // "Stormbrew" or null
    string Body);           // the rest of the message

public static class ChatLineParser
{
    private static readonly HashSet<string> KnownChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Say", "Yell", "Whisper", "Guild", "Skill",
        "Combat", "Game", "Server", "Nave", "Trade", "Help"
    };

    public static ParsedChatLine Parse(string normalizedLine) { ... }
}
```

The parser uses simple string operations (not regex) to extract the bracketed tags from the beginning of the line. It handles both `[timestamp][channel]` and `[channel]` formats, and extracts the player name if present (second bracketed tag after channel).

### Migration from current format

The old pipe-delimited format and `ChatRuleConfig` are replaced by a new `ChatRuleV2Config` with channels/keywords/matchMode fields. On first load of an old config:

1. If `rules` contains old-format entries (detected by presence of `pattern` field), they are displayed in a "Legacy rules" section with a note to re-create them using the new UI.
2. New installs get the structured format directly.
3. The old `CompiledPattern` matching path remains available for legacy rules during a transition period.

### Template redesign

The MO2 Game Events template becomes:

```json
{
  "label": "MO2 Game Events",
  "channels": ["GAME"],
  "keywords": [
    "Sylvan Sanctum", "Tindremic Heartlands", "Tindrem Sewers",
    "Green Weald", "Fabernum Tower", ...all 45 locations...
  ],
  "matchMode": "containsAny",
  "cooldownSec": 600
}
```

This is dramatically more readable than the current regex blob. Users can see and edit individual locations without understanding regex.

## Consequences

### Positive

- **Much easier to use.** Channel checkboxes eliminate bracket escaping. Comma-separated keywords eliminate regex for 90% of use cases.
- **Leverages game structure.** The app understands MO2 channels natively, enabling channel-based filtering that's impossible to get wrong.
- **Faster matching.** Channel-first routing skips rules that can't match, instead of running every regex against every line.
- **Better templates.** Templates are readable lists of keywords instead of opaque regex blobs.
- **Visual clarity.** The active rules summary shows at a glance what's being watched.
- **Extensible.** Adding a new channel (if MO2 adds one) is a one-line change to the `KnownChannels` set.

### Negative

- **Breaking change to config schema.** Existing users' rules need migration. Mitigated by the legacy-rules transition path.
- **More complex UI code.** The structured form has more controls than a text box. But the UX is simpler for the user.
- **Channel parsing depends on OCR accuracy.** If OCR misreads `[Game]` as `[.Game]` or `[Came]`, the channel parser won't match. Mitigated by fuzzy matching on channel names (Levenshtein distance <= 1) and by the existing preprocess pipeline that makes OCR reasonably reliable.

### Risks

- **OCR channel tag reliability.** From our test fixtures, OCR sometimes reads `[.Game]` instead of `[Game]`. The parser should handle common OCR artifacts: leading dots, spaces inside brackets, case variations.
- **New channels in future MO2 updates.** The known-channels list is hardcoded. If MO2 adds a channel, we need a code update. This is acceptable since channel additions are rare and we'd want to add UI support anyway.

## Alternatives considered

### 1. Keep the raw text box but add syntax highlighting

Add color-coded highlighting to the existing text box (green for valid rules, red for syntax errors). This helps power users but doesn't solve the fundamental UX problem for non-technical guild members.

**Rejected:** polishing a bad interface doesn't fix the core issue.

### 2. Visual rule builder with drag-and-drop

A more ambitious UI with draggable condition blocks (like Scratch or IFTTT). Overkill for the actual complexity of the rules.

**Rejected:** over-engineered for the problem. MO2 chat is simple enough that checkboxes + keywords are sufficient.

### 3. Channel-only filtering (no keywords)

Just let users pick channels to relay, without keyword filtering. Every message on a checked channel gets posted.

**Rejected:** too noisy. NAVE channel alone would flood Discord. Keywords are essential for filtering signal from noise.

## Implementation plan

1. Add `ChatLineParser` to `Features.Chat` with TDD (parse real OCR lines from fixtures)
2. Add new `ChatRuleV2Config` to `Core/Config` with channels/keywords/matchMode
3. Build the structured rule editor UI in `ChatConfigTab`
4. Update `ChatWatcher.ProcessOneTickAsync` to use channel-first routing
5. Update templates to use the new keyword-list format
6. Add legacy migration path for old config files
7. Update tests with real OCR fixture lines
