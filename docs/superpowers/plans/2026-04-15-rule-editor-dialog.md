# Rule Editor Dialog + Wildcard Channels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the Chat Watcher rule editor from an inline panel into a modal dialog opened by `+`/`✎`/double-click; treat a rule with empty `Channels` as a wildcard that matches every channel.

**Architecture:** Three units. (1) `RuleEditorLogic` — pure-data helper in `GuildRelay.Features.Chat` that converts dialog field values to a `StructuredChatRule`. (2) `RuleEditorWindow` — `Wpf.Ui.Controls.FluentWindow` modal dialog hosting the form, returns the rule via `DialogResult`. (3) `ChannelMatcher` — partition rules into per-channel buckets and a new `_wildcard` list; `FindMatch` falls through to wildcards after channel-specific lookup misses. `ChatConfigTab` shrinks: inline editor block deleted, three buttons (`+`, `✎`, `—`) sit alongside the rules list.

**Tech Stack:** .NET 8, WPF + WPF-UI, xUnit + FluentAssertions.

**Spec:** [docs/superpowers/specs/2026-04-15-rule-editor-dialog-and-wildcard-channels.md](../specs/2026-04-15-rule-editor-dialog-and-wildcard-channels.md)

---

### Task 1: Wildcard channel matching in `ChannelMatcher`

Partition rules into channel-specific (existing `_byChannel`) and wildcard (new `_wildcard`). `FindMatch` tries the channel-specific bucket first, then falls through to the wildcard list.

**Files:**
- Modify: `src/GuildRelay.Features.Chat/ChannelMatcher.cs`
- Modify: `tests/GuildRelay.Features.Chat.Tests/ChannelMatcherTests.cs`

- [ ] **Step 1: Add four failing tests to `ChannelMatcherTests.cs`**

Append at the end of the class (just before the closing `}`):

```csharp
    [Fact]
    public void EmptyChannelsRuleMatchesAnyChannel()
    {
        var rule = new StructuredChatRule("r1", "Wildcard",
            new List<string>(),                       // no channels = wildcard
            new List<string> { "hello" },
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, "Nave", "Tosh", "hello world");

        var match = matcher.FindMatch(parsed);
        match.Should().NotBeNull();
        match!.Rule.Id.Should().Be("r1");
    }

    [Fact]
    public void EmptyChannelsRuleStillRequiresKeywordMatch()
    {
        var rule = new StructuredChatRule("r1", "Wildcard",
            new List<string>(),
            new List<string> { "never" },
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, "Nave", "Tosh", "hello world");

        matcher.FindMatch(parsed).Should().BeNull();
    }

    [Fact]
    public void EmptyChannelsAndEmptyKeywordsMatchesEveryChannel()
    {
        var rule = new StructuredChatRule("r1", "MatchAll",
            new List<string>(),
            new List<string>(),
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { rule });

        var parsed = new ParsedChatLine(null, "Game", null, "anything goes here");

        matcher.FindMatch(parsed).Should().NotBeNull();
    }

    [Fact]
    public void ChannelSpecificRuleWinsOverWildcard()
    {
        var specific = new StructuredChatRule("specific", "NaveOnly",
            new List<string> { "Nave" },
            new List<string> { "x" },
            MatchMode.ContainsAny);
        var wildcard = new StructuredChatRule("wild", "Wildcard",
            new List<string>(),
            new List<string> { "x" },
            MatchMode.ContainsAny);
        var matcher = new ChannelMatcher(new[] { specific, wildcard });

        var parsed = new ParsedChatLine(null, "Nave", "Tosh", "x");

        var match = matcher.FindMatch(parsed);
        match.Should().NotBeNull();
        match!.Rule.Id.Should().Be("specific");
    }
```

- [ ] **Step 2: Run the new tests — expect failures**

```
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~ChannelMatcherTests" --nologo
```

