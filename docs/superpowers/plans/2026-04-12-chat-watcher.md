# Chat Watcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Chat Watcher feature — periodic screen-capture OCR of a user-marked chat region, with preprocessing, dedup, rule matching, and Discord posting — completing the first v1 detection feature end-to-end.

**Architecture:** Chat Watcher runs on a `PeriodicTimer`, captures a screen region via `IScreenCapture` (BitBlt), runs a configurable preprocess pipeline, OCRs via `IOcrEngine` (Windows.Media.Ocr), deduplicates via an LRU hash set, matches against compiled rules (plain text or regex), and emits `DetectionEvent`s to the `EventBus`. All Windows APIs live in `GuildRelay.Platform.Windows`; the feature logic in `GuildRelay.Features.Chat` depends only on Core interfaces. See architecture spec §5.

**Tech Stack:** .NET 8, `System.Drawing.Common` (GDI+ for BitBlt and preprocess), `Windows.Media.Ocr` (UWP OCR), xUnit + FluentAssertions for tests.

**Prerequisite:** Foundations plan (Plan 1) must be complete and merged. The solution, Core contracts, Publisher, Logging, and App shell already exist.

**Definition of done:**
- `dotnet build -c Release` succeeds with zero warnings.
- `dotnet test` passes all existing tests plus all new Chat Watcher tests.
- Running the app: Chat Watcher tab appears in config. User can pick a region, add a rule, enable the feature, and see matching chat lines posted to Discord.
- Region picker overlay works in borderless/windowed mode.
- DPI drift detection puts the feature into Warning status when resolution changes.

---

## File structure

This plan creates the following new files and modifies a few existing ones:

```
src/
├── GuildRelay.Core/
│   ├── Capture/
│   │   ├── IScreenCapture.cs          (NEW — interface)
│   │   └── CapturedFrame.cs           (NEW — value type)
│   ├── Ocr/
│   │   ├── IOcrEngine.cs              (NEW — interface)
│   │   ├── OcrResult.cs               (NEW — value type)
│   │   └── OcrLine.cs                 (NEW — value type)
│   ├── Rules/
│   │   └── CompiledPattern.cs         (NEW — shared with Status Watcher)
│   └── Config/
│       ├── ChatConfig.cs              (NEW — chat feature config DTO)
│       ├── RegionConfig.cs            (NEW — shared region DTO)
│       ├── PreprocessStageConfig.cs   (NEW)
│       └── AppConfig.cs              (MODIFY — add ChatConfig)
│
├── GuildRelay.Platform.Windows/       (NEW project)
│   ├── GuildRelay.Platform.Windows.csproj
│   ├── Capture/
│   │   └── BitBltCapture.cs           (IScreenCapture impl)
│   ├── Ocr/
│   │   └── WindowsMediaOcrEngine.cs   (IOcrEngine impl)
│   └── Dpi/
│       └── DpiHelper.cs
│
├── GuildRelay.Features.Chat/          (NEW project)
│   ├── GuildRelay.Features.Chat.csproj
│   ├── ChatWatcher.cs                 (IFeature impl)
│   ├── Preprocessing/
│   │   ├── IPreprocessStage.cs
│   │   ├── PreprocessPipeline.cs
│   │   ├── GrayscaleStage.cs
│   │   ├── ContrastStretchStage.cs
│   │   ├── UpscaleStage.cs
│   │   └── AdaptiveThresholdStage.cs
│   ├── ChatDedup.cs
│   └── TextNormalizer.cs
│
└── GuildRelay.App/
    ├── CoreHost.cs                    (MODIFY — register ChatWatcher)
    ├── Config/
    │   ├── ChatConfigTab.xaml         (NEW)
    │   ├── ChatConfigTab.xaml.cs      (NEW)
    │   ├── ConfigWindow.xaml          (MODIFY — add tab)
    │   └── ConfigWindow.xaml.cs       (MODIFY)
    └── RegionPicker/
        ├── RegionPickerWindow.xaml     (NEW)
        └── RegionPickerWindow.xaml.cs  (NEW)

tests/
├── GuildRelay.Core.Tests/
│   └── Rules/
│       └── CompiledPatternTests.cs    (NEW)
├── GuildRelay.Features.Chat.Tests/    (NEW project)
│   ├── GuildRelay.Features.Chat.Tests.csproj
│   ├── ChatDedupTests.cs
│   ├── TextNormalizerTests.cs
│   ├── PreprocessPipelineTests.cs
│   └── ChatWatcherTests.cs
└── GuildRelay.Platform.Windows.Tests/ (NEW project — integration only)
    └── GuildRelay.Platform.Windows.Tests.csproj
```

---

## Task 1: Project scaffold — Platform.Windows, Features.Chat, test projects

**Files:**
- Create: `src/GuildRelay.Platform.Windows/GuildRelay.Platform.Windows.csproj`
- Create: `src/GuildRelay.Features.Chat/GuildRelay.Features.Chat.csproj`
- Create: `tests/GuildRelay.Features.Chat.Tests/GuildRelay.Features.Chat.Tests.csproj`
- Create: `tests/GuildRelay.Platform.Windows.Tests/GuildRelay.Platform.Windows.Tests.csproj`
- Modify: `GuildRelay.sln`
- Modify: `src/GuildRelay.App/GuildRelay.App.csproj` (add new project references)

- [ ] **Step 1: Create projects**

```bash
cd C:/Users/tosha/IdeaProjects/game-event-repost

dotnet new classlib -n GuildRelay.Platform.Windows -o src/GuildRelay.Platform.Windows -f net8.0-windows
dotnet new classlib -n GuildRelay.Features.Chat    -o src/GuildRelay.Features.Chat    -f net8.0

dotnet new xunit -n GuildRelay.Features.Chat.Tests    -o tests/GuildRelay.Features.Chat.Tests    -f net8.0
dotnet new xunit -n GuildRelay.Platform.Windows.Tests -o tests/GuildRelay.Platform.Windows.Tests -f net8.0-windows
```

Delete auto-generated `Class1.cs` / `UnitTest1.cs` in each project.

**Note:** `Platform.Windows` and its test project use `net8.0-windows` because they need Windows desktop APIs. `Features.Chat` uses `net8.0` (platform-agnostic — depends only on Core interfaces).

- [ ] **Step 2: Add to solution**

```bash
dotnet sln add src/GuildRelay.Platform.Windows/GuildRelay.Platform.Windows.csproj
dotnet sln add src/GuildRelay.Features.Chat/GuildRelay.Features.Chat.csproj
dotnet sln add tests/GuildRelay.Features.Chat.Tests/GuildRelay.Features.Chat.Tests.csproj
dotnet sln add tests/GuildRelay.Platform.Windows.Tests/GuildRelay.Platform.Windows.Tests.csproj
```

- [ ] **Step 3: Add project references**

```bash
# Platform.Windows depends on Core
dotnet add src/GuildRelay.Platform.Windows reference src/GuildRelay.Core

# Features.Chat depends on Core
dotnet add src/GuildRelay.Features.Chat reference src/GuildRelay.Core

# App depends on the new projects
dotnet add src/GuildRelay.App reference src/GuildRelay.Platform.Windows
dotnet add src/GuildRelay.App reference src/GuildRelay.Features.Chat

# Test projects
dotnet add tests/GuildRelay.Features.Chat.Tests reference src/GuildRelay.Features.Chat
dotnet add tests/GuildRelay.Features.Chat.Tests reference src/GuildRelay.Core
dotnet add tests/GuildRelay.Platform.Windows.Tests reference src/GuildRelay.Platform.Windows
dotnet add tests/GuildRelay.Platform.Windows.Tests reference src/GuildRelay.Core
```

- [ ] **Step 4: Add packages**

```bash
# System.Drawing.Common for image processing in Platform.Windows
dotnet add src/GuildRelay.Platform.Windows package System.Drawing.Common

# FluentAssertions for new test projects
dotnet add tests/GuildRelay.Features.Chat.Tests package FluentAssertions
dotnet add tests/GuildRelay.Platform.Windows.Tests package FluentAssertions
```

- [ ] **Step 5: Harden csproj settings**

Add to every new project's `<PropertyGroup>`:

