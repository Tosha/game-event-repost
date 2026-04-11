# Foundations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a runnable Windows tray app (C# / .NET 8 / WPF) that can load/save config, post "GuildRelay connected" to a Discord webhook via a "Test webhook" button, and produces an app log and event log — with no detection features yet. This is the foundation the Chat / Audio / Status Watcher plans build on.

**Architecture:** Clean separation between pure Core (contracts, domain types, config, feature registry) and the WPF App shell that wires it all together. No Windows API surface is introduced in this plan — that comes in Plan 2 (Chat Watcher). `DiscordPublisher` is implemented end-to-end with retry/backoff, multipart image support, and secret redaction so subsequent feature plans plug in without touching the publisher. See [`docs/superpowers/specs/2026-04-11-guild-event-relay-architecture.md`](../specs/2026-04-11-guild-event-relay-architecture.md) §1–§13 for the full design.

**Tech Stack:** .NET 8, C# 12, WPF, `System.Text.Json`, Serilog (core + file sink), `Hardcodet.NotifyIcon.Wpf`, xUnit, FluentAssertions, Moq. No Windows-specific APIs in this plan — those arrive in Plan 2.

**Definition of done:**
- `dotnet build -c Release` succeeds.
- `dotnet test` passes for all test projects in this plan.
- Running `GuildRelay.App` shows a tray icon. Right-clicking it reveals Open Config, View Logs folder, Quit.
- On first run, the Config window opens automatically with empty webhook URL and player name.
- Pasting a real Discord webhook URL and clicking "Test webhook" posts a message to Discord and shows a success dialog.
- `%APPDATA%\GuildRelay\config.json` contains the saved URL and name.
- `%APPDATA%\GuildRelay\logs\app-YYYYMMDD.log` exists and contains startup/shutdown entries.
- `%APPDATA%\GuildRelay\logs\events-YYYYMMDD.jsonl` contains one line per test post.
- Nowhere in the app log does the webhook URL appear in cleartext.

---

## File structure

This plan creates the following files (all under repo root):

```
GuildRelay.sln
src/
├── GuildRelay.Core/
│   ├── GuildRelay.Core.csproj
│   ├── Events/
│   │   ├── DetectionEvent.cs
│   │   └── EventBus.cs
│   ├── Features/
│   │   ├── IFeature.cs
│   │   ├── FeatureStatus.cs
│   │   ├── StatusChangedArgs.cs
│   │   ├── FeatureRegistry.cs
│   │   └── WatchdogTask.cs
│   ├── Security/
│   │   └── SecretStore.cs
│   ├── Config/
│   │   ├── AppConfig.cs
│   │   ├── GeneralConfig.cs
│   │   ├── LogsConfig.cs
│   │   └── ConfigStore.cs
│   └── Publishing/
│       └── IDiscordPublisher.cs
├── GuildRelay.Logging/
│   ├── GuildRelay.Logging.csproj
│   ├── LoggingSetup.cs
│   ├── SecretRedactionEnricher.cs
│   └── EventLog.cs
├── GuildRelay.Publisher/
│   ├── GuildRelay.Publisher.csproj
│   ├── TemplateEngine.cs
│   └── DiscordPublisher.cs
└── GuildRelay.App/
    ├── GuildRelay.App.csproj
    ├── App.xaml
    ├── App.xaml.cs
    ├── CoreHost.cs
    ├── Exceptions/
    │   └── GlobalExceptionHandler.cs
    ├── Tray/
    │   ├── TrayView.xaml
    │   └── TrayViewModel.cs
    └── Config/
        ├── ConfigWindow.xaml
        ├── ConfigWindow.xaml.cs
        └── ConfigViewModel.cs
tests/
├── GuildRelay.Core.Tests/
│   ├── GuildRelay.Core.Tests.csproj
│   ├── Events/
│   │   ├── DetectionEventTests.cs
│   │   └── EventBusTests.cs
│   ├── Features/
│   │   ├── FeatureRegistryTests.cs
│   │   └── WatchdogTaskTests.cs
│   ├── Security/
│   │   └── SecretStoreTests.cs
│   └── Config/
│       └── ConfigStoreTests.cs
├── GuildRelay.Logging.Tests/
│   ├── GuildRelay.Logging.Tests.csproj
│   ├── SecretRedactionEnricherTests.cs
│   └── EventLogTests.cs
└── GuildRelay.Publisher.Tests/
    ├── GuildRelay.Publisher.Tests.csproj
    ├── TemplateEngineTests.cs
    └── DiscordPublisherTests.cs
```

**Dependency rule (enforced during scaffold):**
- `GuildRelay.Core` references nothing from our solution and no Windows APIs.
- `GuildRelay.Logging` references `Core` + Serilog.
- `GuildRelay.Publisher` references `Core`.
- `GuildRelay.App` references `Core`, `Logging`, `Publisher`, plus WPF + NotifyIcon.
- Test projects reference their corresponding production project.

---

## Task 1: Solution and project scaffold

**Files:**
- Create: `GuildRelay.sln`
- Create: `src/GuildRelay.Core/GuildRelay.Core.csproj`
- Create: `src/GuildRelay.Logging/GuildRelay.Logging.csproj`
- Create: `src/GuildRelay.Publisher/GuildRelay.Publisher.csproj`
- Create: `src/GuildRelay.App/GuildRelay.App.csproj`
- Create: `tests/GuildRelay.Core.Tests/GuildRelay.Core.Tests.csproj`
- Create: `tests/GuildRelay.Logging.Tests/GuildRelay.Logging.Tests.csproj`
- Create: `tests/GuildRelay.Publisher.Tests/GuildRelay.Publisher.Tests.csproj`

- [ ] **Step 1: Create solution and classlib / WPF projects**

Run from repo root:

```bash
dotnet new sln -n GuildRelay
dotnet new classlib  -n GuildRelay.Core       -o src/GuildRelay.Core       -f net8.0
dotnet new classlib  -n GuildRelay.Logging    -o src/GuildRelay.Logging    -f net8.0
dotnet new classlib  -n GuildRelay.Publisher  -o src/GuildRelay.Publisher  -f net8.0
dotnet new wpf       -n GuildRelay.App        -o src/GuildRelay.App
dotnet new xunit     -n GuildRelay.Core.Tests      -o tests/GuildRelay.Core.Tests      -f net8.0
dotnet new xunit     -n GuildRelay.Logging.Tests   -o tests/GuildRelay.Logging.Tests   -f net8.0
dotnet new xunit     -n GuildRelay.Publisher.Tests -o tests/GuildRelay.Publisher.Tests -f net8.0
```

The `dotnet new wpf` template intentionally omits `-f net8.0`; WPF requires the `net8.0-windows` target framework, which is what the template defaults to. Passing `-f net8.0` would break WPF project generation.

Delete the auto-generated `Class1.cs` / `UnitTest1.cs` files in each project.

Also generate a `.gitignore` for the solution:

```bash
dotnet new gitignore
```

- [ ] **Step 2: Add projects to solution**

```bash
dotnet sln add src/GuildRelay.Core/GuildRelay.Core.csproj
dotnet sln add src/GuildRelay.Logging/GuildRelay.Logging.csproj
dotnet sln add src/GuildRelay.Publisher/GuildRelay.Publisher.csproj
dotnet sln add src/GuildRelay.App/GuildRelay.App.csproj
dotnet sln add tests/GuildRelay.Core.Tests/GuildRelay.Core.Tests.csproj
dotnet sln add tests/GuildRelay.Logging.Tests/GuildRelay.Logging.Tests.csproj
dotnet sln add tests/GuildRelay.Publisher.Tests/GuildRelay.Publisher.Tests.csproj
```

- [ ] **Step 3: Add project references**

```bash
dotnet add src/GuildRelay.Logging    reference src/GuildRelay.Core
dotnet add src/GuildRelay.Publisher  reference src/GuildRelay.Core
dotnet add src/GuildRelay.App        reference src/GuildRelay.Core src/GuildRelay.Logging src/GuildRelay.Publisher
dotnet add tests/GuildRelay.Core.Tests      reference src/GuildRelay.Core
dotnet add tests/GuildRelay.Logging.Tests   reference src/GuildRelay.Logging
dotnet add tests/GuildRelay.Publisher.Tests reference src/GuildRelay.Publisher
```

- [ ] **Step 4: Add package dependencies**

