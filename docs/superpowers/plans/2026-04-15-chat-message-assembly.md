# Chat Watcher Message-Based Assembly Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the per-OCR-line matching pipeline in `ChatWatcher` with a header-driven assembler that groups wrapped OCR lines into complete chat messages before rule matching. Last (terminator-less) message is deferred one tick.

**Architecture:** Two pure units — `ChatMessageAssembler` (stateless, per-tick) and `DeferredTrailing` (stateless, two-tick resolution) — wired into `ChatWatcher` via a single `_deferredTrailing` field. `ChannelMatcher`, `ChatDedup`, and `CooldownTracker` remain untouched in shape; only the input unit changes from OCR line to assembled message. Live Debug View gains HEADER/CONT line role tags and DEFERRED/EMITTED-DEFERRED match-result prefixes.

**Tech Stack:** .NET 8, xUnit, FluentAssertions. No new top-level dependencies.

**ADR:** [docs/superpowers/specs/2026-04-15-chat-watcher-message-assembly-adr.md](../specs/2026-04-15-chat-watcher-message-assembly-adr.md)

---

### Task 1: Header-classifier primitive in ChatLineParser

The assembler needs a cheap "is this a header?" check. Add `IsHeader` that wraps `Parse` — a header is any line the existing parser assigns a non-null `Channel` to.

**Files:**
- Modify: `src/GuildRelay.Features.Chat/ChatLineParser.cs`
- Modify: `tests/GuildRelay.Features.Chat.Tests/ChatLineParserTests.cs`

- [ ] **Step 1: Add failing tests to `ChatLineParserTests.cs`**

Append:

```csharp
[Fact]
public void IsHeaderRecognizesChannelOnly()
{
    ChatLineParser.IsHeader("[Nave] [Stormbrew] inc north").Should().BeTrue();
}

[Fact]
public void IsHeaderRecognizesTimestampPlusChannel()
{
    ChatLineParser.IsHeader("[21:02:15][Game] A large band of Profiteers").Should().BeTrue();
}

[Fact]
public void IsHeaderRejectsPlainContinuation()
{
    ChatLineParser.IsHeader("Plains of Meduli!").Should().BeFalse();
}

[Fact]
public void IsHeaderRejectsUnknownBracketTag()
{
    ChatLineParser.IsHeader("[Unknown] some text").Should().BeFalse();
}
```

- [ ] **Step 2: Run tests — expect failures**

```
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~IsHeader" --nologo
```

Expected: 4 FAIL — compile error because `IsHeader` does not exist.

- [ ] **Step 3: Implement `IsHeader` in `ChatLineParser.cs`**

Add inside the `ChatLineParser` class (next to `Parse`):

```csharp
public static bool IsHeader(string line) => Parse(line).Channel is not null;
```

- [ ] **Step 4: Run tests — expect pass**

```
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~IsHeader" --nologo
```

Expected: 4 PASS.

- [ ] **Step 5: Commit**

```
git add src/GuildRelay.Features.Chat/ChatLineParser.cs tests/GuildRelay.Features.Chat.Tests/ChatLineParserTests.cs
git commit -m "feat(chat): add ChatLineParser.IsHeader classifier"
```

---

### Task 2: AssembledMessage record

Pure data carrier for a completed or in-progress chat message. All fields are public on the record.

**Files:**
- Create: `src/GuildRelay.Features.Chat/AssembledMessage.cs`

- [ ] **Step 1: Create `AssembledMessage.cs`**

```csharp
namespace GuildRelay.Features.Chat;

/// <summary>
/// A chat message assembled from one or more adjacent OCR lines.
/// The header portion (channel / timestamp / player) is taken from the first
/// constituent line; continuation lines are appended to Body with a single
/// space separator.
/// </summary>
public sealed record AssembledMessage(
    string? Timestamp,
    string? Channel,
    string? PlayerName,
    string Body,
    string OriginalText,
    int StartRow,
    int EndRow)
{
    public ParsedChatLine ToParsedChatLine()
        => new(Timestamp, Channel, PlayerName, Body);
}
```

- [ ] **Step 2: Verify build**

```
dotnet build src/GuildRelay.Features.Chat --nologo
```

Expected: success, 0 errors.

- [ ] **Step 3: Commit**

```
git add src/GuildRelay.Features.Chat/AssembledMessage.cs
git commit -m "feat(chat): add AssembledMessage record"
```

---

### Task 3: ChatMessageAssembler (single-tick, stateless)