```xml
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<LangVersion>latest</LangVersion>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

For `Platform.Windows.csproj`, also ensure `<UseWindowsForms>true</UseWindowsForms>` is present (needed for `System.Drawing` types). It should NOT have `<UseWPF>true</UseWPF>`.

For `Features.Chat.csproj`, the target framework must be `net8.0` (NOT `net8.0-windows`).

- [ ] **Step 6: Build and verify**

```bash
dotnet build
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add GuildRelay.sln src/GuildRelay.Platform.Windows src/GuildRelay.Features.Chat tests/GuildRelay.Features.Chat.Tests tests/GuildRelay.Platform.Windows.Tests src/GuildRelay.App/GuildRelay.App.csproj
git commit -m "feat: scaffold Platform.Windows + Features.Chat projects"
```

---

## Task 2: Core interfaces — IScreenCapture, CapturedFrame, IOcrEngine, OcrResult

**Files:**
- Create: `src/GuildRelay.Core/Capture/IScreenCapture.cs`
- Create: `src/GuildRelay.Core/Capture/CapturedFrame.cs`
- Create: `src/GuildRelay.Core/Ocr/IOcrEngine.cs`
- Create: `src/GuildRelay.Core/Ocr/OcrResult.cs`
- Create: `src/GuildRelay.Core/Ocr/OcrLine.cs`

- [ ] **Step 1: Implement the capture interfaces**

`src/GuildRelay.Core/Capture/CapturedFrame.cs`:

```csharp
using System;

namespace GuildRelay.Core.Capture;

/// <summary>
/// Raw BGRA pixel buffer captured from a screen region.
/// </summary>
public sealed class CapturedFrame : IDisposable
{
    public CapturedFrame(byte[] bgraPixels, int width, int height, int stride)
    {
        BgraPixels = bgraPixels;
        Width = width;
        Height = height;
        Stride = stride;
    }

    public byte[] BgraPixels { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }

    public void Dispose() { /* pixel buffer is managed; no-op for now */ }
}
```

`src/GuildRelay.Core/Capture/IScreenCapture.cs`:

```csharp
using System.Drawing;

namespace GuildRelay.Core.Capture;

public interface IScreenCapture
{
    CapturedFrame CaptureRegion(Rectangle screenSpaceRect);
}
```

- [ ] **Step 2: Implement the OCR interfaces**

`src/GuildRelay.Core/Ocr/OcrLine.cs`:

```csharp
using System.Drawing;

namespace GuildRelay.Core.Ocr;

public sealed record OcrLine(string Text, float Confidence, RectangleF Bounds);
```

`src/GuildRelay.Core/Ocr/OcrResult.cs`:

```csharp
using System.Collections.Generic;

namespace GuildRelay.Core.Ocr;

public sealed record OcrResult(IReadOnlyList<OcrLine> Lines);
```

`src/GuildRelay.Core/Ocr/IOcrEngine.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GuildRelay.Core.Ocr;

public interface IOcrEngine
{
    Task<OcrResult> RecognizeAsync(ReadOnlyMemory<byte> bgraPixels,
                                   int width, int height, int stride,
                                   CancellationToken ct);
}
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build src/GuildRelay.Core
```

Expected: 0 warnings. Note: `System.Drawing.Primitives` (for `Rectangle`, `RectangleF`) is part of the .NET 8 BCL — no extra package needed in Core.

- [ ] **Step 4: Commit**

```bash
git add src/GuildRelay.Core/Capture src/GuildRelay.Core/Ocr
git commit -m "feat(core): add IScreenCapture, CapturedFrame, IOcrEngine contracts"
```

---

## Task 3: CompiledPattern — shared rule matcher

**Files:**
- Create: `src/GuildRelay.Core/Rules/CompiledPattern.cs`
- Create: `tests/GuildRelay.Core.Tests/Rules/CompiledPatternTests.cs`

This is lifted into Core (not Features.Chat) so the Status Watcher can reuse it in Plan 4.

- [ ] **Step 1: Write the failing tests**

`tests/GuildRelay.Core.Tests/Rules/CompiledPatternTests.cs`:

```csharp
using FluentAssertions;
using GuildRelay.Core.Rules;
using Xunit;

namespace GuildRelay.Core.Tests.Rules;

public class CompiledPatternTests
{
    [Fact]
    public void LiteralPatternMatchesCaseInsensitive()
    {
        var pattern = CompiledPattern.Create("zerg", isRegex: false);
        pattern.IsMatch("I saw a ZERG coming").Should().BeTrue();
    }

    [Fact]
    public void LiteralPatternDoesNotMatchAbsentText()
    {
        var pattern = CompiledPattern.Create("zerg", isRegex: false);
        pattern.IsMatch("everything is fine").Should().BeFalse();
    }

    [Fact]
    public void RegexPatternMatchesGroups()
    {
        var pattern = CompiledPattern.Create("(inc|incoming|enemies)", isRegex: true);
        pattern.IsMatch("inc north gate").Should().BeTrue();
        pattern.IsMatch("incoming from east").Should().BeTrue();
        pattern.IsMatch("all clear").Should().BeFalse();
    }