Expected: 4 FAIL (the wildcard rules currently match nothing because they're never registered).

- [ ] **Step 3: Update `ChannelMatcher.cs` to support wildcards**

Replace the entire body of `src/GuildRelay.Features.Chat/ChannelMatcher.cs` with:

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
    private readonly List<CompiledStructuredRule> _wildcard;

    public ChannelMatcher(IEnumerable<StructuredChatRule> rules)
    {
        _byChannel = new Dictionary<string, List<CompiledStructuredRule>>(StringComparer.OrdinalIgnoreCase);
        _wildcard = new List<CompiledStructuredRule>();

        foreach (var rule in rules)
        {
            var compiled = new CompiledStructuredRule(rule);
            if (rule.Channels.Count == 0)
            {
                _wildcard.Add(compiled);
                continue;
            }
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

        if (_byChannel.TryGetValue(parsed.Channel, out var candidates))
        {
            foreach (var compiled in candidates)
            {
                if (compiled.Matches(parsed.Body))
                    return new ChannelMatchResult(compiled.Rule);
            }
        }

        foreach (var compiled in _wildcard)
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
            if (_keywords is null && _regex is null) return true;
            if (_regex is not null) return _regex.IsMatch(body);
            var lower = body.ToLowerInvariant();
            return _keywords!.Any(k => lower.Contains(k));
        }
    }
}
```

- [ ] **Step 4: Run the full Chat test project — expect pass**

```
dotnet test tests/GuildRelay.Features.Chat.Tests --nologo
```

Expected: all PASS (existing + 4 new).

- [ ] **Step 5: Commit**

```
git add src/GuildRelay.Features.Chat/ChannelMatcher.cs tests/GuildRelay.Features.Chat.Tests/ChannelMatcherTests.cs
git commit -m "feat(chat): empty-channels rules match every channel"
```

---

### Task 2: `RuleEditorLogic` static helper

Pure-data helper that converts dialog input to a `StructuredChatRule`. Mirrors the existing `BuildRuleFromEditor` logic in `ChatConfigTab.xaml.cs`, but lives in the testable Chat assembly.

**Files:**
- Create: `src/GuildRelay.Features.Chat/RuleEditorLogic.cs`
- Create: `tests/GuildRelay.Features.Chat.Tests/RuleEditorLogicTests.cs`

- [ ] **Step 1: Create failing tests**

Path: `tests/GuildRelay.Features.Chat.Tests/RuleEditorLogicTests.cs`

```csharp
using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.Core.Config;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class RuleEditorLogicTests
{
    [Fact]
    public void BuildRuleTrimsAndDefaultsLabel()
    {
        var rule = RuleEditorLogic.BuildRule(
            label: "   ",
            selectedChannels: new[] { "Nave" },
            keywordsText: "x",
            matchMode: MatchMode.ContainsAny,
            cooldownSec: 60);

        rule.Label.Should().Be("Untitled");
        rule.Id.Should().Be("untitled");
    }

    [Fact]
    public void BuildRuleSplitsCommaSeparatedKeywordsForContainsAnyMode()
    {
        var rule = RuleEditorLogic.BuildRule(
            label: "Incoming",
            selectedChannels: new[] { "Nave" },
            keywordsText: "inc, incoming, enemies ",
            matchMode: MatchMode.ContainsAny,
            cooldownSec: 60);

        rule.Keywords.Should().Equal("inc", "incoming", "enemies");
    }

    [Fact]
    public void BuildRuleKeepsEntireTextAsSingleRegex()
    {
        var rule = RuleEditorLogic.BuildRule(
            label: "RegexRule",
            selectedChannels: new[] { "Game" },
            keywordsText: "(plains|sylvan), of meduli",
            matchMode: MatchMode.Regex,
            cooldownSec: 60);

        rule.Keywords.Should().ContainSingle()
            .Which.Should().Be("(plains|sylvan), of meduli");
    }

    [Fact]
    public void BuildRuleProducesIdFromLabel()
    {
        var rule = RuleEditorLogic.BuildRule(
            label: "My Rule",
            selectedChannels: new[] { "Nave" },
            keywordsText: "x",
            matchMode: MatchMode.ContainsAny,
            cooldownSec: 60);

        rule.Id.Should().Be("my_rule");
    }

    [Fact]
    public void BuildRulePreservesEmptyChannels()
    {
        var rule = RuleEditorLogic.BuildRule(
            label: "Wildcard",
            selectedChannels: System.Array.Empty<string>(),
            keywordsText: "hello",
            matchMode: MatchMode.ContainsAny,
            cooldownSec: 60);

        rule.Channels.Should().BeEmpty();
        rule.Keywords.Should().Equal("hello");
    }

    [Fact]
    public void BuildRuleEmptyKeywordsTextProducesEmptyList()
    {
        var rule = RuleEditorLogic.BuildRule(
            label: "MatchAll",
            selectedChannels: System.Array.Empty<string>(),
            keywordsText: "   ",
            matchMode: MatchMode.ContainsAny,
            cooldownSec: 60);

        rule.Keywords.Should().BeEmpty();
    }

    [Fact]
    public void BuildRulePassesThroughCooldown()
    {
        var rule = RuleEditorLogic.BuildRule(
            label: "x",
            selectedChannels: new[] { "Nave" },
            keywordsText: "x",
            matchMode: MatchMode.ContainsAny,
            cooldownSec: 42);

        rule.CooldownSec.Should().Be(42);
    }
}
```

- [ ] **Step 2: Run tests — expect compile errors**

```
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~RuleEditorLogicTests" --nologo
```

Expected: compile error — `RuleEditorLogic` does not exist.

- [ ] **Step 3: Create `src/GuildRelay.Features.Chat/RuleEditorLogic.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using GuildRelay.Core.Config;