Walks OCR lines in order, opens a new message on every header, appends continuations to the open message, closes the open message when a new header arrives, and hands the final open message back as `Trailing`.

**Files:**
- Create: `src/GuildRelay.Features.Chat/ChatMessageAssembler.cs`
- Create: `tests/GuildRelay.Features.Chat.Tests/ChatMessageAssemblerTests.cs`

- [ ] **Step 1: Create test file with failing tests**

Path: `tests/GuildRelay.Features.Chat.Tests/ChatMessageAssemblerTests.cs`

```csharp
using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class ChatMessageAssemblerTests
{
    private const double Threshold = 0.5;

    private static OcrLineInput L(string normalized, float confidence = 0.9f)
        => new(normalized, normalized, confidence);

    [Fact]
    public void EmptyInputYieldsEmptyResult()
    {
        var r = ChatMessageAssembler.Assemble(new List<OcrLineInput>(), Threshold);
        r.Completed.Should().BeEmpty();
        r.Trailing.Should().BeNull();
    }

    [Fact]
    public void SingleHeaderOnlyIsTrailing()
    {
        var r = ChatMessageAssembler.Assemble(new[] { L("[Nave] [Tosh] hello") }, Threshold);
        r.Completed.Should().BeEmpty();
        r.Trailing.Should().NotBeNull();
        r.Trailing!.Channel.Should().Be("Nave");
        r.Trailing.PlayerName.Should().Be("Tosh");
        r.Trailing.Body.Should().Be("hello");
    }

    [Fact]
    public void TwoHeadersYieldOneCompletedOneTrailing()
    {
        var r = ChatMessageAssembler.Assemble(new[]
        {
            L("[Nave] [Tosh] first"),
            L("[Nave] [Tosh] second"),
        }, Threshold);

        r.Completed.Should().HaveCount(1);
        r.Completed[0].Body.Should().Be("first");
        r.Trailing!.Body.Should().Be("second");
    }

    [Fact]
    public void WrappedMessageJoinedWithSpace()
    {
        var r = ChatMessageAssembler.Assemble(new[]
        {
            L("[21:02:15][Game] A large band of Profiteers has been"),
            L("seen pillaging the Plains of Meduli!"),
            L("[Nave] [Next] next message"),
        }, Threshold);

        r.Completed.Should().HaveCount(1);
        var c = r.Completed[0];
        c.Channel.Should().Be("Game");
        c.Timestamp.Should().Be("21:02:15");
        c.Body.Should().Be("A large band of Profiteers has been seen pillaging the Plains of Meduli!");
        c.StartRow.Should().Be(0);
        c.EndRow.Should().Be(1);

        r.Trailing!.Channel.Should().Be("Nave");
    }

    [Fact]
    public void ContinuationBeforeFirstHeaderIsIgnored()
    {
        var r = ChatMessageAssembler.Assemble(new[]
        {
            L("orphan text with no header above"),
            L("[Nave] [Tosh] real message"),
        }, Threshold);

        r.Completed.Should().BeEmpty();
        r.Trailing!.Body.Should().Be("real message");
        r.Trailing.StartRow.Should().Be(1);
    }

    [Fact]
    public void HeaderBelowThresholdDropsWholeMessage()
    {
        var r = ChatMessageAssembler.Assemble(new[]
        {
            L("[Nave] [Tosh] ok", confidence: 0.9f),
            L("[Game] something", confidence: 0.2f),          // bad header
            L("continuation of bad header", confidence: 0.9f), // orphan now
            L("[Nave] [Tosh] third", confidence: 0.9f),
        }, Threshold);

        r.Completed.Should().HaveCount(1);
        r.Completed[0].Body.Should().Be("ok");
        r.Trailing!.Body.Should().Be("third");
    }

    [Fact]
    public void ContinuationBelowThresholdIsSkippedMessageStillEmitted()
    {
        var r = ChatMessageAssembler.Assemble(new[]
        {
            L("[Game] A large band of Profiteers has been", confidence: 0.9f),
            L("noise", confidence: 0.1f),                       // skipped
            L("pillaging the Plains of Meduli!", confidence: 0.9f),
            L("[Nave] [Tosh] done", confidence: 0.9f),
        }, Threshold);

        r.Completed.Should().HaveCount(1);
        r.Completed[0].Body.Should().Be(
            "A large band of Profiteers has been pillaging the Plains of Meduli!");
        r.Completed[0].EndRow.Should().Be(2);
        r.Trailing!.Body.Should().Be("done");
    }

    [Fact]
    public void OriginalTextPreservesCase()
    {
        var r = ChatMessageAssembler.Assemble(new[]
        {
            new OcrLineInput("[nave] [tosh] lowered", "[Nave] [Tosh] Lowered", 0.9f),
            new OcrLineInput("[nave] [tosh] next", "[Nave] [Tosh] Next", 0.9f),
        }, Threshold);

        r.Completed.Should().HaveCount(1);
        r.Completed[0].OriginalText.Should().Be("[Nave] [Tosh] Lowered");
    }
}
```