    [Fact]
    public void RegexPatternIsCaseInsensitive()
    {
        var pattern = CompiledPattern.Create("zerg", isRegex: true);
        pattern.IsMatch("ZERG spotted").Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~CompiledPatternTests"
```

Expected: compile error, `CompiledPattern` does not exist.

- [ ] **Step 3: Implement `CompiledPattern`**

`src/GuildRelay.Core/Rules/CompiledPattern.cs`:

```csharp
using System;
using System.Text.RegularExpressions;

namespace GuildRelay.Core.Rules;

/// <summary>
/// A single compiled pattern — either a case-insensitive literal substring
/// check or a compiled regex. Used by Chat Watcher rules and Status
/// Watcher disconnect patterns.
/// </summary>
public sealed class CompiledPattern
{
    private readonly Regex? _regex;
    private readonly string? _literal;

    private CompiledPattern(Regex? regex, string? literal)
    {
        _regex = regex;
        _literal = literal;
    }

    public static CompiledPattern Create(string pattern, bool isRegex)
    {
        if (isRegex)
            return new CompiledPattern(
                new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase),
                literal: null);
        return new CompiledPattern(regex: null, literal: pattern);
    }

    public bool IsMatch(string input)
    {
        if (_regex is not null)
            return _regex.IsMatch(input);
        return input.Contains(_literal!, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~CompiledPatternTests"
```

Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Core/Rules tests/GuildRelay.Core.Tests/Rules
git commit -m "feat(core): add CompiledPattern for literal and regex rule matching"
```

---

## Task 4: TextNormalizer — OCR line cleanup

**Files:**
- Create: `src/GuildRelay.Features.Chat/TextNormalizer.cs`
- Create: `tests/GuildRelay.Features.Chat.Tests/TextNormalizerTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/GuildRelay.Features.Chat.Tests/TextNormalizerTests.cs`:

```csharp
using FluentAssertions;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class TextNormalizerTests
{
    [Fact]
    public void TrimsAndCollapsesWhitespace()
    {
        TextNormalizer.Normalize("  hello   world  ").Should().Be("hello world");
    }

    [Fact]
    public void LowercasesText()
    {
        TextNormalizer.Normalize("HELLO World").Should().Be("hello world");
    }

    [Fact]
    public void StripsCommonOcrNoiseCharacters()
    {
        // Pipes, brackets, and other characters OCR frequently hallucinates
        TextNormalizer.Normalize("he|lo [world]").Should().Be("helo world");
    }

    [Fact]
    public void EmptyInputReturnsEmpty()
    {
        TextNormalizer.Normalize("").Should().BeEmpty();
        TextNormalizer.Normalize("   ").Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~TextNormalizerTests"
```

Expected: compile error.

- [ ] **Step 3: Implement `TextNormalizer`**

`src/GuildRelay.Features.Chat/TextNormalizer.cs`:

```csharp
using System.Text.RegularExpressions;

namespace GuildRelay.Features.Chat;

/// <summary>
/// Normalizes OCR output for dedup hashing and rule matching:
/// lowercase, collapse whitespace, strip known noise characters.
/// </summary>
public static class TextNormalizer
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
    // Characters that OCR engines frequently hallucinate on game fonts
    private static readonly Regex NoiseChars = new(@"[\|\[\]\{\}]", RegexOptions.Compiled);

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var cleaned = NoiseChars.Replace(input, string.Empty);
        cleaned = WhitespaceRun.Replace(cleaned, " ").Trim();
        return cleaned.ToLowerInvariant();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/GuildRelay.Features.Chat.Tests
```

Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Features.Chat/TextNormalizer.cs tests/GuildRelay.Features.Chat.Tests/TextNormalizerTests.cs
git commit -m "feat(chat): add TextNormalizer for OCR line cleanup"
```

---

## Task 5: ChatDedup — LRU hash-based deduplication

**Files:**
- Create: `src/GuildRelay.Features.Chat/ChatDedup.cs`
- Create: `tests/GuildRelay.Features.Chat.Tests/ChatDedupTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/GuildRelay.Features.Chat.Tests/ChatDedupTests.cs`:

```csharp
using FluentAssertions;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class ChatDedupTests
{
    [Fact]
    public void FirstOccurrenceIsNotDuplicate()
    {
        var dedup = new ChatDedup(capacity: 256);
        dedup.IsDuplicate("hello world").Should().BeFalse();
    }

    [Fact]
    public void SecondOccurrenceIsDuplicate()
    {
        var dedup = new ChatDedup(capacity: 256);
        dedup.IsDuplicate("hello world");
        dedup.IsDuplicate("hello world").Should().BeTrue();
    }

    [Fact]
    public void DifferentLinesAreNotDuplicates()
    {
        var dedup = new ChatDedup(capacity: 256);
        dedup.IsDuplicate("line one");
        dedup.IsDuplicate("line two").Should().BeFalse();
    }

    [Fact]
    public void OldestEntryIsEvictedWhenCapacityExceeded()
    {
        var dedup = new ChatDedup(capacity: 2);
        dedup.IsDuplicate("a"); // slot 1
        dedup.IsDuplicate("b"); // slot 2
        dedup.IsDuplicate("c"); // evicts "a"

        dedup.IsDuplicate("a").Should().BeFalse("'a' was evicted");
        dedup.IsDuplicate("b").Should().BeTrue("'b' is still cached");
    }

    [Fact]
    public void ClearResetsAllEntries()
    {
        var dedup = new ChatDedup(capacity: 256);
        dedup.IsDuplicate("hello");
        dedup.Clear();
        dedup.IsDuplicate("hello").Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~ChatDedupTests"
```

Expected: compile error, `ChatDedup` does not exist.

- [ ] **Step 3: Implement `ChatDedup`**

`src/GuildRelay.Features.Chat/ChatDedup.cs`:

```csharp
using System.Collections.Generic;
using System.IO.Hashing;
using System.Text;

namespace GuildRelay.Features.Chat;

/// <summary>
/// LRU set of FNV-1a hashes of normalized chat lines. Returns true if a
/// line was already seen within the last <c>capacity</c> lines.
/// </summary>
public sealed class ChatDedup
{
    private readonly int _capacity;
    private readonly LinkedList<ulong> _order = new();
    private readonly HashSet<ulong> _seen = new();

    public ChatDedup(int capacity) { _capacity = capacity; }

    /// <summary>
    /// Returns true if the normalized line has been seen before within the
    /// LRU window. If new, the line is added to the set.
    /// </summary>
    public bool IsDuplicate(string normalizedLine)
    {
        var hash = Hash(normalizedLine);
        if (_seen.Contains(hash))
            return true;

        if (_seen.Count >= _capacity)
        {
            var oldest = _order.First!.Value;
            _order.RemoveFirst();
            _seen.Remove(oldest);
        }

        _seen.Add(hash);
        _order.AddLast(hash);
        return false;
    }

    public void Clear()
    {
        _order.Clear();
        _seen.Clear();
    }

    private static ulong Hash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        // XxHash64 is fast and available in .NET 8; FNV-1a would also work
        return System.IO.Hashing.XxHash64.HashToUInt64(bytes);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/GuildRelay.Features.Chat.Tests
```

Expected: all pass (TextNormalizer + ChatDedup tests).

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Features.Chat/ChatDedup.cs tests/GuildRelay.Features.Chat.Tests/ChatDedupTests.cs
git commit -m "feat(chat): add ChatDedup LRU hash set for line deduplication"
```

---

## Task 6: Preprocessing pipeline interfaces + orchestration

**Files:**
- Create: `src/GuildRelay.Features.Chat/Preprocessing/IPreprocessStage.cs`
- Create: `src/GuildRelay.Features.Chat/Preprocessing/PreprocessPipeline.cs`
- Create: `tests/GuildRelay.Features.Chat.Tests/PreprocessPipelineTests.cs`

The concrete stages (grayscale, contrast, etc.) live in Platform.Windows and are added in Task 9. This task tests the pipeline runner in isolation using a trivial fake stage.

- [ ] **Step 1: Write the failing tests**

`tests/GuildRelay.Features.Chat.Tests/PreprocessPipelineTests.cs`:

```csharp
using FluentAssertions;
using GuildRelay.Core.Capture;
using GuildRelay.Features.Chat.Preprocessing;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class PreprocessPipelineTests
{
    /// <summary>Fake stage that sets all blue bytes to 0xFF.</summary>
    private sealed class FakeBlueStage : IPreprocessStage
    {
        public string Name => "fakeBlue";
        public CapturedFrame Apply(CapturedFrame frame)
        {
            var pixels = (byte[])frame.BgraPixels.Clone();
            for (int i = 0; i < pixels.Length; i += 4)
                pixels[i] = 0xFF; // B channel
            return new CapturedFrame(pixels, frame.Width, frame.Height, frame.Stride);
        }
    }

    /// <summary>Fake stage that sets all green bytes to 0xFF.</summary>
    private sealed class FakeGreenStage : IPreprocessStage
    {
        public string Name => "fakeGreen";
        public CapturedFrame Apply(CapturedFrame frame)
        {
            var pixels = (byte[])frame.BgraPixels.Clone();
            for (int i = 1; i < pixels.Length; i += 4)
                pixels[i] = 0xFF; // G channel
            return new CapturedFrame(pixels, frame.Width, frame.Height, frame.Stride);
        }
    }

    private static CapturedFrame BlackFrame()
        => new(new byte[4 * 4], width: 2, height: 2, stride: 8); // 2x2 all zeros

    [Fact]
    public void EmptyPipelineReturnsInputUnchanged()
    {
        var pipeline = new PreprocessPipeline(System.Array.Empty<IPreprocessStage>());
        var input = BlackFrame();

        var output = pipeline.Apply(input);

        output.BgraPixels.Should().Equal(input.BgraPixels);
    }

    [Fact]
    public void StagesAreAppliedInOrder()
    {
        var pipeline = new PreprocessPipeline(new IPreprocessStage[]
        {
            new FakeBlueStage(),
            new FakeGreenStage()
        });
        var input = BlackFrame();

        var output = pipeline.Apply(input);

        // Blue channel should be 0xFF (from first stage)
        output.BgraPixels[0].Should().Be(0xFF);
        // Green channel should be 0xFF (from second stage)
        output.BgraPixels[1].Should().Be(0xFF);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~PreprocessPipelineTests"
```

Expected: compile error.

- [ ] **Step 3: Implement interface and pipeline**

`src/GuildRelay.Features.Chat/Preprocessing/IPreprocessStage.cs`:

```csharp
using GuildRelay.Core.Capture;

namespace GuildRelay.Features.Chat.Preprocessing;

/// <summary>
/// A single image-processing stage in the chat capture preprocessing pipeline.
/// Implementations live in Platform.Windows (grayscale, contrast, etc.).
/// </summary>
public interface IPreprocessStage
{
    string Name { get; }
    CapturedFrame Apply(CapturedFrame frame);
}
```

`src/GuildRelay.Features.Chat/Preprocessing/PreprocessPipeline.cs`:

```csharp
using System.Collections.Generic;
using GuildRelay.Core.Capture;

namespace GuildRelay.Features.Chat.Preprocessing;

/// <summary>
/// Applies a sequence of <see cref="IPreprocessStage"/>s to a captured
/// frame in order. Stage list is built from config at startup and swapped
/// atomically on config reload.
/// </summary>
public sealed class PreprocessPipeline
{
    private readonly IReadOnlyList<IPreprocessStage> _stages;

    public PreprocessPipeline(IReadOnlyList<IPreprocessStage> stages)
    {
        _stages = stages;
    }

    public CapturedFrame Apply(CapturedFrame frame)
    {
        var current = frame;
        foreach (var stage in _stages)
            current = stage.Apply(current);
        return current;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/GuildRelay.Features.Chat.Tests
```

Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Features.Chat/Preprocessing tests/GuildRelay.Features.Chat.Tests/PreprocessPipelineTests.cs
git commit -m "feat(chat): add IPreprocessStage interface and PreprocessPipeline runner"
```

---

## Task 7: Chat config DTOs

**Files:**
- Create: `src/GuildRelay.Core/Config/RegionConfig.cs`
- Create: `src/GuildRelay.Core/Config/PreprocessStageConfig.cs`
- Create: `src/GuildRelay.Core/Config/ChatRuleConfig.cs`
- Create: `src/GuildRelay.Core/Config/ChatConfig.cs`
- Modify: `src/GuildRelay.Core/Config/AppConfig.cs`

- [ ] **Step 1: Implement config DTOs**

`src/GuildRelay.Core/Config/RegionConfig.cs`:

```csharp
namespace GuildRelay.Core.Config;

public sealed record RegionConfig(
    int X, int Y, int Width, int Height,
    int CapturedAtDpi,
    ResolutionConfig CapturedAtResolution,
    string MonitorDeviceId)
{
    public static RegionConfig Empty => new(0, 0, 0, 0, 96, ResolutionConfig.Empty, string.Empty);
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public sealed record ResolutionConfig(int Width, int Height)
{
    public static ResolutionConfig Empty => new(0, 0);
}
```

`src/GuildRelay.Core/Config/PreprocessStageConfig.cs`:

```csharp
using System.Collections.Generic;

namespace GuildRelay.Core.Config;

public sealed record PreprocessStageConfig(
    string Stage,
    Dictionary<string, double>? Parameters = null);
```

`src/GuildRelay.Core/Config/ChatRuleConfig.cs`:

```csharp
namespace GuildRelay.Core.Config;

public sealed record ChatRuleConfig(
    string Id,
    string Label,
    string Pattern,
    bool Regex);
```

`src/GuildRelay.Core/Config/ChatConfig.cs`:

```csharp
using System.Collections.Generic;

namespace GuildRelay.Core.Config;

public sealed record ChatConfig(
    bool Enabled,
    int CaptureIntervalMs,
    double OcrConfidenceThreshold,
    RegionConfig Region,
    List<PreprocessStageConfig> PreprocessPipeline,
    List<ChatRuleConfig> Rules,
    Dictionary<string, string> Templates)
{
    public static ChatConfig Default => new(
        Enabled: false,
        CaptureIntervalMs: 1000,
        OcrConfidenceThreshold: 0.65,
        Region: RegionConfig.Empty,
        PreprocessPipeline: new List<PreprocessStageConfig>
        {
            new("grayscale"),
            new("contrastStretch", new Dictionary<string, double> { ["low"] = 0.1, ["high"] = 0.9 }),
            new("upscale", new Dictionary<string, double> { ["factor"] = 2 }),
            new("adaptiveThreshold", new Dictionary<string, double> { ["blockSize"] = 15 })
        },
        Rules: new List<ChatRuleConfig>(),
        Templates: new Dictionary<string, string>
        {
            ["default"] = "**{player}** saw chat match [{rule_label}]: `{matched_text}`"
        });
}
```

- [ ] **Step 2: Update `AppConfig` to include `ChatConfig`**

Modify `src/GuildRelay.Core/Config/AppConfig.cs`:

```csharp
namespace GuildRelay.Core.Config;

public sealed record AppConfig(
    int SchemaVersion,
    GeneralConfig General,
    ChatConfig Chat,
    LogsConfig Logs
)
{
    public static AppConfig Default => new(
        SchemaVersion: 1,
        General: GeneralConfig.Default,
        Chat: ChatConfig.Default,
        Logs: LogsConfig.Default);
}
```

- [ ] **Step 3: Fix any compilation issues in existing code that constructs `AppConfig`**

`CoreHost.CreateAsync()` and `ConfigStoreTests` construct `AppConfig`. Update them to include the new `Chat` field. In `CoreHost.cs`, change the `AppConfig.Default` usage (it auto-picks up the new field). In `ConfigStoreTests`, the tests use `LoadOrCreateDefaultsAsync()` which goes through `AppConfig.Default` — should work automatically. Build to verify.

- [ ] **Step 4: Build and run all tests**

```bash
dotnet build && dotnet test
```

Expected: all existing tests still pass.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Core/Config
git commit -m "feat(core): add ChatConfig, RegionConfig, and related config DTOs"
```

---

## Task 8: ChatWatcher : IFeature — TDD with fakes

**Files:**
- Create: `src/GuildRelay.Features.Chat/ChatWatcher.cs`
- Create: `tests/GuildRelay.Features.Chat.Tests/ChatWatcherTests.cs`

This is the main feature class. Tests use fakes for `IScreenCapture` and `IOcrEngine` to keep the test runner platform-agnostic.

- [ ] **Step 1: Write the failing tests**

`tests/GuildRelay.Features.Chat.Tests/ChatWatcherTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Capture;
using GuildRelay.Core.Config;
using GuildRelay.Core.Events;
using GuildRelay.Core.Features;
using GuildRelay.Core.Ocr;
using GuildRelay.Core.Rules;
using GuildRelay.Features.Chat;
using GuildRelay.Features.Chat.Preprocessing;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class ChatWatcherTests
{
    private sealed class FakeCapture : IScreenCapture
    {
        public CapturedFrame CaptureRegion(Rectangle rect)
            => new(new byte[4 * 4], 2, 2, 8);
    }

    private sealed class FakeOcr : IOcrEngine
    {
        public List<OcrLine> NextLines { get; set; } = new();

        public Task<OcrResult> RecognizeAsync(ReadOnlyMemory<byte> bgraPixels,
            int width, int height, int stride, CancellationToken ct)
            => Task.FromResult(new OcrResult(NextLines));
    }

    private static ChatWatcher CreateWatcher(
        FakeOcr ocr,
        EventBus bus,
        List<ChatRuleConfig> rules)
    {
        var config = ChatConfig.Default with
        {
            Enabled = true,
            CaptureIntervalMs = 50, // fast for tests
            OcrConfidenceThreshold = 0.5,
            Region = new RegionConfig(0, 0, 100, 100, 96,
                new ResolutionConfig(1920, 1080), "TEST"),
            Rules = rules
        };
        return new ChatWatcher(
            new FakeCapture(),
            ocr,
            new PreprocessPipeline(Array.Empty<IPreprocessStage>()),
            bus,
            config,
            playerName: "Tosh");
    }

    [Fact]
    public async Task MatchingLineEmitsDetectionEvent()
    {
        var ocr = new FakeOcr();
        var bus = new EventBus(capacity: 16);
        var rules = new List<ChatRuleConfig>
        {
            new("r1", "Incoming", "(inc|incoming)", Regex: true)
        };
        var watcher = CreateWatcher(ocr, bus, rules);

        ocr.NextLines = new List<OcrLine>
        {
            new("inc north gate", 0.9f, RectangleF.Empty)
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(200); // let one tick fire
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().ContainSingle()
            .Which.Should().Match<DetectionEvent>(e =>
                e.FeatureId == "chat" &&
                e.RuleLabel == "Incoming" &&
                e.MatchedContent == "inc north gate" &&
                e.PlayerName == "Tosh");
    }

    [Fact]
    public async Task DuplicateLinesAreNotReEmitted()
    {
        var ocr = new FakeOcr();
        var bus = new EventBus(capacity: 16);
        var rules = new List<ChatRuleConfig>
        {
            new("r1", "Incoming", "inc", Regex: false)
        };
        var watcher = CreateWatcher(ocr, bus, rules);

        // Same line on every tick
        ocr.NextLines = new List<OcrLine>
        {
            new("inc north", 0.9f, RectangleF.Empty)
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(300); // let multiple ticks fire
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().HaveCount(1, "same line should only emit once due to dedup");
    }

    [Fact]
    public async Task LowConfidenceLinesAreDropped()
    {
        var ocr = new FakeOcr();
        var bus = new EventBus(capacity: 16);
        var rules = new List<ChatRuleConfig>
        {
            new("r1", "Incoming", "inc", Regex: false)
        };
        var watcher = CreateWatcher(ocr, bus, rules);

        ocr.NextLines = new List<OcrLine>
        {
            new("inc north", 0.3f, RectangleF.Empty) // below 0.5 threshold
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.StartAsync(cts.Token);
        await Task.Delay(200);
        await watcher.StopAsync();
        bus.Complete();

        var events = new List<DetectionEvent>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            events.Add(e);

        events.Should().BeEmpty("low confidence lines should be silently dropped");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~ChatWatcherTests"
```

Expected: compile error, `ChatWatcher` does not exist.

- [ ] **Step 3: Implement `ChatWatcher`**

`src/GuildRelay.Features.Chat/ChatWatcher.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GuildRelay.Core.Capture;
using GuildRelay.Core.Config;
using GuildRelay.Core.Events;
using GuildRelay.Core.Features;
using GuildRelay.Core.Ocr;
using GuildRelay.Core.Rules;
using GuildRelay.Features.Chat.Preprocessing;

namespace GuildRelay.Features.Chat;

public sealed class ChatWatcher : IFeature
{
    private readonly IScreenCapture _capture;
    private readonly IOcrEngine _ocr;
    private readonly PreprocessPipeline _pipeline;
    private readonly EventBus _bus;
    private readonly string _playerName;
    private readonly ChatDedup _dedup = new(capacity: 256);
    private ChatConfig _config;
    private List<CompiledRule> _compiledRules;
    private CancellationTokenSource? _cts;

    public ChatWatcher(
        IScreenCapture capture,
        IOcrEngine ocr,
        PreprocessPipeline pipeline,
        EventBus bus,
        ChatConfig config,
        string playerName)
    {
        _capture = capture;
        _ocr = ocr;
        _pipeline = pipeline;
        _bus = bus;
        _config = config;
        _playerName = playerName;
        _compiledRules = CompileRules(config.Rules);
    }

    public string Id => "chat";
    public string DisplayName => "Chat Watcher";
    public FeatureStatus Status { get; private set; } = FeatureStatus.Idle;
    public event EventHandler<StatusChangedArgs>? StatusChanged;

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _dedup.Clear();
        Status = FeatureStatus.Running;
        _ = Task.Run(() => CaptureLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        Status = FeatureStatus.Idle;
        return Task.CompletedTask;
    }

    public void ApplyConfig(JsonElement featureConfig)
    {
        // Hot-reload: reparse, recompile rules, swap atomically
        var newConfig = featureConfig.Deserialize<ChatConfig>();
        if (newConfig is null) return;
        _config = newConfig;
        _compiledRules = CompileRules(newConfig.Rules);
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_config.CaptureIntervalMs));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await ProcessOneTickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
        }
    }

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

        var rules = _compiledRules; // snapshot for this tick

        foreach (var line in ocrResult.Lines)
        {
            if (line.Confidence < _config.OcrConfidenceThreshold)
                continue;

            var normalized = TextNormalizer.Normalize(line.Text);
            if (string.IsNullOrEmpty(normalized))
                continue;

            if (_dedup.IsDuplicate(normalized))
                continue;

            foreach (var rule in rules)
            {
                if (rule.Pattern.IsMatch(normalized))
                {
                    var evt = new DetectionEvent(
                        FeatureId: "chat",
                        RuleLabel: rule.Label,
                        MatchedContent: line.Text, // original, not normalized
                        TimestampUtc: DateTimeOffset.UtcNow,
                        PlayerName: _playerName,
                        Extras: new Dictionary<string, string>(),
                        ImageAttachment: null);

                    await _bus.PublishAsync(evt, ct).ConfigureAwait(false);
                    break; // one event per line, first matching rule wins
                }
            }
        }
    }

    private static List<CompiledRule> CompileRules(List<ChatRuleConfig> rules)
        => rules.Select(r => new CompiledRule(r.Label, CompiledPattern.Create(r.Pattern, r.Regex))).ToList();

    private sealed record CompiledRule(string Label, CompiledPattern Pattern);
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/GuildRelay.Features.Chat.Tests
```

Expected: all tests pass (TextNormalizer + ChatDedup + PreprocessPipeline + ChatWatcher).

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Features.Chat/ChatWatcher.cs tests/GuildRelay.Features.Chat.Tests/ChatWatcherTests.cs
git commit -m "feat(chat): add ChatWatcher with capture/OCR/dedup/rule-matching pipeline"
```

---

## Task 9: BitBltCapture + WindowsMediaOcrEngine + DpiHelper (Platform.Windows)

**Files:**
- Create: `src/GuildRelay.Platform.Windows/Capture/BitBltCapture.cs`
- Create: `src/GuildRelay.Platform.Windows/Ocr/WindowsMediaOcrEngine.cs`
- Create: `src/GuildRelay.Platform.Windows/Dpi/DpiHelper.cs`

These are Windows-specific implementations behind Core interfaces. They use P/Invoke (BitBlt), `Windows.Media.Ocr` (UWP), and GDI+. No unit tests in this task — they require a Windows desktop session and are tested manually via the app and in Platform.Windows.Tests as integration tests.

- [ ] **Step 1: Implement `BitBltCapture`**

`src/GuildRelay.Platform.Windows/Capture/BitBltCapture.cs`:

```csharp
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GuildRelay.Core.Capture;

namespace GuildRelay.Platform.Windows.Capture;

public sealed class BitBltCapture : IScreenCapture
{
    public CapturedFrame CaptureRegion(Rectangle rect)
    {
        using var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);

        var hdcDest = g.GetHdc();
        var hdcSrc = GetDC(IntPtr.Zero); // desktop DC
        try
        {
            BitBlt(hdcDest, 0, 0, rect.Width, rect.Height,
                   hdcSrc, rect.X, rect.Y, SRCCOPY);
        }
        finally
        {
            g.ReleaseHdc(hdcDest);
            ReleaseDC(IntPtr.Zero, hdcSrc);
        }

        var lockBits = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[lockBits.Stride * lockBits.Height];
            Marshal.Copy(lockBits.Scan0, bytes, 0, bytes.Length);
            return new CapturedFrame(bytes, lockBits.Width, lockBits.Height, lockBits.Stride);
        }
        finally
        {
            bmp.UnlockBits(lockBits);
        }
    }

    private const int SRCCOPY = 0x00CC0020;

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest,
        int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
}
```

- [ ] **Step 2: Implement `WindowsMediaOcrEngine`**

First, add the `Microsoft.Windows.SDK.Contracts` package to Platform.Windows so we can use `Windows.Media.Ocr`:

```bash
dotnet add src/GuildRelay.Platform.Windows package Microsoft.Windows.SDK.Contracts -v 10.0.22621.755
```

`src/GuildRelay.Platform.Windows/Ocr/WindowsMediaOcrEngine.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using GuildRelay.Core.Ocr;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace GuildRelay.Platform.Windows.Ocr;

public sealed class WindowsMediaOcrEngine : IOcrEngine
{
    private readonly OcrEngine _engine;

    public WindowsMediaOcrEngine()
    {
        _engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException(
                "Windows.Media.Ocr could not create an OCR engine. " +
                "Ensure at least one language pack with OCR is installed.");
    }

    public async Task<OcrResult> RecognizeAsync(
        ReadOnlyMemory<byte> bgraPixels,
        int width, int height, int stride,
        CancellationToken ct)
    {
        // Create a SoftwareBitmap from the BGRA buffer
        var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(bgraPixels.ToArray().AsBuffer());

        var result = await _engine.RecognizeAsync(bitmap);

        var lines = new List<Core.Ocr.OcrLine>();
        foreach (var line in result.Lines)
        {
            // Windows.Media.Ocr doesn't expose per-line confidence directly;
            // use a heuristic based on word-level data or default to 1.0
            var text = line.Text;
            var bounds = RectangleF.Empty;
            if (line.Words.Count > 0)
            {
                var first = line.Words[0].BoundingRect;
                var last = line.Words[line.Words.Count - 1].BoundingRect;
                bounds = new RectangleF(
                    (float)first.X, (float)first.Y,
                    (float)(last.X + last.Width - first.X),
                    (float)Math.Max(first.Height, last.Height));
            }
            lines.Add(new Core.Ocr.OcrLine(text, Confidence: 1.0f, bounds));
        }

        return new OcrResult(lines);
    }
}
```

**Note:** `Windows.Media.Ocr` does not expose per-line confidence. The confidence is set to 1.0 and filtering relies on the user's preprocessing quality. This is a known limitation documented in architecture §16 (open items). If it proves insufficient, Tesseract (which does provide confidence) can be swapped in behind the same interface.

- [ ] **Step 3: Implement `DpiHelper`**

`src/GuildRelay.Platform.Windows/Dpi/DpiHelper.cs`:

```csharp
using System;
using System.Runtime.InteropServices;

namespace GuildRelay.Platform.Windows.Dpi;

public static class DpiHelper
{
    /// <summary>
    /// Returns the effective DPI for the primary monitor.
    /// </summary>
    public static int GetPrimaryMonitorDpi()
    {
        var hdc = GetDC(IntPtr.Zero);
        try
        {
            return GetDeviceCaps(hdc, LOGPIXELSX);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    /// <summary>
    /// Returns the primary screen resolution in physical pixels.
    /// </summary>
    public static (int Width, int Height) GetPrimaryScreenResolution()
    {
        return (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
    }

    private const int LOGPIXELSX = 88;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build
```

Expected: 0 warnings, 0 errors. If the `Microsoft.Windows.SDK.Contracts` package has issues, an alternative is to use `[DllImport]` for Windows.Media.Ocr via WinRT interop or to add `<TargetPlatformVersion>10.0.22621.0</TargetPlatformVersion>` to the Platform.Windows csproj and use the built-in WinRT projections.

- [ ] **Step 5: Run all tests to verify nothing broke**

```bash
dotnet test
```

Expected: all existing tests still pass.

- [ ] **Step 6: Commit**

```bash
git add src/GuildRelay.Platform.Windows
git commit -m "feat(platform): add BitBltCapture, WindowsMediaOcrEngine, DpiHelper"
```

---

## Task 10: Concrete preprocess stages (Platform.Windows)

**Files:**
- Create: `src/GuildRelay.Platform.Windows/Preprocessing/GrayscaleStage.cs`
- Create: `src/GuildRelay.Platform.Windows/Preprocessing/ContrastStretchStage.cs`
- Create: `src/GuildRelay.Platform.Windows/Preprocessing/UpscaleStage.cs`
- Create: `src/GuildRelay.Platform.Windows/Preprocessing/AdaptiveThresholdStage.cs`
- Create: `src/GuildRelay.Platform.Windows/Preprocessing/StageFactory.cs`

Since Platform.Windows depends on Core and Features.Chat provides the `IPreprocessStage` interface, Platform.Windows needs a reference to Features.Chat. Alternatively, move `IPreprocessStage` to Core. The cleanest option is to **move `IPreprocessStage` to Core** (it's a small interface with no dependencies) so Platform.Windows doesn't depend on Features.Chat.

- [ ] **Step 1: Move IPreprocessStage to Core**

Move `src/GuildRelay.Features.Chat/Preprocessing/IPreprocessStage.cs` to `src/GuildRelay.Core/Preprocessing/IPreprocessStage.cs`. Update the namespace from `GuildRelay.Features.Chat.Preprocessing` to `GuildRelay.Core.Preprocessing`. Update all usages in Features.Chat and its tests.

- [ ] **Step 2: Add Platform.Windows reference to Features.Chat test project (for StageFactory)**

```bash
dotnet add tests/GuildRelay.Features.Chat.Tests reference src/GuildRelay.Platform.Windows
```

This is only needed if you want to integration-test the real stages. For now, the fake stages in Task 6 are sufficient.

- [ ] **Step 3: Implement `GrayscaleStage`**

`src/GuildRelay.Platform.Windows/Preprocessing/GrayscaleStage.cs`:

```csharp
using GuildRelay.Core.Capture;
using GuildRelay.Core.Preprocessing;

namespace GuildRelay.Platform.Windows.Preprocessing;

public sealed class GrayscaleStage : IPreprocessStage
{
    public string Name => "grayscale";

    public CapturedFrame Apply(CapturedFrame frame)
    {
        var src = frame.BgraPixels;
        var dst = new byte[src.Length];
        for (int i = 0; i < src.Length; i += 4)
        {
            // ITU-R BT.601 luminance
            var gray = (byte)(0.299 * src[i + 2] + 0.587 * src[i + 1] + 0.114 * src[i]);
            dst[i] = gray;     // B
            dst[i + 1] = gray; // G
            dst[i + 2] = gray; // R
            dst[i + 3] = src[i + 3]; // A
        }
        return new CapturedFrame(dst, frame.Width, frame.Height, frame.Stride);
    }
}
```

- [ ] **Step 4: Implement `ContrastStretchStage`**

`src/GuildRelay.Platform.Windows/Preprocessing/ContrastStretchStage.cs`:

```csharp
using System;
using GuildRelay.Core.Capture;
using GuildRelay.Core.Preprocessing;

namespace GuildRelay.Platform.Windows.Preprocessing;

public sealed class ContrastStretchStage : IPreprocessStage
{
    private readonly double _low;
    private readonly double _high;

    public ContrastStretchStage(double low = 0.1, double high = 0.9)
    {
        _low = low;
        _high = high;
    }

    public string Name => "contrastStretch";

    public CapturedFrame Apply(CapturedFrame frame)
    {
        var src = frame.BgraPixels;
        var dst = new byte[src.Length];

        // Find min/max luminance (use green channel as proxy after grayscale)
        byte min = 255, max = 0;
        for (int i = 1; i < src.Length; i += 4)
        {
            if (src[i] < min) min = src[i];
            if (src[i] > max) max = src[i];
        }

        var lo = (byte)(min + (max - min) * _low);
        var hi = (byte)(min + (max - min) * _high);
        var range = Math.Max(hi - lo, 1);

        for (int i = 0; i < src.Length; i += 4)
        {
            for (int c = 0; c < 3; c++)
            {
                var val = Math.Clamp((src[i + c] - lo) * 255 / range, 0, 255);
                dst[i + c] = (byte)val;
            }
            dst[i + 3] = src[i + 3];
        }
        return new CapturedFrame(dst, frame.Width, frame.Height, frame.Stride);
    }
}
```

- [ ] **Step 5: Implement `UpscaleStage`**

`src/GuildRelay.Platform.Windows/Preprocessing/UpscaleStage.cs`:

```csharp
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GuildRelay.Core.Capture;
using GuildRelay.Core.Preprocessing;

namespace GuildRelay.Platform.Windows.Preprocessing;

public sealed class UpscaleStage : IPreprocessStage
{
    private readonly int _factor;

    public UpscaleStage(int factor = 2) { _factor = factor; }

    public string Name => "upscale";

    public CapturedFrame Apply(CapturedFrame frame)
    {
        var newW = frame.Width * _factor;
        var newH = frame.Height * _factor;

        using var srcBmp = new Bitmap(frame.Width, frame.Height, frame.Stride,
            PixelFormat.Format32bppArgb, Marshal.UnsafeAddrOfPinnedArrayElement(frame.BgraPixels, 0));
        using var dstBmp = new Bitmap(newW, newH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dstBmp))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(srcBmp, 0, 0, newW, newH);
        }

        var lockBits = dstBmp.LockBits(
            new Rectangle(0, 0, newW, newH),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[lockBits.Stride * lockBits.Height];
            Marshal.Copy(lockBits.Scan0, bytes, 0, bytes.Length);
            return new CapturedFrame(bytes, newW, newH, lockBits.Stride);
        }
        finally
        {
            dstBmp.UnlockBits(lockBits);
        }
    }
}
```

- [ ] **Step 6: Implement `AdaptiveThresholdStage`**

`src/GuildRelay.Platform.Windows/Preprocessing/AdaptiveThresholdStage.cs`:

```csharp
using System;
using GuildRelay.Core.Capture;
using GuildRelay.Core.Preprocessing;

namespace GuildRelay.Platform.Windows.Preprocessing;

public sealed class AdaptiveThresholdStage : IPreprocessStage
{
    private readonly int _blockSize;

    public AdaptiveThresholdStage(int blockSize = 15) { _blockSize = blockSize | 1; /* ensure odd */ }

    public string Name => "adaptiveThreshold";

    public CapturedFrame Apply(CapturedFrame frame)
    {
        var w = frame.Width;
        var h = frame.Height;
        var src = frame.BgraPixels;
        var dst = new byte[src.Length];
        var half = _blockSize / 2;

        // Build integral image of green channel (grayscale proxy)
        var integral = new long[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            long rowSum = 0;
            for (int x = 0; x < w; x++)
            {
                rowSum += src[(y * w + x) * 4 + 1]; // green channel
                integral[(y + 1) * (w + 1) + (x + 1)] = rowSum + integral[y * (w + 1) + (x + 1)];
            }
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var x1 = Math.Max(x - half, 0);
                var y1 = Math.Max(y - half, 0);
                var x2 = Math.Min(x + half, w - 1);
                var y2 = Math.Min(y + half, h - 1);
                var count = (x2 - x1 + 1) * (y2 - y1 + 1);

                var sum = integral[(y2 + 1) * (w + 1) + (x2 + 1)]
                        - integral[y1 * (w + 1) + (x2 + 1)]
                        - integral[(y2 + 1) * (w + 1) + x1]
                        + integral[y1 * (w + 1) + x1];

                var mean = sum / count;
                var val = src[(y * w + x) * 4 + 1]; // green channel
                var output = (byte)(val > mean - 10 ? 255 : 0);

                var idx = (y * w + x) * 4;
                dst[idx] = output;
                dst[idx + 1] = output;
                dst[idx + 2] = output;
                dst[idx + 3] = 255;
            }
        }

        return new CapturedFrame(dst, w, h, w * 4);
    }
}
```

- [ ] **Step 7: Implement `StageFactory`**

`src/GuildRelay.Platform.Windows/Preprocessing/StageFactory.cs`:

```csharp
using System;
using System.Collections.Generic;
using GuildRelay.Core.Config;
using GuildRelay.Core.Preprocessing;

namespace GuildRelay.Platform.Windows.Preprocessing;

public static class StageFactory
{
    public static IPreprocessStage Create(PreprocessStageConfig config)
    {
        return config.Stage.ToLowerInvariant() switch
        {
            "grayscale" => new GrayscaleStage(),
            "contraststretch" => new ContrastStretchStage(
                config.Parameters?.GetValueOrDefault("low", 0.1) ?? 0.1,
                config.Parameters?.GetValueOrDefault("high", 0.9) ?? 0.9),
            "upscale" => new UpscaleStage(
                (int)(config.Parameters?.GetValueOrDefault("factor", 2) ?? 2)),
            "adaptivethreshold" => new AdaptiveThresholdStage(
                (int)(config.Parameters?.GetValueOrDefault("blockSize", 15) ?? 15)),
            _ => throw new ArgumentException($"Unknown preprocess stage: {config.Stage}")
        };
    }

    public static List<IPreprocessStage> CreatePipeline(List<PreprocessStageConfig> configs)
    {
        var stages = new List<IPreprocessStage>();
        foreach (var c in configs)
            stages.Add(Create(c));
        return stages;
    }
}
```

- [ ] **Step 8: Build and test**

```bash
dotnet build && dotnet test
```

Expected: 0 warnings. All existing tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/GuildRelay.Platform.Windows/Preprocessing src/GuildRelay.Core/Preprocessing
git commit -m "feat(platform): add concrete preprocess stages with StageFactory"
```

---

## Task 11: Region picker overlay (App)

**Files:**
- Create: `src/GuildRelay.App/RegionPicker/RegionPickerWindow.xaml`
- Create: `src/GuildRelay.App/RegionPicker/RegionPickerWindow.xaml.cs`

This is a borderless, TopMost, transparent WPF window covering all monitors. The user drags a rubber-band rectangle. No unit tests (pure UI). Build-verified.

- [ ] **Step 1: Create `RegionPickerWindow.xaml`**

`src/GuildRelay.App/RegionPicker/RegionPickerWindow.xaml`:

```xml
<Window x:Class="GuildRelay.App.RegionPicker.RegionPickerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True"
        Background="#66000000" Topmost="True"
        ShowInTaskbar="False" ResizeMode="NoResize"
        WindowState="Maximized"
        MouseLeftButtonDown="OnMouseDown"
        MouseMove="OnMouseMove"
        MouseLeftButtonUp="OnMouseUp"
        KeyDown="OnKeyDown">
    <Canvas x:Name="Overlay">
        <Rectangle x:Name="SelectionRect"
                   Stroke="Lime" StrokeThickness="2"
                   Fill="#33FFFFFF"
                   Visibility="Collapsed"/>
        <TextBlock x:Name="HintText"
                   Text="Drag to select the chat region. Press Escape to cancel."
                   Foreground="White" FontSize="18"
                   Canvas.Left="20" Canvas.Top="20"/>
    </Canvas>
</Window>
```

- [ ] **Step 2: Create `RegionPickerWindow.xaml.cs`**

`src/GuildRelay.App/RegionPicker/RegionPickerWindow.xaml.cs`:

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GuildRelay.App.RegionPicker;

public partial class RegionPickerWindow : Window
{
    private Point _start;
    private bool _dragging;

    public RegionPickerWindow()
    {
        InitializeComponent();
        // Cover full virtual screen (all monitors)
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        WindowState = WindowState.Normal; // override maximized so we control size
    }

    /// <summary>
    /// The selected region in physical screen coordinates, or null if cancelled.
    /// </summary>
    public System.Drawing.Rectangle? SelectedRegion { get; private set; }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Overlay);
        _dragging = true;
        SelectionRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionRect, _start.X);
        Canvas.SetTop(SelectionRect, _start.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var pos = e.GetPosition(Overlay);
        var x = Math.Min(_start.X, pos.X);
        var y = Math.Min(_start.Y, pos.Y);
        var w = Math.Abs(pos.X - _start.X);
        var h = Math.Abs(pos.Y - _start.Y);
        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();

        var pos = e.GetPosition(Overlay);
        var x = (int)Math.Min(_start.X, pos.X);
        var y = (int)Math.Min(_start.Y, pos.Y);
        var w = (int)Math.Abs(pos.X - _start.X);
        var h = (int)Math.Abs(pos.Y - _start.Y);

        if (w > 10 && h > 10) // minimum useful region
        {
            // Convert from WPF coordinates to physical screen coordinates
            var screenX = (int)Left + x;
            var screenY = (int)Top + y;
            SelectedRegion = new System.Drawing.Rectangle(screenX, screenY, w, h);
            DialogResult = true;
        }
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SelectedRegion = null;
            DialogResult = false;
            Close();
        }
    }
}
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build
```

Expected: 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add src/GuildRelay.App/RegionPicker
git commit -m "feat(app): add RegionPickerWindow overlay for chat region selection"
```

---

## Task 12: Chat Watcher config tab + wire into CoreHost

**Files:**
- Create: `src/GuildRelay.App/Config/ChatConfigTab.xaml`
- Create: `src/GuildRelay.App/Config/ChatConfigTab.xaml.cs`
- Modify: `src/GuildRelay.App/Config/ConfigWindow.xaml` (add tab)
- Modify: `src/GuildRelay.App/CoreHost.cs` (register ChatWatcher)

- [ ] **Step 1: Create `ChatConfigTab.xaml`**

`src/GuildRelay.App/Config/ChatConfigTab.xaml`:

```xml
<UserControl x:Class="GuildRelay.App.Config.ChatConfigTab"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Margin="12">
        <CheckBox x:Name="EnabledCheck" Content="Enable Chat Watcher" Margin="0,0,0,8"/>
        <TextBlock Text="Chat region" FontWeight="SemiBold"/>
        <StackPanel Orientation="Horizontal" Margin="0,4,0,8">
            <Button Content="Pick region" Click="OnPickRegion" Padding="12,4" Margin="0,0,8,0"/>
            <TextBlock x:Name="RegionLabel" VerticalAlignment="Center" Foreground="Gray"
                       Text="No region selected"/>
        </StackPanel>
        <TextBlock Text="Capture interval (ms)" FontWeight="SemiBold"/>
        <TextBox x:Name="IntervalBox" Width="100" HorizontalAlignment="Left" Margin="0,4,0,8"/>
        <TextBlock Text="OCR confidence threshold (0.0 - 1.0)" FontWeight="SemiBold"/>
        <TextBox x:Name="ConfidenceBox" Width="100" HorizontalAlignment="Left" Margin="0,4,0,12"/>
        <TextBlock Text="Rules (one per line: label|pattern|regex)" FontWeight="SemiBold"/>
        <TextBox x:Name="RulesBox" AcceptsReturn="True" Height="100"
                 VerticalScrollBarVisibility="Auto" Margin="0,4,0,8"
                 FontFamily="Consolas"/>
        <Button Content="Save Chat Settings" Click="OnSave" Padding="12,4"
                HorizontalAlignment="Left"/>
        <TextBlock x:Name="StatusText" Margin="0,8,0,0" Foreground="Gray"/>
    </StackPanel>
</UserControl>
```

- [ ] **Step 2: Create `ChatConfigTab.xaml.cs`**

`src/GuildRelay.App/Config/ChatConfigTab.xaml.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GuildRelay.App.RegionPicker;
using GuildRelay.Core.Config;

namespace GuildRelay.App.Config;

public partial class ChatConfigTab : UserControl
{
    private CoreHost? _host;
    private RegionConfig _currentRegion = RegionConfig.Empty;

    public ChatConfigTab() { InitializeComponent(); Loaded += OnLoaded; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _host = (DataContext as ConfigViewModel)?.Host;
        if (_host is null) return;

        var chat = _host.Config.Chat;
        EnabledCheck.IsChecked = chat.Enabled;
        IntervalBox.Text = chat.CaptureIntervalMs.ToString();
        ConfidenceBox.Text = chat.OcrConfidenceThreshold.ToString("F2");
        _currentRegion = chat.Region;
        UpdateRegionLabel();

        var ruleLines = chat.Rules.Select(r =>
            $"{r.Label}|{r.Pattern}|{(r.Regex ? "regex" : "literal")}");
        RulesBox.Text = string.Join(Environment.NewLine, ruleLines);
    }

    private void OnPickRegion(object sender, RoutedEventArgs e)
    {
        var picker = new RegionPickerWindow();
        var result = picker.ShowDialog();
        if (result == true && picker.SelectedRegion is { } rect)
        {
            _currentRegion = new RegionConfig(
                rect.X, rect.Y, rect.Width, rect.Height,
                96, // TODO: read actual DPI from DpiHelper in a future improvement
                new ResolutionConfig(1920, 1080),
                "PRIMARY");
            UpdateRegionLabel();
        }
    }

    private void UpdateRegionLabel()
    {
        RegionLabel.Text = _currentRegion.IsEmpty
            ? "No region selected"
            : $"{_currentRegion.X},{_currentRegion.Y} {_currentRegion.Width}x{_currentRegion.Height}";
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        try
        {
            var rules = ParseRules(RulesBox.Text);
            var newChat = _host.Config.Chat with
            {
                Enabled = EnabledCheck.IsChecked ?? false,
                CaptureIntervalMs = int.TryParse(IntervalBox.Text, out var iv) ? iv : 1000,
                OcrConfidenceThreshold = double.TryParse(ConfidenceBox.Text, out var ct) ? ct : 0.65,
                Region = _currentRegion,
                Rules = rules
            };
            var newConfig = _host.Config with { Chat = newChat };
            _host.UpdateConfig(newConfig);
            await _host.ConfigStore.SaveAsync(newConfig);

            // Restart the chat feature if it was running
            await _host.Registry.StopAsync("chat");
            if (newChat.Enabled && !newChat.Region.IsEmpty)
                await _host.Registry.StartAsync("chat", CancellationToken.None);

            StatusText.Text = "Chat settings saved.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private static List<ChatRuleConfig> ParseRules(string text)
    {
        var rules = new List<ChatRuleConfig>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('|');
            if (parts.Length < 2) continue;
            var label = parts[0].Trim();
            var pattern = parts[1].Trim();
            var isRegex = parts.Length > 2 && parts[2].Trim().Equals("regex", StringComparison.OrdinalIgnoreCase);
            rules.Add(new ChatRuleConfig(
                Id: label.ToLowerInvariant().Replace(' ', '_'),
                Label: label,
                Pattern: pattern,
                Regex: isRegex));
        }
        return rules;
    }
}
```

- [ ] **Step 3: Add the Chat Watcher tab to ConfigWindow.xaml**

In `src/GuildRelay.App/Config/ConfigWindow.xaml`, add after the General `TabItem`:

```xml
<TabItem Header="Chat Watcher">
    <local:ChatConfigTab x:Name="ChatTab"/>
</TabItem>
```

Add the `xmlns:local="clr-namespace:GuildRelay.App.Config"` namespace declaration to the `<Window>` element.

- [ ] **Step 4: Wire ChatWatcher into CoreHost**

Modify `src/GuildRelay.App/CoreHost.cs` to add the following in `CreateAsync()`, after the registry is created:

```csharp
// Register Chat Watcher
if (config.Chat is { } chatConfig)
{
    var chatCapture = new GuildRelay.Platform.Windows.Capture.BitBltCapture();
    var chatOcr = new GuildRelay.Platform.Windows.Ocr.WindowsMediaOcrEngine();
    var chatStages = GuildRelay.Platform.Windows.Preprocessing.StageFactory.CreatePipeline(chatConfig.PreprocessPipeline);
    var chatPipeline = new GuildRelay.Features.Chat.Preprocessing.PreprocessPipeline(chatStages);
    var chatWatcher = new GuildRelay.Features.Chat.ChatWatcher(
        chatCapture, chatOcr, chatPipeline, bus, chatConfig, config.General.PlayerName);
    registry.Register(chatWatcher);

    if (chatConfig.Enabled && !chatConfig.Region.IsEmpty)
        await chatWatcher.StartAsync(CancellationToken.None).ConfigureAwait(false);
}
```

Also add the chat template to the publisher's `templateByFeatureId`:

```csharp
var templates = new Dictionary<string, string>
{
    ["test"] = "{matched_text}",
    ["chat"] = config.Chat.Templates.GetValueOrDefault("default", "**{player}** saw [{rule_label}]: `{matched_text}`")
};
```

- [ ] **Step 5: Build and run all tests**

```bash
dotnet build && dotnet test
```

Expected: 0 warnings. All tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/GuildRelay.App/Config/ChatConfigTab.xaml src/GuildRelay.App/Config/ChatConfigTab.xaml.cs src/GuildRelay.App/Config/ConfigWindow.xaml src/GuildRelay.App/CoreHost.cs
git commit -m "feat(app): add Chat Watcher config tab, region picker, and wire into CoreHost"
```

---

## Self-review

**Spec coverage (architecture §5):**
- Periodic capture via `PeriodicTimer` → Task 8 (ChatWatcher.CaptureLoopAsync)
- `IScreenCapture.CaptureRegion` → Task 2 (interface) + Task 9 (BitBltCapture)
- Preprocessing pipeline, data-driven stages → Tasks 6 (pipeline runner) + 10 (concrete stages)
- `IOcrEngine.RecognizeAsync` → Task 2 (interface) + Task 9 (WindowsMediaOcrEngine)
- Confidence threshold filtering → Task 8 (ChatWatcher.ProcessOneTickAsync)
- Text normalization → Task 4 (TextNormalizer)
- LRU dedup → Task 5 (ChatDedup)
- Compiled rules (regex + literal) → Task 3 (CompiledPattern)
- DetectionEvent emission to EventBus → Task 8 (ChatWatcher)
- Chat config DTOs → Task 7
- Region picker overlay → Task 11
- Chat config tab + CoreHost wiring → Task 12
- DPI drift detection → Task 9 (DpiHelper) + needs runtime check in ChatWatcher (noted below)

**Gap found:** DPI drift detection (architecture §5 last bullet) — ChatWatcher should compare current DPI/resolution to the values stored in `RegionConfig` on each tick and enter `Warning` if they differ. This is a ~10-line addition to `ChatWatcher.ProcessOneTickAsync`. The engineer should add it as a refinement after the core pipeline is working. Not critical enough to warrant its own task, but noting it so it isn't forgotten.

**Placeholder scan:** No TBDs found. One `TODO` comment in `ChatConfigTab.xaml.cs` about reading actual DPI — this is a known limitation for the v1 region picker (DPI is hardcoded to 96). Acceptable for now; DpiHelper exists for a future improvement.

**Type consistency check:**
- `CapturedFrame` constructor signature matches across Tasks 2, 5, 8, 9, 10.
- `OcrLine(string Text, float Confidence, RectangleF Bounds)` matches across Tasks 2, 8, 9.
- `CompiledPattern.Create(pattern, isRegex)` / `.IsMatch(input)` matches across Tasks 3 and 8.
- `ChatDedup(capacity)` / `.IsDuplicate(normalized)` / `.Clear()` matches across Tasks 5 and 8.
- `PreprocessPipeline(stages)` / `.Apply(frame)` matches across Tasks 6 and 8.
- `ChatConfig` record fields match between Task 7 definition and Task 8 usage.
- `EventBus.PublishAsync` uses `ValueTask` (void) per the Plan 1 deviation — consistent.
