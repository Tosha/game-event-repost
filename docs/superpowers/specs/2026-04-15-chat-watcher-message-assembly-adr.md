# ADR: Chat Watcher reads full chat messages instead of OCR lines

- **Date:** 2026-04-15
- **Status:** Proposed
- **Scope:** `GuildRelay.Features.Chat`

## Context

The Chat Watcher today runs OCR on the configured chat region, then treats each OCR line as an independent candidate for rule matching ([`ChatWatcher.ProcessOneTickAsync`](../../../src/GuildRelay.Features.Chat/ChatWatcher.cs)). Real game messages routinely wrap across two or three OCR lines — visible in the fixtures at `tests/GuildRelay.Platform.Windows.Tests/Fixtures/`:

- `mo2-chat-eastern-highlands.png` — `[Game]` ban spans two OCR lines.
- `mo2-chat-plains-of-meduli.png` — `[Game]` ban with a visible `[HH:MM:SS]` prefix, wraps across two lines.
- `mo2-chat-sylvan-sanctum.png` — similar two-line wrap.

To handle wrapping, the current code builds *candidates* for every line by also joining it with the next 1 and next 2 lines, then tries each candidate against the rules in length-descending order. This has known weaknesses:

- **Brittle upper bound (3 lines).** Long `[Game]` bans wrap further and silently miss.
- **Cross-boundary phantom joins.** Two adjacent message fragments that are really different messages can get joined and produce a match that corresponds to no real message.
- **Dedup fragmentation.** `ChatDedup` hashes per line, so a single real message registers as up to three different hashes (line1; line1+line2; line1+line2+line3) — which one "wins" depends on which candidate fires first.
- **Timestamps are a MO2 chat setting.** Users can toggle timestamp display on or off. The single-line parser tolerates both shapes, but the surrounding pipeline does not understand "message" as a concept that spans lines.

## Decision

Change the unit of work from **OCR line** to **chat message**. A `ChatMessage` is assembled from one or more OCR lines by header-based boundary detection.

### Header rule

Re-uses the existing [`ChatLineParser`](../../../src/GuildRelay.Features.Chat/ChatLineParser.cs). A line is a **header** if either:

- its first bracketed tag is a known channel (`[Nave]`, `[Game]`, `[Guild]`, …), or
- its first bracketed tag is a timestamp-shaped token AND its second bracketed tag is a known channel (`[20:02:15][Game]…`).

Any line that is not a header is a **continuation** of the currently-open message.

### Assembly algorithm (per capture tick)

1. Walk OCR lines in screen order.
2. On a header line: close the open message (if any) and start a new one, parsing `{Timestamp?, Channel, Player?, BodyStart}`.
3. On a continuation line: append to the open message's body with a single space separator.
4. When OCR lines are exhausted, the *trailing* open message is placed in a **deferred buffer** and emitted on the next tick — the last message on screen has no terminator, so we wait one tick for it to either stay put, grow by one or more continuation lines, or scroll off.

### Cross-tick deferral

- Buffer holds at most one previous-tick trailing message.
- On the next tick, compare the buffered message against the new tick's assembly by `(channel, player, timestamp?, body-prefix)`:
  - **Grown** → emit the new, longer version; clear buffer.
  - **Unchanged** → emit the buffered version; clear buffer.
  - **Not found** (scrolled off) → emit the buffered version; clear buffer.
- Buffer is reset on feature stop/restart, same as `ChatDedup` and `CooldownTracker`.

### Dedup

Per-message hash over `channel | player | timestamp? | body`. Replaces per-line dedup. `ChatDedup` itself can either be renamed or kept as a generic LRU hash set and called with message-level input — the contract does not change.

### Rule matching

`ChannelMatcher.FindMatch` takes a `ChatMessage` (semantically equivalent to the existing `ParsedChatLine`, but with a fully-assembled body). The 1/2/3-line join fallback in `ChatWatcher.ProcessOneTickAsync` is removed.

## Consequences

### Positive

- Correct matching for messages that wrap any number of lines.
- No phantom cross-boundary joins.
- Dedup becomes meaningful (one hash per real message).
- Timestamp on/off handled uniformly by the header rule.
- Simpler inner loop in `ChatWatcher`: one candidate per message, no length-descending list.

### Negative

- The last visible message on screen is emitted one capture interval later (+5 s by default).
- OCR-corrupted headers now cause the whole message to be missed instead of the "lucky join" falling through on occasion. Mitigated by the existing tolerance list in `ChatLineParser.KnownChannels` (leading-dot artifacts such as `.game`, `.nave`).
- The assembler introduces small cross-tick state (one buffered message) that must be reset at the same lifecycle points as `ChatDedup` and `CooldownTracker`.

### Neutral

- `ChatLineParser` stays; it is repurposed as the header-classifier primitive for the assembler.
- The live debug view in `ChatWatcher` changes its per-tick shape — OCR lines become grouped under messages. Existing `ChatTickDebugInfo` fields (`OcrLines`, `NormalizedLines`, `ParsedChannels`, `MatchResults`) remain meaningful but gain a per-message grouping.
- OCR confidence threshold now applies at the line level during assembly. Policy: a continuation line below threshold is dropped (the message is still emitted without it); a header line below threshold drops the whole message.

## Alternatives considered

1. **Emit the last message immediately.** Lower latency, but the first emission is usually partial; dedup then suppresses the complete version on the next tick, so the short form wins. Rejected.
2. **Sentence-terminator heuristic for the last message.** Emit immediately if body ends with `.`, `!`, `?`, or `)`; otherwise defer one tick. Fragile — OCR drops punctuation and game messages do not always end in terminators. Rejected.
3. **Keep the line-based pipeline and raise the join count to N.** Does not solve boundary ambiguity or dedup fragmentation; just delays them. Rejected.

## Open questions (resolved in the implementation plan, not here)

- Should the OCR confidence threshold for header lines differ from the threshold for body/continuation lines? Misclassifying a header as a continuation swallows a whole message, so headers may warrant a stricter bar.
- How many deferred messages to tolerate if two capture ticks in a row have no terminator below the buffered message (e.g., chat is idle).

## References

- Current implementation: [`ChatWatcher.ProcessOneTickAsync`](../../../src/GuildRelay.Features.Chat/ChatWatcher.cs), [`ChatLineParser`](../../../src/GuildRelay.Features.Chat/ChatLineParser.cs), [`ChatDedup`](../../../src/GuildRelay.Features.Chat/ChatDedup.cs), [`ChannelMatcher`](../../../src/GuildRelay.Features.Chat/ChannelMatcher.cs).
- Fixtures: `tests/GuildRelay.Platform.Windows.Tests/Fixtures/mo2-chat-*.png`.
- Parent architecture: [`docs/superpowers/specs/2026-04-11-guild-event-relay-architecture.md`](./2026-04-11-guild-event-relay-architecture.md).