```bash
dotnet add src/GuildRelay.Logging  package Serilog
dotnet add src/GuildRelay.Logging  package Serilog.Sinks.File
dotnet add src/GuildRelay.App      package Hardcodet.NotifyIcon.Wpf  -v 2.0.1
dotnet add tests/GuildRelay.Core.Tests       package FluentAssertions
dotnet add tests/GuildRelay.Logging.Tests    package FluentAssertions
dotnet add tests/GuildRelay.Publisher.Tests  package FluentAssertions
dotnet add tests/GuildRelay.Publisher.Tests  package Moq
```

- [ ] **Step 5: Enable nullable + implicit usings in every project**

For each `.csproj` in `src/` and `tests/`, make sure the `<PropertyGroup>` contains:

```xml
<TargetFramework>net8.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<LangVersion>latest</LangVersion>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

**Exception for `GuildRelay.App.csproj`:** keep the WPF template's `<TargetFramework>net8.0-windows</TargetFramework>` (WPF requires the Windows desktop target), keep `<OutputType>WinExe</OutputType>` and `<UseWPF>true</UseWPF>`, and only add `LangVersion` + `TreatWarningsAsErrors` to its existing PropertyGroup.

- [ ] **Step 6: Build and verify**

```bash
dotnet build
```

Expected: build succeeds, 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add GuildRelay.sln src/ tests/
git commit -m "feat: scaffold GuildRelay solution and project references"
```

---

## Task 2: DetectionEvent record + FeatureStatus + IFeature

**Files:**
- Create: `src/GuildRelay.Core/Events/DetectionEvent.cs`
- Create: `src/GuildRelay.Core/Features/FeatureStatus.cs`
- Create: `src/GuildRelay.Core/Features/StatusChangedArgs.cs`
- Create: `src/GuildRelay.Core/Features/IFeature.cs`
- Create: `tests/GuildRelay.Core.Tests/Events/DetectionEventTests.cs`

- [ ] **Step 1: Write the failing test for DetectionEvent immutability and value equality**

`tests/GuildRelay.Core.Tests/Events/DetectionEventTests.cs`:

```csharp
using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.Core.Events;
using Xunit;

namespace GuildRelay.Core.Tests.Events;

public class DetectionEventTests
{
    [Fact]
    public void TwoEventsWithSameFieldsAreEqual()
    {
        var t = new System.DateTimeOffset(2026, 4, 11, 10, 0, 0, System.TimeSpan.Zero);
        var a = new DetectionEvent("chat", "Incoming", "inc north", t, "Tosh",
            new Dictionary<string, string> { ["source"] = "region" }, ImageAttachment: null);
        var b = new DetectionEvent("chat", "Incoming", "inc north", t, "Tosh",
            new Dictionary<string, string> { ["source"] = "region" }, ImageAttachment: null);

        a.Should().Be(b);
    }

    [Fact]
    public void ImageAttachmentDefaultsToNull()
    {
        var evt = new DetectionEvent("audio", "Horse", "whinny", System.DateTimeOffset.UtcNow,
            "Tosh", new Dictionary<string, string>(), ImageAttachment: null);

        evt.ImageAttachment.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/GuildRelay.Core.Tests
```

Expected: compile error, `DetectionEvent` does not exist.

- [ ] **Step 3: Implement `DetectionEvent`**

`src/GuildRelay.Core/Events/DetectionEvent.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace GuildRelay.Core.Events;

/// <summary>
/// A single detection produced by an <c>IFeature</c>. Flows through the
/// event bus to the Discord publisher and the event log.
/// </summary>
public sealed record DetectionEvent(
    string FeatureId,
    string RuleLabel,
    string MatchedContent,
    DateTimeOffset TimestampUtc,
    string PlayerName,
    IReadOnlyDictionary<string, string> Extras,
    byte[]? ImageAttachment
);
```

- [ ] **Step 4: Implement `FeatureStatus`, `StatusChangedArgs`, and `IFeature`**

`src/GuildRelay.Core/Features/FeatureStatus.cs`:

```csharp
namespace GuildRelay.Core.Features;

public enum FeatureStatus
{
    Idle,
    Running,
    Warning,
    Error,
    Paused
}
```

`src/GuildRelay.Core/Features/StatusChangedArgs.cs`:

```csharp
using System;

namespace GuildRelay.Core.Features;

public sealed class StatusChangedArgs : EventArgs
{
    public StatusChangedArgs(FeatureStatus previous, FeatureStatus current, string? message)
    {
        Previous = previous;
        Current = current;
        Message = message;
    }

    public FeatureStatus Previous { get; }
    public FeatureStatus Current { get; }
    public string? Message { get; }
}
```

`src/GuildRelay.Core/Features/IFeature.cs`:

```csharp
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GuildRelay.Core.Features;

public interface IFeature
{
    string Id { get; }
    string DisplayName { get; }
    FeatureStatus Status { get; }

    Task StartAsync(CancellationToken ct);
    Task StopAsync();
    void ApplyConfig(JsonElement featureConfig);

    event EventHandler<StatusChangedArgs>? StatusChanged;
}
```

- [ ] **Step 5: Run tests to verify pass**

```bash
dotnet test tests/GuildRelay.Core.Tests
```

Expected: 2 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/GuildRelay.Core/Events src/GuildRelay.Core/Features tests/GuildRelay.Core.Tests/Events
git commit -m "feat(core): add DetectionEvent, FeatureStatus, IFeature contracts"
```

---

## Task 3: EventBus (bounded Channel, drop-newest on overflow)

**Files:**
- Create: `src/GuildRelay.Core/Events/EventBus.cs`
- Create: `tests/GuildRelay.Core.Tests/Events/EventBusTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/GuildRelay.Core.Tests/Events/EventBusTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Events;
using Xunit;

namespace GuildRelay.Core.Tests.Events;

public class EventBusTests
{
    private static DetectionEvent Make(string label) => new(
        FeatureId: "chat", RuleLabel: label, MatchedContent: label,
        TimestampUtc: System.DateTimeOffset.UtcNow, PlayerName: "Tosh",
        Extras: new Dictionary<string, string>(), ImageAttachment: null);

    [Fact]
    public async Task PublishedEventCanBeConsumed()
    {
        var bus = new EventBus(capacity: 8);
        await bus.PublishAsync(Make("a"), CancellationToken.None);
        bus.Complete();

        var received = new List<string>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            received.Add(e.RuleLabel);

        received.Should().Equal("a");
    }

    [Fact]
    public async Task OverCapacityPublishDropsNewest()
    {
        var bus = new EventBus(capacity: 2);
        (await bus.PublishAsync(Make("a"), CancellationToken.None)).Should().BeTrue();
        (await bus.PublishAsync(Make("b"), CancellationToken.None)).Should().BeTrue();
        (await bus.PublishAsync(Make("c"), CancellationToken.None)).Should().BeFalse();
        bus.Complete();

        var received = new List<string>();
        await foreach (var e in bus.ConsumeAllAsync(CancellationToken.None))
            received.Add(e.RuleLabel);

        received.Should().Equal("a", "b");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~EventBusTests"
```

Expected: compile error, `EventBus` does not exist.

- [ ] **Step 3: Implement `EventBus`**

`src/GuildRelay.Core/Events/EventBus.cs`:

```csharp
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace GuildRelay.Core.Events;

/// <summary>
/// Bounded in-process queue of <see cref="DetectionEvent"/>. Full buffer
/// drops newly-published events rather than blocking the producer.
/// </summary>
public sealed class EventBus
{
    private readonly Channel<DetectionEvent> _channel;

    public EventBus(int capacity)
    {
        _channel = Channel.CreateBounded<DetectionEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>Returns true if the event was accepted, false if the buffer was full.</summary>
    public ValueTask<bool> PublishAsync(DetectionEvent evt, CancellationToken ct)
        => new(_channel.Writer.TryWrite(evt));

    public async IAsyncEnumerable<DetectionEvent> ConsumeAllAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var evt))
                yield return evt;
        }
    }

    public void Complete() => _channel.Writer.TryComplete();
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~EventBusTests"
```

Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Core/Events/EventBus.cs tests/GuildRelay.Core.Tests/Events/EventBusTests.cs
git commit -m "feat(core): add bounded EventBus with drop-newest overflow"
```

---

## Task 4: SecretStore

**Files:**
- Create: `src/GuildRelay.Core/Security/SecretStore.cs`
- Create: `tests/GuildRelay.Core.Tests/Security/SecretStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/GuildRelay.Core.Tests/Security/SecretStoreTests.cs`:

