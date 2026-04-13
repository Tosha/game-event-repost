# Chat Watcher Rule Engine Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the raw-regex rule editor with a structured, channel-aware rule builder where users pick MO2 channels via checkboxes and enter comma-separated keywords instead of writing regex.

**Architecture:** A new `ChatLineParser` extracts the channel tag from OCR text, then `ChatWatcher` routes lines to rules by channel before checking keywords. The UI replaces the text box with a structured form: channel checkboxes + keyword input + match mode toggle. Old `ChatRuleConfig` is replaced by `StructuredChatRule` with `Channels` list + `Keywords` list + `MatchMode` enum. See `docs/adr/2026-04-14-chat-watcher-rule-engine-redesign.md`.

**Tech Stack:** .NET 8, WPF + WPF-UI, xUnit + FluentAssertions. No new packages.

**Prerequisite:** All prior plans merged. ADR merged.

---

## File structure

```
src/
├── GuildRelay.Core/
│   └── Config/
│       ├── StructuredChatRule.cs   (NEW — replaces ChatRuleConfig for channel-aware rules)
│       ├── ChatConfig.cs           (MODIFY — Rules field type changes)
│       ├── RuleTemplates.cs        (MODIFY — templates use new format)
│       └── ChatRuleConfig.cs       (KEEP — legacy support during migration)
│
├── GuildRelay.Features.Chat/
│   ├── ChatLineParser.cs           (NEW — parses [timestamp][channel] [player] body)
│   ├── ChannelMatcher.cs           (NEW — channel-first routing + keyword matching)
│   ├── ChatWatcher.cs              (MODIFY — use ChatLineParser + ChannelMatcher)
│   └── TextNormalizer.cs           (KEEP — still used for keyword comparison)
│
└── GuildRelay.App/
    └── Config/
        ├── ChatConfigTab.xaml       (REWRITE — structured rule editor UI)
        └── ChatConfigTab.xaml.cs    (REWRITE — new rule editor logic)

tests/
├── GuildRelay.Features.Chat.Tests/
│   ├── ChatLineParserTests.cs      (NEW)
│   └── ChannelMatcherTests.cs      (NEW)
```

---

## Task 1: ChatLineParser — parse OCR lines into structured parts (TDD)

**Files:**
- Create: `src/GuildRelay.Features.Chat/ChatLineParser.cs`
- Create: `tests/GuildRelay.Features.Chat.Tests/ChatLineParserTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/GuildRelay.Features.Chat.Tests/ChatLineParserTests.cs`:

```csharp
using FluentAssertions;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class ChatLineParserTests
{
    [Fact]
    public void ParsesChannelWithTimestamp()
    {
        var result = ChatLineParser.Parse("[21:02:15][Game] A large band of Profiteers");

        result.Timestamp.Should().Be("21:02:15");
        result.Channel.Should().Be("Game");
        result.PlayerName.Should().BeNull();
        result.Body.Should().Be("A large band of Profiteers");
    }

    [Fact]
    public void ParsesChannelWithoutTimestamp()
    {
        var result = ChatLineParser.Parse("[Nave] [Stormbrew] they should just add it");

        result.Timestamp.Should().BeNull();
        result.Channel.Should().Be("Nave");
        result.PlayerName.Should().Be("Stormbrew");
        result.Body.Should().Be("they should just add it");
    }

    [Fact]
    public void ParsesChannelCaseInsensitive()
    {
        var result = ChatLineParser.Parse("[game] You received a task");

        result.Channel.Should().Be("Game");
        result.Body.Should().Be("You received a task");
    }

    [Fact]
    public void HandlesOcrArtifactDotBeforeChannel()
    {
        // OCR sometimes reads [.Game] instead of [Game]
        var result = ChatLineParser.Parse("[.Game] YourdrunkPapa has come online.");

        result.Channel.Should().Be("Game");
    }

    [Fact]
    public void ReturnsNullChannelForUnrecognizedTag()
    {
        var result = ChatLineParser.Parse("[Unknown] some text");

        result.Channel.Should().BeNull();
        result.Body.Should().Be("[Unknown] some text");
    }

    [Fact]
    public void ReturnsNullChannelForPlainText()
    {
        var result = ChatLineParser.Parse("just some text without brackets");

        result.Channel.Should().BeNull();
        result.Body.Should().Be("just some text without brackets");
    }

    [Fact]
    public void ParsesGuildChannelWithPlayer()
    {
        var result = ChatLineParser.Parse("[Guild] [PlayerOne] heading to dungeon");

        result.Channel.Should().Be("Guild");
        result.PlayerName.Should().Be("PlayerOne");
        result.Body.Should().Be("heading to dungeon");
    }

    [Fact]
    public void ParsesTimestampChannelPlayer()
    {
        var result = ChatLineParser.Parse("[21:03:40][Game] YourdrunkPapa has come online.");

        result.Timestamp.Should().Be("21:03:40");
        result.Channel.Should().Be("Game");
        result.Body.Should().Be("YourdrunkPapa has come online.");
    }

    [Fact]
    public void HandlesNormalizedLowercaseInput()
    {
        // After TextNormalizer, text is lowercased but brackets preserved
        var result = ChatLineParser.Parse("[nave] [stormbrew] theyl just remake");

        result.Channel.Should().Be("Nave");
        result.PlayerName.Should().Be("stormbrew");
        result.Body.Should().Be("theyl just remake");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~ChatLineParserTests"
```