- [ ] **Step 2: Run tests — expect failures (compile errors — types do not exist)**

```
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~ChatMessageAssemblerTests" --nologo
```

Expected: compile errors for `OcrLineInput` and `ChatMessageAssembler`.

- [ ] **Step 3: Create `src/GuildRelay.Features.Chat/ChatMessageAssembler.cs`**

```csharp
using System.Collections.Generic;

namespace GuildRelay.Features.Chat;

/// <summary>
/// One OCR line fed into the assembler. Normalized is the de-noised text used
/// for parsing/matching; Original is the raw OCR output preserved for debug.
/// </summary>
public sealed record OcrLineInput(string Normalized, string Original, float Confidence);

public sealed record AssemblyResult(
    IReadOnlyList<AssembledMessage> Completed,
    AssembledMessage? Trailing);

public static class ChatMessageAssembler
{
    public static AssemblyResult Assemble(
        IReadOnlyList<OcrLineInput> lines,
        double confidenceThreshold)
    {
        var completed = new List<AssembledMessage>();
        OpenMessage? open = null;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var belowThreshold = line.Confidence < confidenceThreshold;

            // A line is classified by its NORMALIZED content. Threshold decides
            // whether we accept it at all.
            var isHeader = !belowThreshold && ChatLineParser.IsHeader(line.Normalized);

            if (belowThreshold)
            {
                // We do not TRUST the low-confidence line, but we can still look at
                // its shape to decide whether it structurally terminates the open
                // message. If it looks like a header, close any open message (but do
                // NOT start a new one from this garbled header). If it looks like
                // a continuation, skip it silently and leave the open message alive.
                if (ChatLineParser.IsHeader(line.Normalized))
                {
                    if (open is not null)
                    {
                        completed.Add(open.ToAssembled());
                        open = null;
                    }
                }
                continue;
            }

            if (isHeader)
            {
                if (open is not null)
                {
                    completed.Add(open.ToAssembled());
                }
                var parsed = ChatLineParser.Parse(line.Normalized);
                open = new OpenMessage(
                    parsed.Timestamp,
                    parsed.Channel!,
                    parsed.PlayerName,
                    parsed.Body,
                    line.Original,
                    startRow: i,
                    endRow: i);
            }
            else
            {
                if (open is null) continue; // orphan continuation before any header
                open.AppendLine(line.Normalized, line.Original, rowIndex: i);
            }
        }

        AssembledMessage? trailing = open?.ToAssembled();
        return new AssemblyResult(completed, trailing);
    }

    private sealed class OpenMessage
    {
        private string _body;
        private string _original;
        public string? Timestamp { get; }
        public string Channel { get; }
        public string? PlayerName { get; }
        public int StartRow { get; }
        public int EndRow { get; private set; }

        public OpenMessage(string? timestamp, string channel, string? playerName,
            string initialBody, string initialOriginal, int startRow, int endRow)
        {
            Timestamp = timestamp;
            Channel = channel;
            PlayerName = playerName;
            _body = initialBody;
            _original = initialOriginal;
            StartRow = startRow;
            EndRow = endRow;
        }

        public void AppendLine(string normalized, string original, int rowIndex)
        {
            if (_body.Length == 0) _body = normalized;
            else _body = _body + " " + normalized;

            if (_original.Length == 0) _original = original;
            else _original = _original + " " + original;

            EndRow = rowIndex;
        }

        public AssembledMessage ToAssembled() =>
            new(Timestamp, Channel, PlayerName, _body, _original, StartRow, EndRow);
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

```
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~ChatMessageAssemblerTests" --nologo
```

Expected: all 8 PASS.

- [ ] **Step 5: Commit**

```
git add src/GuildRelay.Features.Chat/ChatMessageAssembler.cs tests/GuildRelay.Features.Chat.Tests/ChatMessageAssemblerTests.cs
git commit -m "feat(chat): add header-driven ChatMessageAssembler"
```

---

### Task 4: Cross-tick deferred-trailing resolver

Given the previous tick's buffered trailing message and the current tick's assembly, decide what to emit and what to keep buffered. Pure function.

**Files:**
- Create: `src/GuildRelay.Features.Chat/DeferredTrailing.cs`
- Create: `tests/GuildRelay.Features.Chat.Tests/DeferredTrailingTests.cs`

- [ ] **Step 1: Create failing tests**

```csharp
using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class DeferredTrailingTests
{
    private static AssembledMessage Msg(string body, string? channel = "Nave",
        string? player = "Tosh", string? ts = null, int start = 0, int end = 0)
        => new(ts, channel, player, body, body, start, end);

    [Fact]
    public void NoPreviousTrailingEmitsCompletedBuffersTrailing()
    {
        var current = new AssemblyResult(new[] { Msg("c0"), Msg("c1") }, Msg("tcur"));
        var (toEmit, buffer) = DeferredTrailing.Resolve(previousTrailing: null, current);
        toEmit.Should().HaveCount(2);
        toEmit[0].Body.Should().Be("c0");
        toEmit[1].Body.Should().Be("c1");
        buffer!.Body.Should().Be("tcur");
    }

    [Fact]
    public void PreviousNotFoundEmitsPreviousThenCompleted()
    {
        var prev = Msg("old trailing", channel: "Guild", player: "A");
        var current = new AssemblyResult(
            new[] { Msg("new1", channel: "Nave", player: "B") },
            Msg("new2", channel: "Nave", player: "B"));
        var (toEmit, buffer) = DeferredTrailing.Resolve(prev, current);
        toEmit.Should().HaveCount(2);
        toEmit[0].Body.Should().Be("old trailing");
        toEmit[1].Body.Should().Be("new1");
        buffer!.Body.Should().Be("new2");
    }

    [Fact]
    public void PreviousFoundGrownInCompletedSkipsPrevious()
    {
        var prev = Msg("A large band of Prof", channel: "Game", player: null, ts: "21:02:15");
        var grown = Msg("A large band of Prof pillaging the Plains of Meduli!",
            channel: "Game", player: null, ts: "21:02:15");
        var current = new AssemblyResult(new[] { grown }, Trailing: null);
        var (toEmit, buffer) = DeferredTrailing.Resolve(prev, current);
        toEmit.Should().ContainSingle().Which.Body.Should().Be(grown.Body);
        buffer.Should().BeNull();
    }

    [Fact]
    public void PreviousStillTrailingEmitsPreviousAndBuffersCurrentTrailing()
    {
        // Chat idle for one tick: the same trailing appears again.
        var prev = Msg("hello world", channel: "Nave", player: "Tosh");
        var current = new AssemblyResult(
            new List<AssembledMessage>(),
            Msg("hello world", channel: "Nave", player: "Tosh"));
        var (toEmit, buffer) = DeferredTrailing.Resolve(prev, current);
        toEmit.Should().ContainSingle().Which.Body.Should().Be("hello world");
        buffer!.Body.Should().Be("hello world");
    }

    [Fact]
    public void PreviousScrolledOffEmitsPreviousAndBuffersNewTrailing()
    {
        var prev = Msg("scrolled off", channel: "Nave", player: "Tosh");
        var current = new AssemblyResult(
            new List<AssembledMessage>(),
            Msg("new trailing", channel: "Nave", player: "Tosh"));
        var (toEmit, buffer) = DeferredTrailing.Resolve(prev, current);
        toEmit.Should().ContainSingle().Which.Body.Should().Be("scrolled off");
        buffer!.Body.Should().Be("new trailing");
    }

    [Fact]
    public void EmptyCurrentWithPreviousEmitsPreviousAndClearsBuffer()
    {
        var prev = Msg("final", channel: "Nave", player: "Tosh");
        var current = new AssemblyResult(new List<AssembledMessage>(), Trailing: null);
        var (toEmit, buffer) = DeferredTrailing.Resolve(prev, current);
        toEmit.Should().ContainSingle().Which.Body.Should().Be("final");
        buffer.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests — expect compile errors**

```
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~DeferredTrailingTests" --nologo
```

Expected: compile error — `DeferredTrailing` does not exist.

- [ ] **Step 3: Create `src/GuildRelay.Features.Chat/DeferredTrailing.cs`**

```csharp
using System.Collections.Generic;

namespace GuildRelay.Features.Chat;

/// <summary>
/// Resolves a previous tick's trailing message against the current tick's
/// assembly. Implements the one-tick deferral described in the ADR: the last
/// message on screen is buffered until the next tick, then emitted as either
/// its grown form (if the next tick confirmed it extended) or its buffered
/// form (if it stayed the same or scrolled off).
/// </summary>
public static class DeferredTrailing
{
    public static (IReadOnlyList<AssembledMessage> ToEmit, AssembledMessage? NewBuffer)
        Resolve(AssembledMessage? previousTrailing, AssemblyResult current)
    {
        var toEmit = new List<AssembledMessage>();

        if (previousTrailing is not null)
        {
            var resolvedByCompleted = false;
            foreach (var msg in current.Completed)
            {
                if (IsGrownVersion(msg, previousTrailing))
                {
                    resolvedByCompleted = true;
                    break;
                }
            }
            if (!resolvedByCompleted)
                toEmit.Add(previousTrailing);
        }

        toEmit.AddRange(current.Completed);
        return (toEmit, current.Trailing);
    }

    /// <summary>
    /// Candidate is the grown version of previous iff header identity matches
    /// and candidate's body starts with previous's body.
    /// </summary>
    private static bool IsGrownVersion(AssembledMessage candidate, AssembledMessage previous)
    {
        if (candidate.Channel != previous.Channel) return false;
        if (candidate.PlayerName != previous.PlayerName) return false;
        if (candidate.Timestamp != previous.Timestamp) return false;
        return candidate.Body.StartsWith(previous.Body);
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

```
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~DeferredTrailingTests" --nologo
```

Expected: all 6 PASS.

- [ ] **Step 5: Commit**

```
git add src/GuildRelay.Features.Chat/DeferredTrailing.cs tests/GuildRelay.Features.Chat.Tests/DeferredTrailingTests.cs
git commit -m "feat(chat): add cross-tick DeferredTrailing resolver"
```

---

### Task 5: Wire assembler + deferral into ChatWatcher

Replace the per-line candidate loop with: assemble → resolve deferred → dedup → match → cooldown → publish. Add `_deferredTrailing` field; reset it in Start/Stop alongside dedup and cooldown. Delete the 1/2/3-line join fallback.

Also: extend `ChatTickDebugInfo` with `LineRoles` (parallel to `OcrLines`, values `"HEADER"`, `"CONT"`, or `"SKIP"`) so the live view can render per-message groupings.

**Files:**
- Modify: `src/GuildRelay.Features.Chat/ChatWatcher.cs`
- Modify: `tests/GuildRelay.Features.Chat.Tests/ChatWatcherTests.cs`

- [ ] **Step 1: Update existing ChatWatcher tests**

The old tests send a single OCR line that matches. Under the new logic, that single line becomes the trailing message and is not emitted until the next tick. Rewrite them to send two ticks' worth of input or two-line input with a terminator header. Replace the entire `ChatWatcherTests.cs` body below `CreateWatcher` with:

```csharp
    [Fact]
    public async Task MatchingCompletedMessageEmitsDetectionEvent()
    {
        var ocr = new FakeOcr();
        var bus = new EventBus(capacity: 16);
        var rules = new List<StructuredChatRule>
        {
            new("r1", "Incoming",
                new List<string> { "Nave", "Yell" },
                new List<string> { "(inc|incoming)" },
                MatchMode.Regex)
        };
        var watcher = CreateWatcher(ocr, bus, rules);

        // Two lines: the first is the message we want to match, the second
        // acts as a terminator so the first becomes a Completed message.
        ocr.NextLines = new List<OcrLine>
        {
            new("[Nave] [Someone] inc north gate", 0.9f, RectangleF.Empty),
            new("[Nave] [Other]    status ok",      0.9f, RectangleF.Empty),
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(1500);
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().ContainSingle()
            .Which.Should().Match<DetectionEvent>(e =>
                e.FeatureId == "chat" && e.RuleLabel == "Incoming" && e.PlayerName == "Tosh");
    }

    [Fact]
    public async Task WrappedTwoLineGameMessageIsMatchedAfterAssembly()
    {
        var ocr = new FakeOcr();
        var bus = new EventBus(capacity: 16);
        var rules = new List<StructuredChatRule>
        {
            new("r1", "World Event",
                new List<string> { "Game" },
                new List<string> { "plains of meduli" },
                MatchMode.ContainsAny)
        };
        var watcher = CreateWatcher(ocr, bus, rules);

        // First line is the [Game] header + start of body; second line is the
        // continuation. Third line is a terminator so the wrapped message completes.
        ocr.NextLines = new List<OcrLine>
        {
            new("[21:02:15][Game] A large band of Profiteers has been", 0.9f, RectangleF.Empty),
            new("seen pillaging the Plains of Meduli!",                 0.9f, RectangleF.Empty),
            new("[Nave] [Tosh] nothing to see",                         0.9f, RectangleF.Empty),
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(1500);
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().ContainSingle()
            .Which.RuleLabel.Should().Be("World Event");
    }

    [Fact]
    public async Task LastMessageIsDeferredOneTickThenEmitted()
    {
        var ocr = new FakeOcr();
        var bus = new EventBus(capacity: 16);
        var rules = new List<StructuredChatRule>
        {
            new("r1", "Incoming",
                new List<string> { "Nave" },
                new List<string> { "inc" },
                MatchMode.ContainsAny)
        };
        var watcher = CreateWatcher(ocr, bus, rules);

        // Only one line - it's trailing with no terminator, so tick 1 emits
        // nothing. On tick 2 (same OCR), it is emitted from the deferred buffer.
        ocr.NextLines = new List<OcrLine>
        {
            new("[Nave] [Someone] inc north", 0.9f, RectangleF.Empty),
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(2500);  // ~2 ticks at 1s interval
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().ContainSingle("the deferred trailing message should fire once on the second tick");
    }

    [Fact]
    public async Task DuplicateCompletedMessagesEmitOnlyOnce()
    {
        var ocr = new FakeOcr();
        var bus = new EventBus(capacity: 16);
        var rules = new List<StructuredChatRule>
        {
            new("r1", "Incoming",
                new List<string> { "Nave" },
                new List<string> { "inc" },
                MatchMode.ContainsAny)
        };
        var watcher = CreateWatcher(ocr, bus, rules);

        ocr.NextLines = new List<OcrLine>
        {
            new("[Nave] [Someone] inc north",       0.9f, RectangleF.Empty),
            new("[Nave] [Other]    terminator ok",  0.9f, RectangleF.Empty),
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(2500);  // ~2 ticks
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().ContainSingle("the same message on two consecutive ticks dedups to one event");
    }

    [Fact]
    public async Task LowConfidenceHeaderDropsMessage()
    {
        var ocr = new FakeOcr();
        var bus = new EventBus(capacity: 16);
        var rules = new List<StructuredChatRule>
        {
            new("r1", "Incoming",
                new List<string> { "Nave" },
                new List<string> { "inc" },
                MatchMode.ContainsAny)
        };
        var watcher = CreateWatcher(ocr, bus, rules);

        ocr.NextLines = new List<OcrLine>
        {
            new("[Nave] inc north", 0.3f, RectangleF.Empty), // below 0.5 threshold
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(2000);
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().BeEmpty("low confidence headers are dropped before assembly");
    }
```

Delete the three original `[Fact]` tests (`MatchingLineEmitsDetectionEvent`, `DuplicateLinesAreNotReEmitted`, `LowConfidenceLinesAreDropped`) — they're replaced above.

- [ ] **Step 2: Run tests — expect failures (old per-line pipeline still active or compile breaks)**

```
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~ChatWatcherTests" --nologo
```

Expected: some tests FAIL (notably the wrapped-two-line and deferred-one-tick cases) because the per-line pipeline cannot assemble a two-line [Game] message and fires the trailing message immediately.

- [ ] **Step 3: Add `LineRoles` to `ChatTickDebugInfo`**

In `src/GuildRelay.Features.Chat/ChatWatcher.cs`, modify the class body near the existing init-only properties:

```csharp
public sealed class ChatTickDebugInfo
{
    public byte[] CapturedImageBgra { get; init; } = Array.Empty<byte>();
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    public int ImageStride { get; init; }
    public List<string> OcrLines { get; init; } = new();
    public List<string> NormalizedLines { get; init; } = new();
    public List<string> ParsedChannels { get; init; } = new();
    public List<string> LineRoles { get; init; } = new();  // NEW: "HEADER" | "CONT" | "SKIP"
    public List<string> MatchResults { get; init; } = new();
    public DateTimeOffset Timestamp { get; init; }
}
```

- [ ] **Step 4: Rewrite `ProcessOneTickAsync` and add `_deferredTrailing`**

Replace the body of `ChatWatcher` (keeping constructor, public surface, and events) with the following. The critical changes: new field, new reset, rewritten `ProcessOneTickAsync`.

Add the field alongside `_cooldown`:

```csharp
    private AssembledMessage? _deferredTrailing;
```

Replace `StartAsync` body:

```csharp
    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _dedup.Clear();
        _cooldown.Reset();
        _deferredTrailing = null;
        Status = FeatureStatus.Running;
        _ = Task.Run(() => CaptureLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }
```

Replace `ProcessOneTickAsync` with:

```csharp
    private async Task ProcessOneTickAsync(CancellationToken ct)
    {
        if (_config.Region.IsEmpty) return;

        var rect = new Rectangle(_config.Region.X, _config.Region.Y,
            _config.Region.Width, _config.Region.Height);

        using var raw = _capture.CaptureRegion(rect);
        using var preprocessed = _pipeline.Apply(raw);

        var ocrResult = await _ocr.RecognizeAsync(
            preprocessed.BgraPixels,
            preprocessed.Width,
            preprocessed.Height,
            preprocessed.Stride,
            ct).ConfigureAwait(false);

        var matcher = _matcher;

        var debug = DebugTick is not null ? new ChatTickDebugInfo
        {
            CapturedImageBgra = (byte[])raw.BgraPixels.Clone(),
            ImageWidth = raw.Width,
            ImageHeight = raw.Height,
            ImageStride = raw.Stride,
            Timestamp = DateTimeOffset.UtcNow
        } : null;

        // Build assembler input: normalize each OCR line.
        var inputs = new List<OcrLineInput>(ocrResult.Lines.Count);
        foreach (var line in ocrResult.Lines)
        {
            var normalized = TextNormalizer.Normalize(line.Text);
            if (string.IsNullOrEmpty(normalized)) continue;
            inputs.Add(new OcrLineInput(normalized, line.Text, line.Confidence));

            if (debug is not null)
            {
                debug.OcrLines.Add(line.Text);
                debug.NormalizedLines.Add(normalized);
                bool skip = line.Confidence < _config.OcrConfidenceThreshold;
                bool header = !skip && ChatLineParser.IsHeader(normalized);
                debug.LineRoles.Add(skip ? "SKIP" : header ? "HEADER" : "CONT");
                var parsed = ChatLineParser.Parse(normalized);
                debug.ParsedChannels.Add(parsed.Channel ?? "—");
            }
        }

        var assembly = ChatMessageAssembler.Assemble(inputs, _config.OcrConfidenceThreshold);
        var (toEmit, newBuffer) = DeferredTrailing.Resolve(_deferredTrailing, assembly);

        // Diagnostics: anything held in the new buffer is "deferred" this tick.
        if (debug is not null && newBuffer is not null)
            debug.MatchResults.Add(
                $"DEFERRED [rows {newBuffer.StartRow}-{newBuffer.EndRow}]: " +
                $"[{newBuffer.Channel ?? "—"}] {newBuffer.OriginalText}");

        // Track which emitted messages originated from the deferred buffer
        // (first toEmit element, when previous buffer was not resolved-by-completed).
        var previousWasEmittedFromBuffer =
            _deferredTrailing is not null &&
            toEmit.Count > 0 &&
            ReferenceEquals(toEmit[0], _deferredTrailing);

        _deferredTrailing = newBuffer;

        for (int i = 0; i < toEmit.Count; i++)
        {
            var msg = toEmit[i];
            var fromBuffer = (i == 0) && previousWasEmittedFromBuffer;
            var prefix = fromBuffer ? "EMITTED-DEFERRED" : "EMITTED";

            var dedupKey = $"{msg.Channel}|{msg.PlayerName}|{msg.Timestamp}|{msg.Body}";
            if (_dedup.IsDuplicate(dedupKey))
            {
                debug?.MatchResults.Add(
                    $"DEDUP [rows {msg.StartRow}-{msg.EndRow}]: {msg.OriginalText}");
                continue;
            }

            var parsed = msg.ToParsedChatLine();
            var match = matcher.FindMatch(parsed);
            if (match is null) continue;

            if (!_cooldown.TryFire(match.Rule.Id, TimeSpan.FromSeconds(match.Rule.CooldownSec)))
            {
                debug?.MatchResults.Add(
                    $"COOLDOWN [{match.Rule.Label}] rows {msg.StartRow}-{msg.EndRow}: " +
                    $"{msg.OriginalText}");
                continue;
            }

            debug?.MatchResults.Add(
                $"{prefix} [{match.Rule.Label}] rows {msg.StartRow}-{msg.EndRow}: " +
                $"{msg.OriginalText}");

            var evt = new DetectionEvent(
                FeatureId: "chat",
                RuleLabel: match.Rule.Label,
                MatchedContent: msg.OriginalText,
                TimestampUtc: DateTimeOffset.UtcNow,
                PlayerName: _playerName,
                Extras: new Dictionary<string, string>(),
                ImageAttachment: null);

            await _bus.PublishAsync(evt, ct).ConfigureAwait(false);
        }

        if (debug is not null &&
            (debug.OcrLines.Count > 0 || debug.MatchResults.Count > 0))
            DebugTick?.Invoke(debug);
    }
```

Remove the `ChatLineParser` loop / 1-2-3 join candidate loop that existed in the old `ProcessOneTickAsync` — the code above replaces it entirely.

Also remove the unused `using GuildRelay.Features.Chat.Preprocessing;` only if the compiler flags it as unused after the change; otherwise leave imports alone.

- [ ] **Step 5: Run tests — expect pass**

```
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~ChatWatcherTests" --nologo
```

Expected: all 5 PASS.

- [ ] **Step 6: Run full chat tests to catch regressions**

```
dotnet test tests/GuildRelay.Features.Chat.Tests --nologo
```

Expected: all PASS.

- [ ] **Step 7: Commit**

```
git add src/GuildRelay.Features.Chat/ChatWatcher.cs tests/GuildRelay.Features.Chat.Tests/ChatWatcherTests.cs
git commit -m "feat(chat): integrate message assembler + deferred trailing into ChatWatcher"
```

---

### Task 6: Live Debug View rendering updates

Use the new `LineRoles` field to distinguish HEADER / CONT / SKIP in the OCR output panel.

**Files:**
- Modify: `src/GuildRelay.App/Config/DebugLiveView.xaml.cs`

- [ ] **Step 1: Update `UpdateOcrOutput`**

Replace the body of `UpdateOcrOutput` in `DebugLiveView.xaml.cs` with:

```csharp
    private void UpdateOcrOutput(ChatTickDebugInfo info)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < info.OcrLines.Count; i++)
        {
            var role = i < info.LineRoles.Count ? info.LineRoles[i] : "?";
            var channel = i < info.ParsedChannels.Count ? info.ParsedChannels[i] : "?";
            var normalized = i < info.NormalizedLines.Count ? info.NormalizedLines[i] : "";

            string prefix = role switch
            {
                "HEADER" => $"[{channel}]  ",
                "CONT"   => "   \u21B3    ",   // ↳
                "SKIP"   => "(skip)  ",
                _        => "        "
            };

            sb.AppendLine($"{prefix}OCR: \"{info.OcrLines[i]}\"");
            sb.AppendLine($"         Norm: \"{normalized}\"");
        }
        if (info.OcrLines.Count == 0)
            sb.AppendLine("(no text detected)");

        OcrOutputBox.Text = sb.ToString();
    }
```

- [ ] **Step 2: Build and verify the App compiles**

```
dotnet build src/GuildRelay.App --nologo
```

Expected: success, 0 errors.

- [ ] **Step 3: Commit**

```
git add src/GuildRelay.App/Config/DebugLiveView.xaml.cs
git commit -m "feat(chat): show header/cont roles and per-message diagnostics in live view"
```

---

### Task 7: Full verification

- [ ] **Step 1: Full test suite**

```
dotnet test --nologo
```

Expected: all tests PASS (baseline before this plan: 109).

- [ ] **Step 2: Full build**

```
dotnet build --nologo
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Commit any residual cleanup**

```
git status
```

If clean, skip. Otherwise commit with `chore: cleanup after chat assembler integration`.

---

## Acceptance

- `ChatMessageAssembler` groups OCR lines into messages by header detection.
- `DeferredTrailing` holds the last (terminator-less) message for one tick before emitting it.
- `ChatWatcher` no longer uses the 1/2/3-line join candidate loop; each assembled message is matched exactly once.
- `ChatTickDebugInfo` carries `LineRoles` and `MatchResults` is enriched with `DEFERRED`, `EMITTED-DEFERRED`, and row-range annotations.
- The Live Debug View OCR panel distinguishes HEADER vs CONT vs SKIP lines.
- All existing tests pass; seven new test classes cover the new behaviour.