```csharp
using FluentAssertions;
using GuildRelay.Core.Security;
using Xunit;

namespace GuildRelay.Core.Tests.Security;

public class SecretStoreTests
{
    [Fact]
    public void ReadReturnsStoredValue()
    {
        var store = new SecretStore();
        store.SetWebhookUrl("https://discord.com/api/webhooks/123/abc");

        using var access = store.BorrowWebhookUrl();
        access.Value.Should().Be("https://discord.com/api/webhooks/123/abc");
    }

    [Fact]
    public void ToStringDoesNotLeakSecret()
    {
        var store = new SecretStore();
        store.SetWebhookUrl("https://discord.com/api/webhooks/123/abc");

        store.ToString().Should().NotContain("abc");
        store.ToString().Should().NotContain("webhooks/123");
    }

    [Fact]
    public void EmptyStoreThrowsOnBorrow()
    {
        var store = new SecretStore();
        var act = () => store.BorrowWebhookUrl();
        act.Should().Throw<System.InvalidOperationException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~SecretStoreTests"
```

Expected: compile error, `SecretStore` does not exist.

- [ ] **Step 3: Implement `SecretStore`**

`src/GuildRelay.Core/Security/SecretStore.cs`:

```csharp
using System;

namespace GuildRelay.Core.Security;

/// <summary>
/// Single in-memory holder for the Discord webhook URL. The URL is only
/// accessed via <see cref="BorrowWebhookUrl"/>, and the store's
/// <see cref="ToString"/> is locked to a safe constant so accidental
/// string interpolation or logger format calls cannot leak the secret.
/// </summary>
public sealed class SecretStore
{
    private string? _webhookUrl;

    public void SetWebhookUrl(string? value) => _webhookUrl = value;

    public bool HasWebhookUrl => !string.IsNullOrWhiteSpace(_webhookUrl);

    public WebhookUrlAccess BorrowWebhookUrl()
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl))
            throw new InvalidOperationException("Webhook URL is not configured.");
        return new WebhookUrlAccess(_webhookUrl);
    }

    public override string ToString() => "SecretStore(***)";

    public readonly struct WebhookUrlAccess : IDisposable
    {
        public WebhookUrlAccess(string value) { Value = value; }
        public string Value { get; }
        public void Dispose() { /* no-op; struct present for future hardening */ }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~SecretStoreTests"
```

Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Core/Security tests/GuildRelay.Core.Tests/Security
git commit -m "feat(core): add SecretStore for webhook URL with ToString redaction"
```

---

## Task 5: Config DTOs + ConfigStore

**Files:**
- Create: `src/GuildRelay.Core/Config/AppConfig.cs`
- Create: `src/GuildRelay.Core/Config/GeneralConfig.cs`
- Create: `src/GuildRelay.Core/Config/LogsConfig.cs`
- Create: `src/GuildRelay.Core/Config/ConfigStore.cs`
- Create: `tests/GuildRelay.Core.Tests/Config/ConfigStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/GuildRelay.Core.Tests/Config/ConfigStoreTests.cs`:

```csharp
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Config;
using Xunit;

namespace GuildRelay.Core.Tests.Config;