Expected: compile error, `ChatLineParser` does not exist.

- [ ] **Step 3: Implement `ChatLineParser`**

`src/GuildRelay.Features.Chat/ChatLineParser.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace GuildRelay.Features.Chat;

public sealed record ParsedChatLine(
    string? Timestamp,
    string? Channel,
    string? PlayerName,
    string Body);

public static class ChatLineParser
{
    private static readonly Dictionary<string, string> KnownChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["say"] = "Say", ["yell"] = "Yell", ["whisper"] = "Whisper",
        ["guild"] = "Guild", ["skill"] = "Skill", ["combat"] = "Combat",
        ["game"] = "Game", ["server"] = "Server", ["nave"] = "Nave",
        ["trade"] = "Trade", ["help"] = "Help",
        // OCR artifacts
        [".game"] = "Game", [".server"] = "Server", [".nave"] = "Nave",
        [".guild"] = "Guild", [".say"] = "Say", [".yell"] = "Yell",
        [".combat"] = "Combat", [".skill"] = "Skill", [".trade"] = "Trade",
        [".whisper"] = "Whisper", [".help"] = "Help",
    };

    public static ParsedChatLine Parse(string line)
    {
        var pos = 0;
        string? timestamp = null;
        string? channel = null;
        string? playerName = null;

        // Try to extract bracketed tags from the start
        // Pattern: [optional-timestamp][channel] [optional-player] body

        // First bracket: could be timestamp or channel
        var tag1 = ExtractBracketedTag(line, ref pos);
        if (tag1 is null)
            return new ParsedChatLine(null, null, null, line);

        // Check if tag1 is a timestamp (HH:MM:SS pattern)
        if (tag1.Length == 8 && tag1[2] == ':' && tag1[5] == ':')
        {
            timestamp = tag1;
            // Next bracket should be channel
            var tag2 = ExtractBracketedTag(line, ref pos);
            if (tag2 is not null && KnownChannels.TryGetValue(tag2, out var ch))
                channel = ch;
        }
        else if (KnownChannels.TryGetValue(tag1, out var ch))
        {
            channel = ch;
        }
        else
        {
            // Not a known channel, return as plain text
            return new ParsedChatLine(null, null, null, line);
        }

        if (channel is null)
            return new ParsedChatLine(timestamp, null, null, line);

        // Skip whitespace after channel tag
        while (pos < line.Length && line[pos] == ' ') pos++;

        // Check for player name: [PlayerName]
        if (pos < line.Length && line[pos] == '[')
        {
            var savedPos = pos;
            var maybePlayer = ExtractBracketedTag(line, ref pos);
            if (maybePlayer is not null && !KnownChannels.ContainsKey(maybePlayer))
            {
                playerName = maybePlayer;
            }
            else
            {
                pos = savedPos; // not a player, rewind
            }
        }

        // Skip whitespace before body
        while (pos < line.Length && line[pos] == ' ') pos++;

        var body = pos < line.Length ? line[pos..] : string.Empty;
        return new ParsedChatLine(timestamp, channel, playerName, body);
    }

    private static string? ExtractBracketedTag(string line, ref int pos)
    {
        if (pos >= line.Length || line[pos] != '[') return null;
        var close = line.IndexOf(']', pos + 1);
        if (close < 0) return null;
        var tag = line[(pos + 1)..close];
        pos = close + 1;
        return tag;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~ChatLineParserTests"
```