namespace GuildRelay.Features.Chat;

/// <summary>
/// Pure-data conversion from rule-editor dialog field values into a
/// <see cref="StructuredChatRule"/>. Lives outside WPF so it can be unit tested.
/// </summary>
public static class RuleEditorLogic
{
    public static StructuredChatRule BuildRule(
        string label,
        IEnumerable<string> selectedChannels,
        string keywordsText,
        MatchMode matchMode,
        int cooldownSec)
    {
        var trimmedLabel = (label ?? string.Empty).Trim();
        if (trimmedLabel.Length == 0) trimmedLabel = "Untitled";

        var channels = selectedChannels?.ToList() ?? new List<string>();

        var trimmedKeywordsText = (keywordsText ?? string.Empty).Trim();

        List<string> keywords;
        if (trimmedKeywordsText.Length == 0)
        {
            keywords = new List<string>();
        }
        else if (matchMode == MatchMode.Regex)
        {
            keywords = new List<string> { trimmedKeywordsText };
        }
        else
        {
            keywords = trimmedKeywordsText
                .Split(',')
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .ToList();
        }

        return new StructuredChatRule(
            Id: trimmedLabel.ToLowerInvariant().Replace(' ', '_'),
            Label: trimmedLabel,
            Channels: channels,
            Keywords: keywords,
            MatchMode: matchMode,
            CooldownSec: cooldownSec);
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

```
dotnet test tests/GuildRelay.Features.Chat.Tests --filter "FullyQualifiedName~RuleEditorLogicTests" --nologo
```

Expected: all 7 PASS.

- [ ] **Step 5: Run the full Chat test project — catch regressions**

```
dotnet test tests/GuildRelay.Features.Chat.Tests --nologo
```

Expected: all PASS.

- [ ] **Step 6: Commit**

```
git add src/GuildRelay.Features.Chat/RuleEditorLogic.cs tests/GuildRelay.Features.Chat.Tests/RuleEditorLogicTests.cs
git commit -m "feat(chat): add testable RuleEditorLogic helper"
```

---

### Task 3: `RuleEditorWindow` modal dialog

Create the WPF dialog. Form fields are lifted verbatim from the existing inline rule editor in `ChatConfigTab.xaml`. The dialog uses `RuleEditorLogic.BuildRule` to produce its result.

**Files:**
- Create: `src/GuildRelay.App/Config/RuleEditorWindow.xaml`
- Create: `src/GuildRelay.App/Config/RuleEditorWindow.xaml.cs`

- [ ] **Step 1: Create `src/GuildRelay.App/Config/RuleEditorWindow.xaml`**

```xml
<ui:FluentWindow x:Class="GuildRelay.App.Config.RuleEditorWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        Title="Add Rule" Width="500" Height="520"
        WindowStartupLocation="CenterOwner"
        ExtendsContentIntoTitleBar="True"
        ShowInTaskbar="False">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <ui:TitleBar Grid.Row="0" x:Name="TitleBarControl" Title="Add Rule"/>

        <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
            <StackPanel Margin="16">
                <TextBlock Text="Label" FontWeight="SemiBold"/>
                <TextBox x:Name="RuleLabelBox" Width="320" HorizontalAlignment="Left" Margin="0,4,0,12"/>

                <TextBlock Text="Channels" FontWeight="SemiBold"/>
                <WrapPanel x:Name="ChannelPanel" Margin="0,4,0,4"/>
                <TextBlock x:Name="WildcardHint"
                           Text="No channels selected → match all channels"
                           Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                           Margin="0,0,0,12" Visibility="Collapsed"/>

                <TextBlock Text="Keywords (comma-separated for ContainsAny, single regex for Regex)" FontWeight="SemiBold"/>
                <TextBox x:Name="KeywordsBox" Height="60" Margin="0,4,0,8"
                         TextWrapping="Wrap" AcceptsReturn="False" FontFamily="Consolas"
                         VerticalScrollBarVisibility="Auto"/>

                <StackPanel Orientation="Horizontal" Margin="0,4,0,12">
                    <RadioButton x:Name="ContainsAnyRadio" Content="Contains any keyword" IsChecked="True" Margin="0,0,16,0"/>
                    <RadioButton x:Name="RegexRadio" Content="Regex"/>
                </StackPanel>

                <TextBlock Text="Cooldown (seconds)" FontWeight="SemiBold"/>
                <TextBox x:Name="RuleCooldownBox" Width="120" HorizontalAlignment="Left" Margin="0,4,0,16"/>

                <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                    <ui:Button Content="Save"   Click="OnSaveClick"   Appearance="Primary" Margin="0,0,8,0" IsDefault="True"/>
                    <ui:Button Content="Cancel" Click="OnCancelClick" IsCancel="True"/>
                </StackPanel>
            </StackPanel>
        </ScrollViewer>
    </Grid>
</ui:FluentWindow>
```

- [ ] **Step 2: Create `src/GuildRelay.App/Config/RuleEditorWindow.xaml.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GuildRelay.Core.Config;
using GuildRelay.Features.Chat;

namespace GuildRelay.App.Config;

public partial class RuleEditorWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly Dictionary<string, CheckBox> _channelChecks = new();
    private StructuredChatRule? _result;

    private RuleEditorWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show the dialog. Pass <paramref name="existing"/>=null for add mode,
    /// or a rule for edit mode. Returns the constructed rule on Save, or
    /// null on Cancel.
    /// </summary>
    public static StructuredChatRule? Show(
        Window owner,
        StructuredChatRule? existing,
        int defaultCooldownSec)
    {
        var dlg = new RuleEditorWindow { Owner = owner };
        dlg.Initialize(existing, defaultCooldownSec);
        var ok = dlg.ShowDialog() == true;
        return ok ? dlg._result : null;
    }

    private void Initialize(StructuredChatRule? existing, int defaultCooldownSec)
    {
        // Build channel checkboxes from the parser's known channels.
        ChannelPanel.Children.Clear();
        _channelChecks.Clear();
        foreach (var ch in ChatLineParser.KnownChannelNames)
        {
            var cb = new CheckBox { Content = ch, Margin = new Thickness(0, 0, 12, 4) };
            cb.Checked   += (_, _) => UpdateWildcardHint();
            cb.Unchecked += (_, _) => UpdateWildcardHint();
            ChannelPanel.Children.Add(cb);
            _channelChecks[ch] = cb;
        }

        if (existing is null)
        {
            // Add mode
            Title = "Add Rule";
            TitleBarControl.Title = "Add Rule";
            RuleLabelBox.Text = string.Empty;
            KeywordsBox.Text = string.Empty;
            ContainsAnyRadio.IsChecked = true;
            RegexRadio.IsChecked = false;
            RuleCooldownBox.Text = defaultCooldownSec.ToString();
        }
        else
        {
            // Edit mode
            Title = $"Edit Rule — {existing.Label}";
            TitleBarControl.Title = $"Edit Rule — {existing.Label}";
            RuleLabelBox.Text = existing.Label;
            foreach (var (ch, cb) in _channelChecks)
                cb.IsChecked = existing.Channels.Contains(ch, StringComparer.OrdinalIgnoreCase);
            KeywordsBox.Text = existing.MatchMode == MatchMode.Regex
                ? (existing.Keywords.Count > 0 ? existing.Keywords[0] : string.Empty)
                : string.Join(", ", existing.Keywords);
            ContainsAnyRadio.IsChecked = existing.MatchMode == MatchMode.ContainsAny;
            RegexRadio.IsChecked       = existing.MatchMode == MatchMode.Regex;
            RuleCooldownBox.Text = existing.CooldownSec.ToString();
        }

        UpdateWildcardHint();
    }

    private void UpdateWildcardHint()
    {
        bool anyChecked = _channelChecks.Values.Any(cb => cb.IsChecked == true);
        WildcardHint.Visibility = anyChecked ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var selectedChannels = _channelChecks
            .Where(kv => kv.Value.IsChecked == true)
            .Select(kv => kv.Key)
            .ToList();

        var matchMode = RegexRadio.IsChecked == true ? MatchMode.Regex : MatchMode.ContainsAny;
        var cooldown = int.TryParse(RuleCooldownBox.Text, out var cd) ? cd : 600;

        _result = RuleEditorLogic.BuildRule(
            label: RuleLabelBox.Text,
            selectedChannels: selectedChannels,
            keywordsText: KeywordsBox.Text,
            matchMode: matchMode,
            cooldownSec: cooldown);

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
```

- [ ] **Step 3: Build the App**

```
dotnet build src/GuildRelay.App --nologo
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Commit**

```
git add src/GuildRelay.App/Config/RuleEditorWindow.xaml src/GuildRelay.App/Config/RuleEditorWindow.xaml.cs
git commit -m "feat(chat-ui): add RuleEditorWindow modal dialog"
```

---

### Task 4: Replace inline editor in `ChatConfigTab` with dialog buttons

Delete the inline editor block. Add `+`, `✎`, `—` buttons in a horizontal stack to the right of the "Active rules" header. Add double-click support on the rules list. Wire up handlers that open `RuleEditorWindow` and update `_rules`.

**Files:**
- Modify: `src/GuildRelay.App/Config/ChatConfigTab.xaml`
- Modify: `src/GuildRelay.App/Config/ChatConfigTab.xaml.cs`

- [ ] **Step 1: Update `ChatConfigTab.xaml`**

Replace the entire `<StackPanel Margin="12">` content (everything between the opening `<StackPanel Margin="12">` after `<ScrollViewer>` and its closing `</StackPanel>`) with:

```xml
        <StackPanel Margin="12">
            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                <ui:ToggleSwitch x:Name="EnabledToggle" IsChecked="False" Checked="OnToggleChanged" Unchecked="OnToggleChanged"/>
                <TextBlock Text="Chat Watcher" VerticalAlignment="Center" FontWeight="SemiBold" FontSize="14" Margin="8,0,0,0"/>
                <ui:Button Content="Live View" Click="OnOpenLiveView" Margin="16,0,0,0" FontSize="11"/>
            </StackPanel>

            <TextBlock Text="Chat region" FontWeight="SemiBold"/>
            <StackPanel Orientation="Horizontal" Margin="0,4,0,12">
                <ui:Button Content="Pick region" Click="OnPickRegion" Margin="0,0,8,0"/>
                <TextBlock x:Name="RegionLabel" VerticalAlignment="Center"
                           Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                           Text="No region selected"/>
            </StackPanel>

            <!-- Rule templates -->
            <TextBlock Text="Rule templates" FontWeight="SemiBold"/>
            <StackPanel Orientation="Horizontal" Margin="0,4,0,12">
                <ComboBox x:Name="TemplateCombo" Width="220" Margin="0,0,8,0"/>
                <ui:Button Content="Load Template" Click="OnLoadTemplate"/>
            </StackPanel>

            <!-- Active rules list with action buttons -->
            <DockPanel Margin="0,0,0,4">
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                    <ui:Button Content="+" Click="OnAddRule" Width="32" Margin="4,0,0,0" ToolTip="Add rule"/>
                    <ui:Button x:Name="EditRuleButton" Content="✎" Click="OnEditRule" Width="32" Margin="4,0,0,0" IsEnabled="False" ToolTip="Edit selected rule"/>
                    <ui:Button x:Name="RemoveRuleButton" Content="—" Click="OnRemoveRule" Width="32" Margin="4,0,0,0" IsEnabled="False" ToolTip="Remove selected rule"/>
                </StackPanel>
                <TextBlock Text="Active rules" FontWeight="SemiBold" VerticalAlignment="Center"/>
            </DockPanel>
            <ListBox x:Name="RulesList" Height="140" Margin="0,4,0,12"
                     SelectionChanged="OnRulesListSelectionChanged"
                     MouseDoubleClick="OnRulesListDoubleClick"/>

            <ui:Button Content="Save Chat Settings" Click="OnSave" Appearance="Primary"
                       HorizontalAlignment="Left"/>

            <!-- Test message -->
            <TextBlock Text="Test a message against your rules" FontWeight="SemiBold" Margin="0,16,0,0"/>
            <StackPanel Orientation="Horizontal" Margin="0,4,0,4">
                <TextBox x:Name="TestMessageBox" Width="350" Margin="0,0,8,0" FontFamily="Consolas"/>
                <ui:Button Content="Test" Click="OnTestMessage"/>
            </StackPanel>
            <TextBlock x:Name="TestResultText" Margin="0,4,0,0"
                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" TextWrapping="Wrap"/>
            <TextBlock x:Name="StatusText" Margin="0,8,0,0"
                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"/>
        </StackPanel>
```

The deleted blocks: rule editor TextBlock label, RuleLabelBox, ChannelPanel, KeywordsBox, ContainsAny/Regex radios, RuleCooldownBox, "Add Rule"/"Update Selected" buttons. The list height grows from 100 to 140 since the editor is gone. The "Remove Selected" button is replaced by the inline `—` icon next to `+` and `✎`.

- [ ] **Step 2: Update `ChatConfigTab.xaml.cs`**

Replace the entire file with:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GuildRelay.App.RegionPicker;
using GuildRelay.Core.Config;
using GuildRelay.Features.Chat;

namespace GuildRelay.App.Config;

public partial class ChatConfigTab : UserControl
{
    private CoreHost? _host;
    private RegionConfig _currentRegion = RegionConfig.Empty;
    private bool _loading;
    private readonly ObservableCollection<StructuredChatRule> _rules = new();
    private DebugLiveView? _debugWindow;

    public ChatConfigTab() { InitializeComponent(); Loaded += OnLoaded; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _host = (DataContext as ConfigViewModel)?.Host;
        if (_host is null) return;

        var chat = _host.Config.Chat;
        _loading = true;
        EnabledToggle.IsChecked = chat.Enabled;
        _loading = false;
        _currentRegion = chat.Region;
        UpdateRegionLabel();

        // Load rules
        _rules.Clear();
        foreach (var r in chat.Rules)
            _rules.Add(r);
        RefreshRulesList();

        TemplateCombo.ItemsSource = RuleTemplates.BuiltInNames;
        if (RuleTemplates.BuiltInNames.Count > 0)
            TemplateCombo.SelectedIndex = 0;
    }

    private void RefreshRulesList()
    {
        RulesList.Items.Clear();
        foreach (var r in _rules)
            RulesList.Items.Add(FormatRuleSummary(r));
        UpdateActionButtons();
    }

    private static string FormatRuleSummary(StructuredChatRule r)
    {
        var channels = r.Channels.Count == 0
            ? "all channels"
            : string.Join(", ", r.Channels);
        var keywords = r.Keywords.Count == 0 ? "all messages" : $"{r.Keywords.Count} keywords";
        var mode = r.MatchMode == MatchMode.Regex ? " (regex)" : "";
        return $"{r.Label}  —  {channels}  —  {keywords}{mode}  —  {r.CooldownSec}s";
    }

    private void UpdateActionButtons()
    {
        bool hasSelection = RulesList.SelectedIndex >= 0;
        EditRuleButton.IsEnabled = hasSelection;
        RemoveRuleButton.IsEnabled = hasSelection;
    }

    private void OnRulesListSelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateActionButtons();

    private void OnRulesListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RulesList.SelectedIndex >= 0)
            OnEditRule(sender, e);
    }

    private void OnAddRule(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        var window = Window.GetWindow(this)!;
        var rule = RuleEditorWindow.Show(window, existing: null, _host.Config.Chat.DefaultCooldownSec);
        if (rule is null) return;
        _rules.Add(rule);
        RefreshRulesList();
        StatusText.Text = $"Added rule: {rule.Label}";
    }

    private void OnEditRule(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        var idx = RulesList.SelectedIndex;
        if (idx < 0 || idx >= _rules.Count) return;

        var window = Window.GetWindow(this)!;
        var rule = RuleEditorWindow.Show(window, existing: _rules[idx], _host.Config.Chat.DefaultCooldownSec);
        if (rule is null) return;
        _rules[idx] = rule;
        RefreshRulesList();
        RulesList.SelectedIndex = idx;
        StatusText.Text = $"Updated rule: {rule.Label}";
    }

    private void OnRemoveRule(object sender, RoutedEventArgs e)
    {
        var idx = RulesList.SelectedIndex;
        if (idx < 0 || idx >= _rules.Count) return;
        var label = _rules[idx].Label;
        _rules.RemoveAt(idx);
        RefreshRulesList();
        StatusText.Text = $"Removed rule: {label}";
    }

    private async void OnToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null) return;
        var enabled = EnabledToggle.IsChecked ?? false;
        var newChat = _host.Config.Chat with { Enabled = enabled };
        var newConfig = _host.Config with { Chat = newChat };
        _host.UpdateConfig(newConfig);
        await _host.ConfigStore.SaveAsync(newConfig);

        await _host.Registry.StopAsync("chat");
        if (enabled && !_host.Config.Chat.Region.IsEmpty)
            await _host.Registry.StartAsync("chat", CancellationToken.None);

        var window = Window.GetWindow(this) as ConfigWindow;
        if (window is not null && DataContext is ConfigViewModel vm)
            window.UpdateIndicators(vm);

        StatusText.Text = enabled ? "Chat Watcher enabled." : "Chat Watcher disabled.";
    }

    private void OnOpenLiveView(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;

        var chatFeature = _host.Registry.Get("chat") as ChatWatcher;
        if (chatFeature is null)
        {
            StatusText.Text = "Chat Watcher not registered.";
            return;
        }

        if (_debugWindow is null || !_debugWindow.IsLoaded)
        {
            _debugWindow = new DebugLiveView();
            _debugWindow.Attach(chatFeature);
            _debugWindow.Show();
        }
        else
        {
            _debugWindow.Activate();
        }

        StatusText.Text = "Live debug view opened. Enable Chat Watcher to see data.";
    }

    private void OnPickRegion(object sender, RoutedEventArgs e)
    {
        var picker = new RegionPickerWindow();
        var result = picker.ShowDialog();
        if (result == true && picker.SelectedRegion is { } rect)
        {
            var dpi = GuildRelay.Platform.Windows.Dpi.DpiHelper.GetPrimaryMonitorDpi();
            var (resW, resH) = GuildRelay.Platform.Windows.Dpi.DpiHelper.GetPrimaryScreenResolution();
            _currentRegion = new RegionConfig(
                rect.X, rect.Y, rect.Width, rect.Height,
                dpi, new ResolutionConfig(resW, resH), "PRIMARY");
            UpdateRegionLabel();
        }
    }

    private void UpdateRegionLabel()
    {
        RegionLabel.Text = _currentRegion.IsEmpty
            ? "No region selected"
            : $"{_currentRegion.X},{_currentRegion.Y} {_currentRegion.Width}x{_currentRegion.Height}";
    }

    private void OnLoadTemplate(object sender, RoutedEventArgs e)
    {
        if (TemplateCombo.SelectedItem is not string name) return;
        if (!RuleTemplates.BuiltIn.TryGetValue(name, out var templateRules)) return;

        var newRules = templateRules.Where(r => !_rules.Any(er => er.Id == r.Id)).ToList();
        if (newRules.Count == 0)
        {
            StatusText.Text = $"Template \"{name}\" rules already present.";
            return;
        }

        foreach (var r in newRules)
            _rules.Add(r);
        RefreshRulesList();
        StatusText.Text = $"Loaded template: {name} ({newRules.Count} rules added)";
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        try
        {
            var newChat = _host.Config.Chat with
            {
                Enabled = EnabledToggle.IsChecked ?? false,
                Region = _currentRegion,
                Rules = _rules.ToList()
            };
            var newConfig = _host.Config with { Chat = newChat };
            _host.UpdateConfig(newConfig);
            await _host.ConfigStore.SaveAsync(newConfig);

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

    private void OnTestMessage(object sender, RoutedEventArgs e)
    {
        var message = TestMessageBox.Text;
        if (string.IsNullOrWhiteSpace(message))
        {
            TestResultText.Text = "Enter a message to test.";
            TestResultText.Foreground = Brushes.Gray;
            return;
        }

        if (_rules.Count == 0)
        {
            TestResultText.Text = "No rules defined. Add rules above first.";
            TestResultText.Foreground = Brushes.Gray;
            return;
        }

        var normalized = TextNormalizer.Normalize(message);
        var parsed = ChatLineParser.Parse(normalized);
        var matcher = new ChannelMatcher(_rules);
        var match = matcher.FindMatch(parsed);

        if (match is not null)
        {
            TestResultText.Text = $"MATCH: rule \"{match.Rule.Label}\" on channel [{parsed.Channel}].  Body: \"{parsed.Body}\"";
            TestResultText.Foreground = Brushes.LimeGreen;
        }
        else
        {
            TestResultText.Text = $"No match.  Channel: [{parsed.Channel ?? "none"}]  Body: \"{parsed.Body}\"";
            TestResultText.Foreground = Brushes.OrangeRed;
        }
    }
}
```

Removed: `_currentRegion`-unrelated leftovers, the `_channelChecks` dictionary, `BuildRuleFromEditor`, the inline-editor handlers (`OnRuleSelected`, the old `OnAddRule`, `OnUpdateRule`), the channel-checkbox build loop in `OnLoaded`, and the default-cooldown population of `RuleCooldownBox`. Added: `UpdateActionButtons`, `OnRulesListSelectionChanged`, `OnRulesListDoubleClick`, new dialog-based `OnAddRule`, new `OnEditRule`. `FormatRuleSummary` now returns `"all channels"` for empty channels.

- [ ] **Step 3: Build the App**

```
dotnet build src/GuildRelay.App --nologo
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Run the full solution test suite — catch regressions**

```
dotnet test --nologo
```

Expected: all PASS.

- [ ] **Step 5: Commit**

```
git add src/GuildRelay.App/Config/ChatConfigTab.xaml src/GuildRelay.App/Config/ChatConfigTab.xaml.cs
git commit -m "feat(chat-ui): replace inline rule editor with dialog + action buttons"
```

---

### Task 5: Final verification

- [ ] **Step 1: Full build**

```
dotnet build --nologo
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 2: Full test suite**

```
dotnet test --nologo
```

Expected: all PASS (baseline 129 + 11 new from Task 1 and Task 2 = 140).

- [ ] **Step 3: Commit any residual cleanup**

```
git status
```

If clean, skip. Otherwise commit with `chore: post-implementation cleanup`.

---

## Acceptance

- A wildcard rule (`Channels = []`) matches every channel and respects keyword/regex filters.
- Channel-specific rules win over wildcards when both could match the same parsed line.
- The Chat tab shows `+`, `✎`, `—` buttons next to "Active rules". `+` opens a blank dialog. `✎` and double-click open the dialog pre-populated. `—` removes the selected row.
- The dialog shows "No channels selected → match all channels" when zero boxes are checked, hides it otherwise.
- The dialog modifies only the in-memory `_rules` collection. **Save Chat Settings** still controls disk persistence and watcher restart.
- Rule list shows `"all channels"` for empty-channel rules.
- All existing tests pass; 11 new tests cover the matcher and the editor logic.

## Manual smoke tests (operator runs after Task 5)

1. `+` with no channels checked, keyword `"test"`, Save → rule appears as `"…  —  all channels  —  …"`.
2. `✎` on existing row → dialog opens pre-populated with that row's values.
3. Double-click a row → same as `✎`.
4. Cancel from the dialog → list unchanged.
5. Add a wildcard rule with keyword `"hello"`. Type `[Nave] [Tosh] hello world` into the Test panel → matches with the wildcard rule's label.
6. Add a `Nave`-specific rule with keyword `"hello"` AND keep the wildcard rule. Type the same line → matches the channel-specific rule (verify by label in the test result).