public class ConfigStoreTests
{
    private static string FreshConfigPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "guildrelay-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "config.json");
    }

    [Fact]
    public async Task LoadMissingFileReturnsDefaultsAndCreatesFile()
    {
        var path = FreshConfigPath();
        var store = new ConfigStore(path);

        var cfg = await store.LoadOrCreateDefaultsAsync();

        cfg.SchemaVersion.Should().Be(1);
        cfg.General.PlayerName.Should().BeEmpty();
        cfg.General.WebhookUrl.Should().BeEmpty();
        cfg.General.GlobalEnabled.Should().BeTrue();
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task SavedConfigRoundTrips()
    {
        var path = FreshConfigPath();
        var store = new ConfigStore(path);

        var cfg = await store.LoadOrCreateDefaultsAsync();
        cfg = cfg with { General = cfg.General with { PlayerName = "Tosh", WebhookUrl = "https://discord.com/api/webhooks/1/x" } };
        await store.SaveAsync(cfg);

        var reopened = await new ConfigStore(path).LoadOrCreateDefaultsAsync();

        reopened.General.PlayerName.Should().Be("Tosh");
        reopened.General.WebhookUrl.Should().Be("https://discord.com/api/webhooks/1/x");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~ConfigStoreTests"
```

Expected: compile error, config types do not exist.

- [ ] **Step 3: Implement config DTOs**

`src/GuildRelay.Core/Config/GeneralConfig.cs`:

```csharp
namespace GuildRelay.Core.Config;

public sealed record GeneralConfig(
    string WebhookUrl,
    string PlayerName,
    bool GlobalEnabled
)
{
    public static GeneralConfig Default => new(WebhookUrl: string.Empty, PlayerName: string.Empty, GlobalEnabled: true);
}
```

`src/GuildRelay.Core/Config/LogsConfig.cs`:

```csharp
namespace GuildRelay.Core.Config;

public sealed record LogsConfig(int RetentionDays, int MaxFileSizeMb)
{
    public static LogsConfig Default => new(RetentionDays: 14, MaxFileSizeMb: 50);
}
```

`src/GuildRelay.Core/Config/AppConfig.cs`:

```csharp
namespace GuildRelay.Core.Config;

public sealed record AppConfig(
    int SchemaVersion,
    GeneralConfig General,
    LogsConfig Logs
)
{
    public static AppConfig Default => new(
        SchemaVersion: 1,
        General: GeneralConfig.Default,
        Logs: LogsConfig.Default);
}
```

- [ ] **Step 4: Implement `ConfigStore`**

`src/GuildRelay.Core/Config/ConfigStore.cs`:

```csharp
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace GuildRelay.Core.Config;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public ConfigStore(string path)
    {
        _path = path;
    }

    public string Path => _path;

    public async Task<AppConfig> LoadOrCreateDefaultsAsync()
    {
        if (!File.Exists(_path))
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            await SaveAsync(AppConfig.Default).ConfigureAwait(false);
            return AppConfig.Default;
        }

        using var stream = File.OpenRead(_path);
        var loaded = await JsonSerializer.DeserializeAsync<AppConfig>(stream, Json).ConfigureAwait(false);
        return loaded ?? AppConfig.Default;
    }

    public async Task SaveAsync(AppConfig config)
    {
        var tmp = _path + ".tmp";
        using (var stream = File.Create(tmp))
            await JsonSerializer.SerializeAsync(stream, config, Json).ConfigureAwait(false);
        File.Copy(tmp, _path, overwrite: true);
        File.Delete(tmp);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~ConfigStoreTests"
```

Expected: 2 passed.

- [ ] **Step 6: Commit**

```bash
git add src/GuildRelay.Core/Config tests/GuildRelay.Core.Tests/Config
git commit -m "feat(core): add AppConfig DTOs and ConfigStore with JSON round-trip"
```

---

## Task 6: WatchdogTask

**Files:**
- Create: `src/GuildRelay.Core/Features/WatchdogTask.cs`
- Create: `tests/GuildRelay.Core.Tests/Features/WatchdogTaskTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/GuildRelay.Core.Tests/Features/WatchdogTaskTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Features;
using Xunit;

namespace GuildRelay.Core.Tests.Features;

public class WatchdogTaskTests
{
    [Fact]
    public async Task ThrowingBodyTransitionsToError()
    {
        var reached = 0;
        var watchdog = new WatchdogTask(
            name: "test",
            body: _ => { reached++; throw new InvalidOperationException("boom"); },
            backoffs: new[] { TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero });

        await watchdog.StartAsync(CancellationToken.None);
        await watchdog.WaitForTerminalAsync(TimeSpan.FromSeconds(2));

        watchdog.State.Should().Be(WatchdogState.Error);
        reached.Should().Be(3);
    }

    [Fact]
    public async Task NormalCompletionTransitionsToStopped()
    {
        var watchdog = new WatchdogTask(
            name: "test",
            body: _ => Task.CompletedTask,
            backoffs: new[] { TimeSpan.Zero });

        await watchdog.StartAsync(CancellationToken.None);
        await watchdog.WaitForTerminalAsync(TimeSpan.FromSeconds(2));

        watchdog.State.Should().Be(WatchdogState.Stopped);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~WatchdogTaskTests"
```

Expected: compile error.

- [ ] **Step 3: Implement `WatchdogTask`**

`src/GuildRelay.Core/Features/WatchdogTask.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GuildRelay.Core.Features;

public enum WatchdogState { Idle, Running, Stopped, Error }

/// <summary>
/// Runs a user-supplied task body with retry + backoff. When the body
/// throws, <see cref="WatchdogTask"/> waits the next backoff interval and
/// restarts. When the supplied backoff list is exhausted, the watchdog
/// gives up and transitions to <see cref="WatchdogState.Error"/>.
/// </summary>
public sealed class WatchdogTask
{
    private readonly string _name;
    private readonly Func<CancellationToken, Task> _body;
    private readonly IReadOnlyList<TimeSpan> _backoffs;
    private readonly TaskCompletionSource _terminal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _cts;

    public WatchdogTask(string name, Func<CancellationToken, Task> body, IReadOnlyList<TimeSpan> backoffs)
    {
        _name = name;
        _body = body;
        _backoffs = backoffs;
    }

    public WatchdogState State { get; private set; } = WatchdogState.Idle;
    public string Name => _name;
    public Exception? LastError { get; private set; }

    public Task StartAsync(CancellationToken outer)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        _ = Task.Run(() => RunLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    public Task WaitForTerminalAsync(TimeSpan timeout)
        => _terminal.Task.WaitAsync(timeout);

    private async Task RunLoopAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt <= _backoffs.Count; attempt++)
        {
            try
            {
                State = WatchdogState.Running;
                await _body(ct).ConfigureAwait(false);
                State = WatchdogState.Stopped;
                _terminal.TrySetResult();
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                State = WatchdogState.Stopped;
                _terminal.TrySetResult();
                return;
            }
            catch (Exception ex)
            {
                LastError = ex;
                if (attempt >= _backoffs.Count - 1)
                {
                    State = WatchdogState.Error;
                    _terminal.TrySetResult();
                    return;
                }
                try
                {
                    await Task.Delay(_backoffs[attempt], ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    State = WatchdogState.Stopped;
                    _terminal.TrySetResult();
                    return;
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~WatchdogTaskTests"
```

Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Core/Features/WatchdogTask.cs tests/GuildRelay.Core.Tests/Features/WatchdogTaskTests.cs
git commit -m "feat(core): add WatchdogTask with backoff and terminal state"
```

---

## Task 7: FeatureRegistry

**Files:**
- Create: `src/GuildRelay.Core/Features/FeatureRegistry.cs`
- Create: `tests/GuildRelay.Core.Tests/Features/FeatureRegistryTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/GuildRelay.Core.Tests/Features/FeatureRegistryTests.cs`:

```csharp
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Features;
using Xunit;

namespace GuildRelay.Core.Tests.Features;

public class FeatureRegistryTests
{
    [Fact]
    public async Task StartingRegisteredFeatureCallsStartAsync()
    {
        var feature = new FakeFeature("chat");
        var registry = new FeatureRegistry();
        registry.Register(feature);

        await registry.StartAsync("chat", CancellationToken.None);

        feature.Started.Should().BeTrue();
    }

    [Fact]
    public async Task StartingUnknownFeatureIsNoOp()
    {
        var registry = new FeatureRegistry();

        var act = async () => await registry.StartAsync("ghost", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void AllReturnsRegisteredFeaturesInOrder()
    {
        var registry = new FeatureRegistry();
        registry.Register(new FakeFeature("chat"));
        registry.Register(new FakeFeature("audio"));

        registry.All.Should().HaveCount(2);
        registry.All[0].Id.Should().Be("chat");
        registry.All[1].Id.Should().Be("audio");
    }

    private sealed class FakeFeature : IFeature
    {
        public FakeFeature(string id) { Id = id; }
        public string Id { get; }
        public string DisplayName => Id;
        public FeatureStatus Status { get; private set; } = FeatureStatus.Idle;
        public bool Started { get; private set; }
        public Task StartAsync(CancellationToken ct) { Started = true; Status = FeatureStatus.Running; return Task.CompletedTask; }
        public Task StopAsync() { Status = FeatureStatus.Idle; return Task.CompletedTask; }
        public void ApplyConfig(JsonElement featureConfig) { }
        public event EventHandler<StatusChangedArgs>? StatusChanged;
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~FeatureRegistryTests"
```

Expected: compile error.

- [ ] **Step 3: Implement `FeatureRegistry`**

`src/GuildRelay.Core/Features/FeatureRegistry.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GuildRelay.Core.Features;

public sealed class FeatureRegistry
{
    private readonly List<IFeature> _features = new();

    public IReadOnlyList<IFeature> All => _features;

    public void Register(IFeature feature) => _features.Add(feature);

    public IFeature? Get(string id) => _features.FirstOrDefault(f => f.Id == id);

    public async Task StartAsync(string id, CancellationToken ct)
    {
        var feature = Get(id);
        if (feature is null) return;
        await feature.StartAsync(ct).ConfigureAwait(false);
    }

    public async Task StopAsync(string id)
    {
        var feature = Get(id);
        if (feature is null) return;
        await feature.StopAsync().ConfigureAwait(false);
    }

    public async Task StopAllAsync()
    {
        foreach (var f in _features)
            await f.StopAsync().ConfigureAwait(false);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/GuildRelay.Core.Tests --filter "FullyQualifiedName~FeatureRegistryTests"
```

Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Core/Features/FeatureRegistry.cs tests/GuildRelay.Core.Tests/Features/FeatureRegistryTests.cs
git commit -m "feat(core): add FeatureRegistry with start/stop and All accessor"
```

---

## Task 8: IDiscordPublisher interface

**Files:**
- Create: `src/GuildRelay.Core/Publishing/IDiscordPublisher.cs`

- [ ] **Step 1: Implement the interface (no test yet; it's shape-only)**

`src/GuildRelay.Core/Publishing/IDiscordPublisher.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using GuildRelay.Core.Events;

namespace GuildRelay.Core.Publishing;

public interface IDiscordPublisher
{
    ValueTask PublishAsync(DetectionEvent evt, CancellationToken ct);
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/GuildRelay.Core
```

Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/GuildRelay.Core/Publishing
git commit -m "feat(core): add IDiscordPublisher interface"
```

---

## Task 9: Serilog setup + secret redaction enricher

**Files:**
- Create: `src/GuildRelay.Logging/SecretRedactionEnricher.cs`
- Create: `src/GuildRelay.Logging/LoggingSetup.cs`
- Create: `tests/GuildRelay.Logging.Tests/SecretRedactionEnricherTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/GuildRelay.Logging.Tests/SecretRedactionEnricherTests.cs`:

```csharp
using System.IO;
using FluentAssertions;
using GuildRelay.Logging;
using Serilog;
using Serilog.Sinks.File;
using Xunit;

namespace GuildRelay.Logging.Tests;

public class SecretRedactionEnricherTests
{
    [Fact]
    public void WebhookUrlIsRedactedFromRenderedOutput()
    {
        var path = Path.Combine(Path.GetTempPath(), "guildrelay-tests", Path.GetRandomFileName() + ".log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var log = new LoggerConfiguration()
            .Enrich.With(new SecretRedactionEnricher())
            .WriteTo.File(path, outputTemplate: "{Message:lj}{NewLine}")
            .CreateLogger();

        log.Information("POSTing to https://discord.com/api/webhooks/123/abc failed");
        log.Dispose();

        var contents = File.ReadAllText(path);
        contents.Should().NotContain("123/abc");
        contents.Should().Contain("https://discord.com/api/webhooks/***");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/GuildRelay.Logging.Tests
```

Expected: compile error.

- [ ] **Step 3: Implement `SecretRedactionEnricher`**

`src/GuildRelay.Logging/SecretRedactionEnricher.cs`:

```csharp
using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace GuildRelay.Logging;

/// <summary>
/// Replaces any Discord webhook URL found in the rendered message with a
/// safe placeholder so secrets never land in the app log.
/// </summary>
public sealed class SecretRedactionEnricher : ILogEventEnricher
{
    private static readonly Regex WebhookPattern = new(
        @"https://discord(?:app)?\.com/api/webhooks/[^\s""']+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var rendered = logEvent.MessageTemplate.Text;
        if (!WebhookPattern.IsMatch(rendered))
            return;

        var redacted = WebhookPattern.Replace(rendered, "https://discord.com/api/webhooks/***");
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("OriginalMessage", rendered));
        // Serilog's message template is immutable; we rewrite by replacing the
        // rendered message via a property the sink can emit.
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("Message", redacted));
    }
}
```

> **Note:** Serilog's `MessageTemplate.Text` is immutable. The simplest
> robust approach is to tell the sink to render from a property we control.
> Update the sink configuration in `LoggingSetup` to use an output template
> that preferences `{Message}` and also applies redaction on the rendered
> output. The test above passes because the `outputTemplate` uses
> `{Message:lj}` which renders the raw template — we'll change the sink to
> redact during write instead.

- [ ] **Step 4: Replace enricher with a write-time redaction wrapper**

Delete the body of `SecretRedactionEnricher.cs` and replace with:

`src/GuildRelay.Logging/SecretRedactionEnricher.cs`:

```csharp
using System.IO;
using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace GuildRelay.Logging;

/// <summary>
/// Text formatter that renders a log event through the inner formatter and
/// then scrubs any Discord webhook URLs from the rendered string.
/// </summary>
public sealed class RedactingTextFormatter : ITextFormatter
{
    private static readonly Regex WebhookPattern = new(
        @"https://discord(?:app)?\.com/api/webhooks/[^\s""']+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ITextFormatter _inner;

    public RedactingTextFormatter(ITextFormatter inner) { _inner = inner; }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var captured = new StringWriter();
        _inner.Format(logEvent, captured);
        var redacted = WebhookPattern.Replace(captured.ToString(), "https://discord.com/api/webhooks/***");
        output.Write(redacted);
    }
}

// Kept for DI wiring even though the file name says "enricher".
public sealed class SecretRedactionEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) { }
}
```

- [ ] **Step 5: Update the test to use the redacting formatter**

Replace `SecretRedactionEnricherTests.cs` with:

```csharp
using System.IO;
using FluentAssertions;
using GuildRelay.Logging;
using Serilog;
using Serilog.Formatting.Display;
using Xunit;

namespace GuildRelay.Logging.Tests;

public class SecretRedactionTests
{
    [Fact]
    public void WebhookUrlIsRedactedFromRenderedOutput()
    {
        var path = Path.Combine(Path.GetTempPath(), "guildrelay-tests", Path.GetRandomFileName() + ".log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var inner = new MessageTemplateTextFormatter("{Message:lj}{NewLine}");
        var formatter = new RedactingTextFormatter(inner);

        var log = new LoggerConfiguration()
            .WriteTo.File(formatter, path)
            .CreateLogger();

        log.Information("POSTing to {Url} failed", "https://discord.com/api/webhooks/123/abc");
        log.Dispose();

        var contents = File.ReadAllText(path);
        contents.Should().NotContain("123/abc");
        contents.Should().Contain("https://discord.com/api/webhooks/***");
    }
}
```

- [ ] **Step 6: Implement `LoggingSetup`**

`src/GuildRelay.Logging/LoggingSetup.cs`:

```csharp
using System;
using System.IO;
using Serilog;
using Serilog.Formatting.Display;

namespace GuildRelay.Logging;

public static class LoggingSetup
{
    public static ILogger CreateAppLogger(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);
        var path = Path.Combine(logsDirectory, "app-.log");
        var inner = new MessageTemplateTextFormatter(
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
        var formatter = new RedactingTextFormatter(inner);

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                formatter,
                path,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();
    }
}
```

- [ ] **Step 7: Run the test to verify it passes**

```bash
dotnet test tests/GuildRelay.Logging.Tests
```

Expected: 1 passed.

- [ ] **Step 8: Commit**

```bash
git add src/GuildRelay.Logging tests/GuildRelay.Logging.Tests
git commit -m "feat(logging): add Serilog setup with webhook URL redaction"
```

---

## Task 10: EventLog JSONL writer

**Files:**
- Create: `src/GuildRelay.Logging/EventLog.cs`
- Create: `tests/GuildRelay.Logging.Tests/EventLogTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/GuildRelay.Logging.Tests/EventLogTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Events;
using GuildRelay.Logging;
using Xunit;

namespace GuildRelay.Logging.Tests;

public class EventLogTests
{
    private static DetectionEvent Sample(string label) => new(
        FeatureId: "chat", RuleLabel: label, MatchedContent: "inc north",
        TimestampUtc: System.DateTimeOffset.UtcNow, PlayerName: "Tosh",
        Extras: new Dictionary<string, string>(), ImageAttachment: null);

    private static string FreshDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "guildrelay-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task AppendWritesOneJsonLinePerEvent()
    {
        var dir = FreshDir();
        var log = new EventLog(dir);

        await log.AppendAsync(Sample("a"), EventPostStatus.Pending);
        await log.AppendAsync(Sample("b"), EventPostStatus.Success);

        var today = $"events-{System.DateTime.UtcNow:yyyyMMdd}.jsonl";
        var lines = File.ReadAllLines(Path.Combine(dir, today));
        lines.Should().HaveCount(2);
        lines.All(l => l.TrimStart().StartsWith("{")).Should().BeTrue();
    }

    [Fact]
    public async Task StatusUpdateAppendsStatusOnlyLine()
    {
        var dir = FreshDir();
        var log = new EventLog(dir);

        var evt = Sample("a");
        await log.AppendAsync(evt, EventPostStatus.Pending);
        await log.UpdateStatusAsync(evt, EventPostStatus.Success);

        var today = $"events-{System.DateTime.UtcNow:yyyyMMdd}.jsonl";
        var lines = File.ReadAllLines(Path.Combine(dir, today));
        lines.Should().HaveCount(2);
        lines[1].Should().Contain("\"status\":\"Success\"");
        lines[1].Should().Contain(evt.RuleLabel);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/GuildRelay.Logging.Tests --filter "FullyQualifiedName~EventLogTests"
```

Expected: compile error.

- [ ] **Step 3: Implement `EventLog`**

`src/GuildRelay.Logging/EventLog.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GuildRelay.Core.Events;

namespace GuildRelay.Logging;

public enum EventPostStatus { Pending, Success, Failed, Dropped }

public sealed class EventLog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory;

    public EventLog(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public Task AppendAsync(DetectionEvent evt, EventPostStatus status)
        => WriteLineAsync(BuildEntry(evt, status, updateOnly: false));

    public Task UpdateStatusAsync(DetectionEvent evt, EventPostStatus status)
        => WriteLineAsync(BuildEntry(evt, status, updateOnly: true));

    private object BuildEntry(DetectionEvent evt, EventPostStatus status, bool updateOnly)
        => updateOnly
            ? (object)new
            {
                kind = "status",
                featureId = evt.FeatureId,
                ruleLabel = evt.RuleLabel,
                timestampUtc = evt.TimestampUtc,
                status = status.ToString()
            }
            : new
            {
                kind = "event",
                featureId = evt.FeatureId,
                ruleLabel = evt.RuleLabel,
                matchedContent = evt.MatchedContent,
                timestampUtc = evt.TimestampUtc,
                playerName = evt.PlayerName,
                extras = evt.Extras,
                status = status.ToString()
            };

    private async Task WriteLineAsync(object entry)
    {
        var json = JsonSerializer.Serialize(entry, Json);
        var name = $"events-{DateTime.UtcNow:yyyyMMdd}.jsonl";
        var path = Path.Combine(_directory, name);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(path, json + Environment.NewLine).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/GuildRelay.Logging.Tests --filter "FullyQualifiedName~EventLogTests"
```

Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Logging/EventLog.cs tests/GuildRelay.Logging.Tests/EventLogTests.cs
git commit -m "feat(logging): add EventLog JSONL writer with status updates"
```

---

## Task 11: Template engine

**Files:**
- Create: `src/GuildRelay.Publisher/TemplateEngine.cs`
- Create: `tests/GuildRelay.Publisher.Tests/TemplateEngineTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/GuildRelay.Publisher.Tests/TemplateEngineTests.cs`:

```csharp
using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.Core.Events;
using GuildRelay.Publisher;
using Xunit;

namespace GuildRelay.Publisher.Tests;

public class TemplateEngineTests
{
    private static DetectionEvent Make(string label, string matched) => new(
        FeatureId: "chat", RuleLabel: label, MatchedContent: matched,
        TimestampUtc: System.DateTimeOffset.UtcNow, PlayerName: "Tosh",
        Extras: new Dictionary<string, string> { ["region"] = "chat_top" },
        ImageAttachment: null);

    [Fact]
    public void KnownPlaceholdersAreSubstituted()
    {
        var engine = new TemplateEngine();
        var evt = Make("Incoming", "inc north");

        var output = engine.Render("**{player}** saw [{rule_label}]: `{matched_text}`", evt);

        output.Should().Be("**Tosh** saw [Incoming]: `inc north`");
    }

    [Fact]
    public void MissingPlaceholderRendersAsEmpty()
    {
        var engine = new TemplateEngine();
        var evt = Make("Incoming", "inc north");

        var output = engine.Render("{nonexistent}{player}", evt);

        output.Should().Be("Tosh");
    }

    [Fact]
    public void ExtrasAreSubstitutable()
    {
        var engine = new TemplateEngine();
        var evt = Make("Incoming", "inc north");

        var output = engine.Render("{player} in {region}", evt);

        output.Should().Be("Tosh in chat_top");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/GuildRelay.Publisher.Tests
```

Expected: compile error.

- [ ] **Step 3: Implement `TemplateEngine`**

`src/GuildRelay.Publisher/TemplateEngine.cs`:

```csharp
using System.Text.RegularExpressions;
using GuildRelay.Core.Events;

namespace GuildRelay.Publisher;

public sealed class TemplateEngine
{
    private static readonly Regex Placeholder = new(@"\{([a-zA-Z_][a-zA-Z0-9_]*)\}", RegexOptions.Compiled);

    public string Render(string template, DetectionEvent evt)
    {
        return Placeholder.Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            return key switch
            {
                "player" => evt.PlayerName,
                "rule_label" => evt.RuleLabel,
                "matched_text" => evt.MatchedContent,
                "feature" => evt.FeatureId,
                "timestamp" => evt.TimestampUtc.ToString("O"),
                _ => evt.Extras.TryGetValue(key, out var v) ? v : string.Empty
            };
        });
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/GuildRelay.Publisher.Tests --filter "FullyQualifiedName~TemplateEngineTests"
```

Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Publisher/TemplateEngine.cs tests/GuildRelay.Publisher.Tests/TemplateEngineTests.cs
git commit -m "feat(publisher): add template engine with event placeholder substitution"
```

---

## Task 12: DiscordPublisher (HTTP post, retry/backoff, multipart)

**Files:**
- Create: `src/GuildRelay.Publisher/DiscordPublisher.cs`
- Create: `tests/GuildRelay.Publisher.Tests/DiscordPublisherTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/GuildRelay.Publisher.Tests/DiscordPublisherTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Events;
using GuildRelay.Core.Security;
using GuildRelay.Publisher;
using Xunit;

namespace GuildRelay.Publisher.Tests;

public class DiscordPublisherTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public List<HttpRequestMessage> Requests { get; } = new();
        public StubHandler(params HttpResponseMessage[] responses) { _responses = new Queue<HttpResponseMessage>(responses); }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Requests.Add(req);
            return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private static DetectionEvent Make(byte[]? image = null) => new(
        FeatureId: "chat", RuleLabel: "Incoming", MatchedContent: "inc north",
        TimestampUtc: DateTimeOffset.UtcNow, PlayerName: "Tosh",
        Extras: new Dictionary<string, string>(), ImageAttachment: image);

    private static SecretStore StoreWithUrl()
    {
        var store = new SecretStore();
        store.SetWebhookUrl("https://discord.com/api/webhooks/1/token");
        return store;
    }

    [Fact]
    public async Task TextEventPostsJsonPayload()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var publisher = new DiscordPublisher(
            new HttpClient(handler),
            StoreWithUrl(),
            new TemplateEngine(),
            new Dictionary<string, string> { ["chat"] = "{player}: {matched_text}" },
            maxRetries: 0,
            initialBackoff: TimeSpan.Zero);

        await publisher.PublishAsync(Make(), CancellationToken.None);

        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task ImageEventPostsMultipart()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var publisher = new DiscordPublisher(
            new HttpClient(handler),
            StoreWithUrl(),
            new TemplateEngine(),
            new Dictionary<string, string> { ["chat"] = "{player}" },
            maxRetries: 0,
            initialBackoff: TimeSpan.Zero);

        await publisher.PublishAsync(Make(image: new byte[] { 1, 2, 3 }), CancellationToken.None);

        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Content!.Headers.ContentType!.MediaType.Should().StartWith("multipart/form-data");
    }

    [Fact]
    public async Task ServerErrorIsRetriedUpToMaxRetries()
    {
        var handler = new StubHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.NoContent));
        var publisher = new DiscordPublisher(
            new HttpClient(handler),
            StoreWithUrl(),
            new TemplateEngine(),
            new Dictionary<string, string> { ["chat"] = "x" },
            maxRetries: 3,
            initialBackoff: TimeSpan.Zero);

        await publisher.PublishAsync(Make(), CancellationToken.None);

        handler.Requests.Should().HaveCount(3);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/GuildRelay.Publisher.Tests --filter "FullyQualifiedName~DiscordPublisherTests"
```

Expected: compile error.

- [ ] **Step 3: Implement `DiscordPublisher`**

Add a reference from `GuildRelay.Publisher` to `GuildRelay.Core` (already present from Task 1) and implement:

`src/GuildRelay.Publisher/DiscordPublisher.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GuildRelay.Core.Events;
using GuildRelay.Core.Publishing;
using GuildRelay.Core.Security;

namespace GuildRelay.Publisher;

public sealed class DiscordPublisher : IDiscordPublisher
{
    private readonly HttpClient _http;
    private readonly SecretStore _secrets;
    private readonly TemplateEngine _templates;
    private readonly IReadOnlyDictionary<string, string> _templateByFeatureId;
    private readonly int _maxRetries;
    private readonly TimeSpan _initialBackoff;

    public DiscordPublisher(
        HttpClient http,
        SecretStore secrets,
        TemplateEngine templates,
        IReadOnlyDictionary<string, string> templateByFeatureId,
        int maxRetries = 4,
        TimeSpan? initialBackoff = null)
    {
        _http = http;
        _secrets = secrets;
        _templates = templates;
        _templateByFeatureId = templateByFeatureId;
        _maxRetries = maxRetries;
        _initialBackoff = initialBackoff ?? TimeSpan.FromSeconds(1);
    }

    public async ValueTask PublishAsync(DetectionEvent evt, CancellationToken ct)
    {
        if (!_secrets.HasWebhookUrl) return;

        var template = _templateByFeatureId.TryGetValue(evt.FeatureId, out var t) ? t : "{player} — {rule_label}";
        var body = _templates.Render(template, evt);

        for (var attempt = 0; attempt <= _maxRetries; attempt++)
        {
            using var request = BuildRequest(evt, body);
            HttpResponseMessage? response = null;
            try
            {
                response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
                    return;
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? _initialBackoff;
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    continue;
                }
                if ((int)response.StatusCode >= 500)
                {
                    if (attempt == _maxRetries) return;
                    await Task.Delay(BackoffFor(attempt), ct).ConfigureAwait(false);
                    continue;
                }
                return; // 4xx other than 429: do not retry
            }
            catch (HttpRequestException) when (attempt < _maxRetries)
            {
                await Task.Delay(BackoffFor(attempt), ct).ConfigureAwait(false);
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    private HttpRequestMessage BuildRequest(DetectionEvent evt, string body)
    {
        using var access = _secrets.BorrowWebhookUrl();
        var request = new HttpRequestMessage(HttpMethod.Post, access.Value);

        if (evt.ImageAttachment is null)
        {
            var payload = JsonSerializer.Serialize(new { content = body });
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }
        else
        {
            var multipart = new MultipartFormDataContent("boundary-" + Guid.NewGuid().ToString("N"));
            var payloadJson = JsonSerializer.Serialize(new { content = body });
            multipart.Add(new StringContent(payloadJson, Encoding.UTF8, "application/json"), "payload_json");
            var file = new ByteArrayContent(evt.ImageAttachment);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            multipart.Add(file, "files[0]", "attachment.png");
            request.Content = multipart;
        }

        return request;
    }

    private TimeSpan BackoffFor(int attempt)
    {
        var ms = _initialBackoff.TotalMilliseconds * Math.Pow(2, attempt);
        return TimeSpan.FromMilliseconds(ms);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test tests/GuildRelay.Publisher.Tests --filter "FullyQualifiedName~DiscordPublisherTests"
```

Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add src/GuildRelay.Publisher/DiscordPublisher.cs tests/GuildRelay.Publisher.Tests/DiscordPublisherTests.cs
git commit -m "feat(publisher): add DiscordPublisher with retry, 429 handling, and multipart support"
```

---

## Task 13: CoreHost composition root

**Files:**
- Create: `src/GuildRelay.App/CoreHost.cs`
- Create: `src/GuildRelay.App/Exceptions/GlobalExceptionHandler.cs`

- [ ] **Step 1: Implement `CoreHost`**

`src/GuildRelay.App/CoreHost.cs`:

```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using GuildRelay.Core.Config;
using GuildRelay.Core.Events;
using GuildRelay.Core.Features;
using GuildRelay.Core.Security;
using GuildRelay.Logging;
using GuildRelay.Publisher;
using Serilog;

namespace GuildRelay.App;

public sealed class CoreHost : IAsyncDisposable
{
    public CoreHost(
        string appDataDirectory,
        ConfigStore configStore,
        AppConfig config,
        SecretStore secrets,
        EventBus bus,
        EventLog eventLog,
        ILogger logger,
        DiscordPublisher publisher,
        FeatureRegistry registry)
    {
        AppDataDirectory = appDataDirectory;
        ConfigStore = configStore;
        Config = config;
        Secrets = secrets;
        Bus = bus;
        EventLog = eventLog;
        Logger = logger;
        Publisher = publisher;
        Registry = registry;
    }

    public string AppDataDirectory { get; }
    public ConfigStore ConfigStore { get; }
    public AppConfig Config { get; private set; }
    public SecretStore Secrets { get; }
    public EventBus Bus { get; }
    public EventLog EventLog { get; }
    public ILogger Logger { get; }
    public DiscordPublisher Publisher { get; }
    public FeatureRegistry Registry { get; }

    public static async Task<CoreHost> CreateAsync()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GuildRelay");
        Directory.CreateDirectory(appData);

        var configStore = new ConfigStore(Path.Combine(appData, "config.json"));
        var config = await configStore.LoadOrCreateDefaultsAsync().ConfigureAwait(false);

        var secrets = new SecretStore();
        secrets.SetWebhookUrl(config.General.WebhookUrl);

        var logger = LoggingSetup.CreateAppLogger(Path.Combine(appData, "logs"));
        var eventLog = new EventLog(Path.Combine(appData, "logs"));
        var bus = new EventBus(capacity: 256);

        var publisher = new DiscordPublisher(
            new HttpClient(),
            secrets,
            new TemplateEngine(),
            templateByFeatureId: new System.Collections.Generic.Dictionary<string, string>
            {
                ["test"] = "{matched_text}"
            });

        var registry = new FeatureRegistry();

        logger.Information("CoreHost initialized at {Path}", appData);
        return new CoreHost(appData, configStore, config, secrets, bus, eventLog, logger, publisher, registry);
    }

    public void UpdateConfig(AppConfig newConfig)
    {
        Config = newConfig;
        Secrets.SetWebhookUrl(newConfig.General.WebhookUrl);
    }

    public async ValueTask DisposeAsync()
    {
        Logger.Information("CoreHost shutting down");
        await Registry.StopAllAsync().ConfigureAwait(false);
        Bus.Complete();
        if (Logger is IDisposable disposable) disposable.Dispose();
    }
}
```

- [ ] **Step 2: Implement `GlobalExceptionHandler`**

`src/GuildRelay.App/Exceptions/GlobalExceptionHandler.cs`:

```csharp
using System;
using System.Threading.Tasks;
using Serilog;

namespace GuildRelay.App.Exceptions;

public static class GlobalExceptionHandler
{
    public static void Hook(ILogger logger)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                logger.Fatal(ex, "Unhandled AppDomain exception");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }
}
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build src/GuildRelay.App
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/GuildRelay.App/CoreHost.cs src/GuildRelay.App/Exceptions
git commit -m "feat(app): add CoreHost composition root and global exception handler"
```

---

## Task 14: WPF App shell + tray icon

**Files:**
- Modify: `src/GuildRelay.App/App.xaml`
- Modify: `src/GuildRelay.App/App.xaml.cs`
- Create: `src/GuildRelay.App/Tray/TrayView.xaml`
- Create: `src/GuildRelay.App/Tray/TrayViewModel.cs`
- Delete: `src/GuildRelay.App/MainWindow.xaml` (+ `.cs`)
- Create: `src/GuildRelay.App/Assets/tray.ico` (placeholder — see Step 5)

- [ ] **Step 1: Remove the auto-generated MainWindow**

```bash
rm src/GuildRelay.App/MainWindow.xaml src/GuildRelay.App/MainWindow.xaml.cs
```

- [ ] **Step 2: Replace `App.xaml`**

`src/GuildRelay.App/App.xaml`:

```xml
<Application x:Class="GuildRelay.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown"
             Startup="OnStartup">
    <Application.Resources/>
</Application>
```

- [ ] **Step 3: Replace `App.xaml.cs`**

`src/GuildRelay.App/App.xaml.cs`:

```csharp
using System;
using System.Threading.Tasks;
using System.Windows;
using GuildRelay.App.Exceptions;
using GuildRelay.App.Tray;

namespace GuildRelay.App;

public partial class App : Application
{
    private CoreHost? _host;
    private TrayView? _trayView;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        _host = await CoreHost.CreateAsync().ConfigureAwait(true);
        GlobalExceptionHandler.Hook(_host.Logger);

        _trayView = new TrayView();
        _trayView.DataContext = new TrayViewModel(_host, OpenConfig, Quit);
        _trayView.Show();

        if (!_host.Secrets.HasWebhookUrl)
            OpenConfig();
    }

    private void OpenConfig()
    {
        var window = new Config.ConfigWindow();
        window.DataContext = new Config.ConfigViewModel(_host!);
        window.Show();
        window.Activate();
    }

    private async void Quit()
    {
        if (_trayView is not null) _trayView.Hide();
        if (_host is not null) await _host.DisposeAsync();
        Shutdown();
    }
}
```

- [ ] **Step 4: Create `TrayView.xaml`**

`src/GuildRelay.App/Tray/TrayView.xaml`:

```xml
<Window x:Class="GuildRelay.App.Tray.TrayView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:tb="http://www.hardcodet.net/taskbar"
        ShowInTaskbar="False" Visibility="Hidden" WindowStyle="None"
        Width="0" Height="0">
    <tb:TaskbarIcon IconSource="pack://application:,,,/Assets/tray.ico"
                    ToolTipText="GuildRelay">
        <tb:TaskbarIcon.ContextMenu>
            <ContextMenu>
                <MenuItem Header="Open Config"      Click="OnOpenConfig"/>
                <MenuItem Header="View Logs folder" Click="OnOpenLogs"/>
                <Separator/>
                <MenuItem Header="Quit"             Click="OnQuit"/>
            </ContextMenu>
        </tb:TaskbarIcon.ContextMenu>
    </tb:TaskbarIcon>
</Window>
```

`src/GuildRelay.App/Tray/TrayView.xaml.cs`:

```csharp
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace GuildRelay.App.Tray;

public partial class TrayView : Window
{
    public TrayView() { InitializeComponent(); }

    private void OnOpenConfig(object sender, RoutedEventArgs e)
        => (DataContext as TrayViewModel)?.OpenConfig();

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as TrayViewModel;
        if (vm is null) return;
        var logsDir = Path.Combine(vm.Host.AppDataDirectory, "logs");
        Process.Start(new ProcessStartInfo("explorer.exe", logsDir) { UseShellExecute = true });
    }

    private void OnQuit(object sender, RoutedEventArgs e)
        => (DataContext as TrayViewModel)?.Quit();
}
```

- [ ] **Step 5: Create a placeholder tray icon**

A minimal 32×32 `.ico` file is required for the build. Use any placeholder image and drop it at `src/GuildRelay.App/Assets/tray.ico`. The simplest reliable source is converting a small PNG via an online converter or a single-color generated file:

```powershell
New-Item -ItemType Directory -Force src/GuildRelay.App/Assets
# Copy any 32x32 .ico file to src/GuildRelay.App/Assets/tray.ico.
# If you do not have one handy, use the Windows calc icon temporarily:
Copy-Item "$env:WINDIR\System32\calc.exe" src/GuildRelay.App/Assets/tray.ico -ErrorAction SilentlyContinue
```

(An actual branded icon is an open item for a later milestone; this task only needs something the build and tray code can load.)

Then add to `GuildRelay.App.csproj` inside an `<ItemGroup>`:

```xml
<Resource Include="Assets\tray.ico"/>
```

- [ ] **Step 6: Create `TrayViewModel.cs`**

`src/GuildRelay.App/Tray/TrayViewModel.cs`:

```csharp
using System;

namespace GuildRelay.App.Tray;

public sealed class TrayViewModel
{
    private readonly Action _openConfig;
    private readonly Action _quit;

    public TrayViewModel(CoreHost host, Action openConfig, Action quit)
    {
        Host = host;
        _openConfig = openConfig;
        _quit = quit;
    }

    public CoreHost Host { get; }

    public void OpenConfig() => _openConfig();
    public void Quit() => _quit();
}
```

- [ ] **Step 7: Build to verify**

```bash
dotnet build src/GuildRelay.App
```

Expected: build succeeds. (The ConfigWindow referenced by `App.OnStartup` is added in Task 15 — temporarily comment out the `OpenConfig` body if build fails here, then re-enable after Task 15.)

- [ ] **Step 8: Commit**

```bash
git add src/GuildRelay.App/App.xaml src/GuildRelay.App/App.xaml.cs src/GuildRelay.App/Tray src/GuildRelay.App/Assets src/GuildRelay.App/GuildRelay.App.csproj
git rm src/GuildRelay.App/MainWindow.xaml src/GuildRelay.App/MainWindow.xaml.cs
git commit -m "feat(app): replace MainWindow with tray-only shell"
```

---

## Task 15: Config window General tab + Test webhook flow

**Files:**
- Create: `src/GuildRelay.App/Config/ConfigWindow.xaml`
- Create: `src/GuildRelay.App/Config/ConfigWindow.xaml.cs`
- Create: `src/GuildRelay.App/Config/ConfigViewModel.cs`

- [ ] **Step 1: Create `ConfigWindow.xaml`**

`src/GuildRelay.App/Config/ConfigWindow.xaml`:

```xml
<Window x:Class="GuildRelay.App.Config.ConfigWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="GuildRelay — Config" Width="520" Height="360"
        WindowStartupLocation="CenterScreen">
    <TabControl Margin="8">
        <TabItem Header="General">
            <StackPanel Margin="12">
                <TextBlock Text="Discord webhook URL" FontWeight="SemiBold"/>
                <PasswordBox x:Name="WebhookBox" Height="26" Margin="0,4,0,12"/>
                <TextBlock Text="Player name" FontWeight="SemiBold"/>
                <TextBox     x:Name="PlayerBox"  Height="26" Margin="0,4,0,12"/>
                <StackPanel Orientation="Horizontal">
                    <Button Content="Test webhook" Click="OnTestWebhookClick" Padding="12,4" Margin="0,0,8,0"/>
                    <Button Content="Save"         Click="OnSaveClick"        Padding="12,4" Margin="0,0,8,0"/>
                    <Button Content="Close"        Click="OnCloseClick"       Padding="12,4"/>
                </StackPanel>
                <TextBlock x:Name="StatusText" Margin="0,12,0,0" Foreground="Gray"/>
            </StackPanel>
        </TabItem>
    </TabControl>
</Window>
```

- [ ] **Step 2: Create `ConfigWindow.xaml.cs`**

`src/GuildRelay.App/Config/ConfigWindow.xaml.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using GuildRelay.Core.Events;

namespace GuildRelay.App.Config;

public partial class ConfigWindow : Window
{
    public ConfigWindow() { InitializeComponent(); Loaded += OnLoaded; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        WebhookBox.Password = vm.WebhookUrl;
        PlayerBox.Text      = vm.PlayerName;
    }

    private async void OnTestWebhookClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        StatusText.Text = "Testing…";
        try
        {
            vm.WebhookUrl = WebhookBox.Password;
            vm.PlayerName = PlayerBox.Text;
            vm.Apply();

            await vm.Host.Publisher.PublishAsync(new DetectionEvent(
                FeatureId: "test",
                RuleLabel: "Connection test",
                MatchedContent: $"GuildRelay connected — hello from {vm.PlayerName}",
                TimestampUtc: DateTimeOffset.UtcNow,
                PlayerName: vm.PlayerName,
                Extras: new Dictionary<string, string>(),
                ImageAttachment: null), CancellationToken.None);

            StatusText.Text = "Test message sent.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Test failed: " + ex.Message;
            vm.Host.Logger.Error(ex, "Test webhook post failed");
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        vm.WebhookUrl = WebhookBox.Password;
        vm.PlayerName = PlayerBox.Text;
        await vm.SaveAsync();
        StatusText.Text = "Saved.";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 3: Create `ConfigViewModel.cs`**

`src/GuildRelay.App/Config/ConfigViewModel.cs`:

```csharp
using System.Threading.Tasks;
using GuildRelay.Core.Config;

namespace GuildRelay.App.Config;

public sealed class ConfigViewModel
{
    public ConfigViewModel(CoreHost host)
    {
        Host = host;
        WebhookUrl = host.Config.General.WebhookUrl;
        PlayerName = host.Config.General.PlayerName;
    }

    public CoreHost Host { get; }
    public string WebhookUrl { get; set; }
    public string PlayerName { get; set; }

    public void Apply()
    {
        var next = Host.Config with
        {
            General = Host.Config.General with
            {
                WebhookUrl = WebhookUrl,
                PlayerName = PlayerName
            }
        };
        Host.UpdateConfig(next);
    }

    public async Task SaveAsync()
    {
        Apply();
        await Host.ConfigStore.SaveAsync(Host.Config);
    }
}
```

- [ ] **Step 4: Build and run**

```bash
dotnet build
dotnet run --project src/GuildRelay.App
```

Expected: build succeeds; app launches; tray icon appears; the Config window opens automatically on first run because no webhook URL is set.

- [ ] **Step 5: Smoke test**

1. Paste a real Discord webhook URL into the Webhook field.
2. Enter a player name.
3. Click **Test webhook**. Expected: status line shows "Test message sent." and a message appears in the target Discord channel.
4. Click **Save**, then Close.
5. Right-click tray → **View Logs folder**. Verify `app-YYYYMMDD.log` exists and contains a startup entry plus a "Test webhook post" entry. Open the log and confirm the webhook URL appears as `https://discord.com/api/webhooks/***` (redacted), not in cleartext.
6. Verify `events-YYYYMMDD.jsonl` contains at least one JSON line with `"featureId":"test"`.
7. Right-click tray → **Quit**. App exits cleanly.

- [ ] **Step 6: Commit**

```bash
git add src/GuildRelay.App/Config
git commit -m "feat(app): add Config window with webhook/test/save flow"
```

---

## Self-review

**Spec coverage (architecture §1–§13):**
- §1 Overview / §2 Tech choices → Task 1 (scaffold), Task 9 (Serilog).
- §3 Component map → CoreHost (Task 13) wires Bus, EventLog, Publisher, Registry.
- §4 Core contracts → Tasks 2 (DetectionEvent, IFeature, FeatureStatus), 8 (IDiscordPublisher). `IOcrEngine`, `IAudioMatcher`, `IScreenCapture` intentionally deferred to Plans 2 and 3 (they belong to their respective feature plans).
- §5 Chat Watcher / §6 Audio Watcher / §7 Status Watcher → deferred to Plans 2, 3, 4 as explicitly stated in the plan header.
- §8 Discord publisher + event log → Tasks 10, 11, 12.
- §9 UI → Tasks 14, 15. Region picker overlay deferred to Plan 2 (first feature that needs a region).
- §10 Lifecycle, watchdog → Task 6 (WatchdogTask), Task 13 (CoreHost startup/shutdown), Task 13 (GlobalExceptionHandler).
- §11 Config schema → Task 5 covers the General + Logs sections. Feature-specific config blocks are added in their respective plans.
- §12 Project layout → Task 1 sets up `Core`, `Logging`, `Publisher`, `App`. `Platform.Windows`, `Features.Chat`, `Features.Audio`, `Features.Status` are added in their own plans.
- §13 Anti-cheat audit → enforced implicitly: this plan introduces zero Windows APIs. The dependency rule in Task 1 keeps `Core` platform-agnostic.

**Placeholder scan:** no TBDs, no "add appropriate error handling", every code step contains the actual code.

**Type consistency check:**
- `DetectionEvent` signature is used consistently across Tasks 2, 10, 11, 12, 15.
- `FeatureStatus` enum values match between Task 2 and any future consumer.
- `SecretStore.BorrowWebhookUrl()` return type is `WebhookUrlAccess`, used as `using var access = …` in both tests (Task 4) and `DiscordPublisher.BuildRequest` (Task 12).
- `EventLog.AppendAsync(DetectionEvent, EventPostStatus)` signature matches between Task 10 tests and implementation.
- `ConfigStore` constructor and method names (`LoadOrCreateDefaultsAsync`, `SaveAsync`, `Path`) match between Tasks 5 and 13.

**Out of scope for this plan (explicit, not gaps):**
- Region picker, OCR, BitBlt capture, WASAPI capture, NWaves matcher.
- `Features.Chat`, `Features.Audio`, `Features.Status` code.
- The `IOcrEngine`, `IAudioMatcher`, `IScreenCapture` interfaces (they land in their respective feature plans alongside their first implementations).
- A branded tray icon (placeholder in Task 14).
- Installer / MSI packaging.

These are tracked for subsequent plans (Plans 2–4 per the scope note at the top of this document).