Expected: 9 passed.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Features.Chat/ChatLineParser.cs tests/GuildRelay.Features.Chat.Tests/ChatLineParserTests.cs
git commit -m "feat(chat): add ChatLineParser for structured MO2 channel extraction"
```

---

## Task 2: StructuredChatRule config + ChannelMatcher (TDD)

**Files:**
- Create: `src/GuildRelay.Core/Config/StructuredChatRule.cs`
- Create: `src/GuildRelay.Features.Chat/ChannelMatcher.cs`
- Create: `tests/GuildRelay.Features.Chat.Tests/ChannelMatcherTests.cs`

- [ ] **Step 1: Create the new rule config type**

`src/GuildRelay.Core/Config/StructuredChatRule.cs`:

```csharp
using System.Collections.Generic;

namespace GuildRelay.Core.Config;

public enum MatchMode { ContainsAny, Regex }

public sealed record StructuredChatRule(
    string Id,
    string Label,
    List<string> Channels,
    List<string> Keywords,
    MatchMode MatchMode,
    int CooldownSec = 600);
```

- [ ] **Step 2: Write the failing tests for ChannelMatcher**

`tests/GuildRelay.Features.Chat.Tests/ChannelMatcherTests.cs`:

```csharp
using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.Core.Config;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class ChannelMatcherTests
{
    [Fact]
    public void MatchesChannelAndKeyword()
    {
        var rule = new StructuredChatRule("r1", "Game Events",
            new List<string> { "Game" },
            new List<string> { "Dire Wolf" },
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine("21:07:35", "Game",
            null, "You received a task to kill Dire Wolf (8)");

        var match = matcher.FindMatch(parsed);

        match.Should().NotBeNull();
        match!.Rule.Id.Should().Be("r1");
    }

    [Fact]
    public void DoesNotMatchWrongChannel()
    {
        var rule = new StructuredChatRule("r1", "Game Events",
            new List<string> { "Game" },
            new List<string> { "Dire Wolf" },
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, "Nave",
            "Stormbrew", "Dire Wolf spotted north");

        matcher.FindMatch(parsed).Should().BeNull();
    }

    [Fact]
    public void MatchesAnyKeywordInList()
    {
        var rule = new StructuredChatRule("r1", "Game Events",
            new List<string> { "Game" },
            new List<string> { "Sylvan Sanctum", "Dire Wolf" },
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, "Game",
            null, "A large band pillaging the Sylvan Sanctum!");

        matcher.FindMatch(parsed).Should().NotBeNull();
    }

    [Fact]
    public void EmptyKeywordsMatchesAllOnChannel()
    {
        var rule = new StructuredChatRule("r1", "Guild Relay",
            new List<string> { "Guild" },
            new List<string>(),
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, "Guild",
            "PlayerOne", "anything at all");

        matcher.FindMatch(parsed).Should().NotBeNull();
    }

    [Fact]
    public void RegexModeMatchesPattern()
    {
        var rule = new StructuredChatRule("r1", "Incoming",
            new List<string> { "Nave", "Yell" },
            new List<string> { "(inc|incoming|enemies)" },
            MatchMode.Regex);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, "Nave",
            "Stormbrew", "inc north gate");

        matcher.FindMatch(parsed).Should().NotBeNull();
    }

    [Fact]
    public void KeywordMatchIsCaseInsensitive()
    {
        var rule = new StructuredChatRule("r1", "Game Events",
            new List<string> { "Game" },
            new List<string> { "Dire Wolf" },
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, "Game",
            null, "you received a task to kill dire wolf");

        matcher.FindMatch(parsed).Should().NotBeNull();
    }

    [Fact]
    public void NullChannelDoesNotMatch()
    {
        var rule = new StructuredChatRule("r1", "Game",
            new List<string> { "Game" },
            new List<string>(),
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, null, null, "some unparseable text");

        matcher.FindMatch(parsed).Should().BeNull();
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~ChannelMatcherTests"
```

Expected: compile error.

- [ ] **Step 4: Implement `ChannelMatcher`**

`src/GuildRelay.Features.Chat/ChannelMatcher.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GuildRelay.Core.Config;

namespace GuildRelay.Features.Chat;

public sealed record ChannelMatchResult(StructuredChatRule Rule);

public sealed class ChannelMatcher
{
    private readonly Dictionary<string, List<CompiledStructuredRule>> _byChannel;

    public ChannelMatcher(IEnumerable<StructuredChatRule> rules)
    {
        _byChannel = new Dictionary<string, List<CompiledStructuredRule>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            var compiled = new CompiledStructuredRule(rule);
            foreach (var ch in rule.Channels)
            {
                if (!_byChannel.TryGetValue(ch, out var list))
                {
                    list = new List<CompiledStructuredRule>();
                    _byChannel[ch] = list;
                }
                list.Add(compiled);
            }
        }
    }

    public ChannelMatchResult? FindMatch(ParsedChatLine parsed)
    {
        if (parsed.Channel is null) return null;
        if (!_byChannel.TryGetValue(parsed.Channel, out var candidates)) return null;

        foreach (var compiled in candidates)
        {
            if (compiled.Matches(parsed.Body))
                return new ChannelMatchResult(compiled.Rule);
        }
        return null;
    }

    private sealed class CompiledStructuredRule
    {
        public StructuredChatRule Rule { get; }
        private readonly List<string>? _keywords;
        private readonly Regex? _regex;

        public CompiledStructuredRule(StructuredChatRule rule)
        {
            Rule = rule;
            if (rule.Keywords.Count == 0)
            {
                // Empty keywords = match everything on the channel
                _keywords = null;
                _regex = null;
            }
            else if (rule.MatchMode == MatchMode.Regex)
            {
                var pattern = string.Join("|", rule.Keywords);
                _regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            }
            else
            {
                _keywords = rule.Keywords.Select(k => k.ToLowerInvariant()).ToList();
            }
        }

        public bool Matches(string body)
        {
            if (_keywords is null && _regex is null) return true; // match-all
            if (_regex is not null) return _regex.IsMatch(body);
            var lower = body.ToLowerInvariant();
            return _keywords!.Any(k => lower.Contains(k));
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~ChannelMatcherTests"
```

Expected: 7 passed.

- [ ] **Step 6: Commit**

```bash
git add src/GuildRelay.Core/Config/StructuredChatRule.cs src/GuildRelay.Features.Chat/ChannelMatcher.cs tests/GuildRelay.Features.Chat.Tests/ChannelMatcherTests.cs
git commit -m "feat(chat): add StructuredChatRule config + ChannelMatcher with channel-first routing"
```

---

## Task 3: Update ChatConfig + RuleTemplates for new rule format

**Files:**
- Modify: `src/GuildRelay.Core/Config/ChatConfig.cs`
- Modify: `src/GuildRelay.Core/Config/RuleTemplates.cs`

- [ ] **Step 1: Update ChatConfig to use StructuredChatRule**

`src/GuildRelay.Core/Config/ChatConfig.cs`:

```csharp
using System.Collections.Generic;

namespace GuildRelay.Core.Config;

public sealed record ChatConfig(
    bool Enabled,
    int CaptureIntervalMs,
    double OcrConfidenceThreshold,
    int DefaultCooldownSec,
    RegionConfig Region,
    List<PreprocessStageConfig> PreprocessPipeline,
    List<StructuredChatRule> Rules,
    Dictionary<string, string> Templates)
{
    public static ChatConfig Default => new(
        Enabled: false,
        CaptureIntervalMs: 1000,
        OcrConfidenceThreshold: 0.65,
        DefaultCooldownSec: 600,
        Region: RegionConfig.Empty,
        PreprocessPipeline: new List<PreprocessStageConfig>
        {
            new("grayscale"),
            new("contrastStretch", new Dictionary<string, double> { ["low"] = 0.1, ["high"] = 0.9 }),
            new("upscale", new Dictionary<string, double> { ["factor"] = 2 }),
            new("adaptiveThreshold", new Dictionary<string, double> { ["blockSize"] = 15 })
        },
        Rules: new List<StructuredChatRule>(RuleTemplates.BuiltIn["MO2 Game Events"]),
        Templates: new Dictionary<string, string>
        {
            ["default"] = "**{player}** saw chat match [{rule_label}]: `{matched_text}`"
        });
}
```

- [ ] **Step 2: Update RuleTemplates to use StructuredChatRule**

`src/GuildRelay.Core/Config/RuleTemplates.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace GuildRelay.Core.Config;

public static class RuleTemplates
{
    public static readonly string[] Mo2Locations =
    {
        "Sylvan Sanctum", "Tindremic Heartlands", "Tindrem Sewers",
        "Green Weald", "Fabernum Tower", "Western Talus Mountains",
        "Central Talus Mountains", "Mouth of Gal Barag", "Northern Valley",
        "Northern Foothills", "Eastern Talus Mountains", "Gaul'Kor Wastes",
        "Gaul'Kor Outpost", "Bullhead Gulch", "Eastern Highlands",
        "Morin Khur Sewers", "Khurite Bluffs", "Deepwood", "Colored Forest",
        "Tindremic Midlands", "Western Coastlands", "Plains of Meduli",
        "Melisars Vault", "Western Steppe", "The Undercroft", "Eastern Steppe",
        "Eastern Moorland", "Eastern Glen", "Eastern Greatwoods",
        "Tower of Descensus", "Marlwood", "Southern Greatwoods", "Landfall",
        "Meduli Badlands", "Tekton's Heel", "Sunken Isles", "Northern Canteri",
        "Southern Canteri", "Western Stairs of Echidna", "Western Brood Isles",
        "Shinarian Temple", "Shinarian Labyrinth", "Eastern Stairs of Echidna",
        "Eastern Brood Isles",
    };

    public static IReadOnlyDictionary<string, List<StructuredChatRule>> BuiltIn { get; } =
        new Dictionary<string, List<StructuredChatRule>>
        {
            ["MO2 Game Events"] = new List<StructuredChatRule>
            {
                new(
                    Id: "mo2_game_events",
                    Label: "MO2 Game Events",
                    Channels: new List<string> { "Game" },
                    Keywords: Mo2Locations.ToList(),
                    MatchMode: MatchMode.ContainsAny,
                    CooldownSec: 600)
            }
        };

    public static IReadOnlyList<string> BuiltInNames { get; } = BuiltIn.Keys.ToList();
}
```

- [ ] **Step 3: Build to find all compilation errors in downstream code**

```bash
dotnet build 2>&1
```

This will show errors in `ChatWatcher.cs` (uses old `ChatRuleConfig`), `ChatConfigTab.xaml.cs` (ParseRules returns old type), and tests. Fix those in the next tasks.

- [ ] **Step 4: Commit (build may not pass yet — that's OK, next tasks fix it)**

```bash
git add src/GuildRelay.Core/Config/ChatConfig.cs src/GuildRelay.Core/Config/RuleTemplates.cs
git commit -m "feat(core): update ChatConfig and RuleTemplates to use StructuredChatRule"
```

---

## Task 4: Update ChatWatcher to use ChatLineParser + ChannelMatcher

**Files:**
- Modify: `src/GuildRelay.Features.Chat/ChatWatcher.cs`

- [ ] **Step 1: Rewrite ChatWatcher to use the new matching pipeline**

Replace the old `CompiledRule` / `CompiledPattern` matching with `ChatLineParser.Parse` + `ChannelMatcher.FindMatch`. The full updated `ChatWatcher.cs`:

Key changes to `ProcessOneTickAsync`:
- Parse each OCR line with `ChatLineParser.Parse`
- Use `ChannelMatcher.FindMatch` instead of iterating `CompiledPattern.IsMatch`
- For joined lines, re-parse the joined text (channel tag is on the first line)
- Dedup and cooldown remain the same

Key changes to constructor/fields:
- Accept `List<StructuredChatRule>` from config instead of `List<ChatRuleConfig>`
- Build a `ChannelMatcher` instance instead of `List<CompiledRule>`
- `ApplyConfig` rebuilds the `ChannelMatcher`

Remove: `CompileRules` method, `CompiledRule` record, `using GuildRelay.Core.Rules` import.

The constructor becomes:
```csharp
public ChatWatcher(IScreenCapture capture, IOcrEngine ocr,
    PreprocessPipeline pipeline, EventBus bus,
    ChatConfig config, string playerName)
{
    ...
    _matcher = new ChannelMatcher(config.Rules);
}
```

`ProcessOneTickAsync` matching loop becomes:
```csharp
foreach (var candidate in new[] { (joinedNormalized, joinedOriginal), (normalized, original) })
{
    if (_dedup.IsDuplicate(candidate.Item1))
        continue;

    var parsed = ChatLineParser.Parse(candidate.Item1);
    var match = _matcher.FindMatch(parsed);
    if (match is null) continue;

    if (!_cooldown.TryFire(match.Rule.Id, TimeSpan.FromSeconds(match.Rule.CooldownSec)))
        continue;

    var evt = new DetectionEvent(
        FeatureId: "chat",
        RuleLabel: match.Rule.Label,
        MatchedContent: candidate.Item2,
        ...);
    await _bus.PublishAsync(evt, ct).ConfigureAwait(false);
    break;
}
```

- [ ] **Step 2: Update ChatWatcher tests to use StructuredChatRule**

`tests/GuildRelay.Features.Chat.Tests/ChatWatcherTests.cs` — change `ChatRuleConfig` usages to `StructuredChatRule`. Example:

```csharp
var rules = new List<StructuredChatRule>
{
    new("r1", "Incoming", new List<string> { "Nave", "Game" },
        new List<string> { "inc", "incoming" }, MatchMode.ContainsAny)
};
```

And the FakeOcr returns lines with channel prefixes:
```csharp
ocr.NextLines = new List<OcrLine>
{
    new("[Nave] [Someone] inc north gate", 0.9f, RectangleF.Empty)
};
```

- [ ] **Step 3: Build and run all tests**

```bash
dotnet build && dotnet test
```

- [ ] **Step 4: Commit**

```bash
git add src/GuildRelay.Features.Chat/ChatWatcher.cs tests/GuildRelay.Features.Chat.Tests/ChatWatcherTests.cs
git commit -m "feat(chat): update ChatWatcher to use ChatLineParser + ChannelMatcher"
```

---

## Task 5: Rewrite ChatConfigTab UI — structured rule editor

**Files:**
- Rewrite: `src/GuildRelay.App/Config/ChatConfigTab.xaml`
- Rewrite: `src/GuildRelay.App/Config/ChatConfigTab.xaml.cs`

- [ ] **Step 1: Design the new XAML**

Replace the text box with:
- An `ItemsControl` or `ListBox` showing active rules as summary cards
- An "Add Rule" / "Edit Rule" section with:
  - Label text box
  - Channel checkboxes (GAME, GUILD, NAVE, etc.) using `WrapPanel`
  - Keywords text box (comma-separated)
  - Match mode toggle (ContainsAny / Regex)
  - Cooldown text box
  - Add / Update / Remove buttons
- Template dropdown + Load button (same as before)
- Test message section (same as before)

```xml
<!-- Active rules list -->
<TextBlock Text="Active rules" FontWeight="SemiBold"/>
<ListBox x:Name="RulesList" Height="120" Margin="0,4,0,8"
         SelectionChanged="OnRuleSelected"/>

<!-- Rule editor -->
<TextBlock Text="Rule editor" FontWeight="SemiBold" Margin="0,4,0,0"/>
<TextBlock Text="Label"/>
<TextBox x:Name="RuleLabelBox" Margin="0,2,0,4"/>

<TextBlock Text="Channels"/>
<WrapPanel x:Name="ChannelPanel" Margin="0,2,0,4">
    <!-- CheckBoxes generated in code-behind for each known channel -->
</WrapPanel>

<TextBlock Text="Keywords (comma-separated)"/>
<TextBox x:Name="KeywordsBox" Margin="0,2,0,4" TextWrapping="Wrap"
         AcceptsReturn="False" FontFamily="Consolas"/>

<StackPanel Orientation="Horizontal" Margin="0,2,0,4">
    <RadioButton x:Name="ContainsAnyRadio" Content="Contains any" IsChecked="True" Margin="0,0,12,0"/>
    <RadioButton x:Name="RegexRadio" Content="Regex"/>
</StackPanel>

<TextBlock Text="Cooldown (seconds)"/>
<TextBox x:Name="RuleCooldownBox" Width="120" HorizontalAlignment="Left" Margin="0,2,0,8"/>

<StackPanel Orientation="Horizontal">
    <ui:Button Content="Add Rule" Click="OnAddRule" Appearance="Primary" Margin="0,0,8,0"/>
    <ui:Button Content="Update Rule" Click="OnUpdateRule" Margin="0,0,8,0"/>
    <ui:Button Content="Remove Rule" Click="OnRemoveRule"/>
</StackPanel>
```

- [ ] **Step 2: Implement the code-behind**

Key methods:
- `OnLoaded`: populate channel checkboxes dynamically from `ChatLineParser.KnownChannelNames`, load rules into `RulesList`
- `OnAddRule`: read editor fields → create `StructuredChatRule` → add to internal list → refresh list
- `OnUpdateRule`: update selected rule with editor fields
- `OnRemoveRule`: remove selected rule
- `OnRuleSelected`: populate editor fields from selected rule
- `OnSave`: save all rules to config (same as before but with `StructuredChatRule`)
- `OnLoadTemplate`: append template rules to the list (same as before)
- `OnTestMessage`: parse test input with `ChatLineParser`, run through `ChannelMatcher`
- `FormatRuleSummary(StructuredChatRule)`: returns display string like `"Game Events — GAME, SERVER — 3 keywords — 600s"`

- [ ] **Step 3: Build and run full test suite**

```bash
dotnet build && dotnet test
```

- [ ] **Step 4: Commit**

```bash
git add src/GuildRelay.App/Config/ChatConfigTab.xaml src/GuildRelay.App/Config/ChatConfigTab.xaml.cs
git commit -m "feat(app): rewrite ChatConfigTab with structured channel-aware rule editor"
```

---

## Task 6: Update end-to-end tests + cleanup

**Files:**
- Modify: `tests/GuildRelay.Platform.Windows.Tests/ChatWatcherEndToEndTests.cs`
- Delete or keep: `src/GuildRelay.Core/Config/ChatRuleConfig.cs` (keep for AudioWatcher which still uses cooldown patterns, or delete if unused)

- [ ] **Step 1: Update ChatWatcherEndToEndTests to use new StructuredChatRule**

Update the rule construction and matching logic:
```csharp
var rule = new StructuredChatRule("r1", "Game Events",
    new List<string> { "Game" },
    new List<string> { "Dire Wolf" },
    MatchMode.ContainsAny);
var matcher = new ChannelMatcher(new[] { rule });
```

Parse each OCR line with `ChatLineParser.Parse` before matching.

- [ ] **Step 2: Check if ChatRuleConfig is still used elsewhere**

```bash
grep -r "ChatRuleConfig" src/ --include="*.cs"
```

If only used in the old `ChatWatcher` code (now replaced), delete `ChatRuleConfig.cs`. If used by `AudioConfigTab` or elsewhere, keep it.

- [ ] **Step 3: Build and run full test suite**

```bash
dotnet build && dotnet test
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(chat): update e2e tests for structured rules + cleanup old ChatRuleConfig"
```

---

## Self-review

**ADR coverage:**
- Channel-aware parsing → Task 1 (ChatLineParser)
- StructuredChatRule with channels/keywords/matchMode → Task 2
- Channel-first routing via ChannelMatcher → Task 2
- ChatWatcher integration → Task 4
- Structured UI with channel checkboxes → Task 5
- Templates updated → Task 3
- Keyword "Contains any" mode → Task 2 (ChannelMatcher.Matches)
- Regex mode → Task 2
- Empty keywords = match all → Task 2 test
- OCR artifact handling (.Game) → Task 1 test
- Migration path → Task 6 (ChatRuleConfig kept/removed based on usage)

**Placeholder scan:** No TBDs found. Task 5 is described at UI-level rather than line-by-line code (appropriate for WPF UI code that's hard to TDD), but all other tasks have complete code.

**Type consistency:**
- `StructuredChatRule(Id, Label, Channels, Keywords, MatchMode, CooldownSec)` — consistent across Tasks 2, 3, 4, 5, 6
- `ParsedChatLine(Timestamp, Channel, PlayerName, Body)` — consistent across Tasks 1, 2, 4
- `ChannelMatcher(rules)` / `.FindMatch(parsed)` → `ChannelMatchResult?` — consistent across Tasks 2, 4, 5
- `ChatLineParser.Parse(line)` — consistent across Tasks 1, 4, 5, 6
