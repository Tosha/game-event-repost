using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
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
    private readonly Dictionary<string, CheckBox> _channelChecks = new();
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
        IntervalBox.Text = chat.CaptureIntervalMs.ToString();
        ConfidenceBox.Text = chat.OcrConfidenceThreshold.ToString("F2");
        CooldownBox.Text = chat.DefaultCooldownSec.ToString();
        _currentRegion = chat.Region;
        UpdateRegionLabel();

        // Build channel checkboxes
        ChannelPanel.Children.Clear();
        _channelChecks.Clear();
        foreach (var ch in ChatLineParser.KnownChannelNames)
        {
            var cb = new CheckBox { Content = ch, Margin = new Thickness(0, 0, 12, 4) };
            ChannelPanel.Children.Add(cb);
            _channelChecks[ch] = cb;
        }

        // Load rules
        _rules.Clear();
        foreach (var r in chat.Rules)
            _rules.Add(r);
        RefreshRulesList();

        // Default editor cooldown
        RuleCooldownBox.Text = chat.DefaultCooldownSec.ToString();

        TemplateCombo.ItemsSource = RuleTemplates.BuiltInNames;
        if (RuleTemplates.BuiltInNames.Count > 0)
            TemplateCombo.SelectedIndex = 0;
    }

    private void RefreshRulesList()
    {
        RulesList.Items.Clear();
        foreach (var r in _rules)
            RulesList.Items.Add(FormatRuleSummary(r));
    }

    private static string FormatRuleSummary(StructuredChatRule r)
    {
        var channels = string.Join(", ", r.Channels);
        var keywords = r.Keywords.Count == 0 ? "all messages" : $"{r.Keywords.Count} keywords";
        var mode = r.MatchMode == MatchMode.Regex ? " (regex)" : "";
        return $"{r.Label}  —  {channels}  —  {keywords}{mode}  —  {r.CooldownSec}s";
    }

    private void OnRuleSelected(object sender, SelectionChangedEventArgs e)
    {
        var idx = RulesList.SelectedIndex;
        if (idx < 0 || idx >= _rules.Count) return;
        var rule = _rules[idx];

        RuleLabelBox.Text = rule.Label;
        KeywordsBox.Text = string.Join(", ", rule.Keywords);
        RuleCooldownBox.Text = rule.CooldownSec.ToString();
        ContainsAnyRadio.IsChecked = rule.MatchMode == MatchMode.ContainsAny;
        RegexRadio.IsChecked = rule.MatchMode == MatchMode.Regex;

        foreach (var (ch, cb) in _channelChecks)
            cb.IsChecked = rule.Channels.Contains(ch, StringComparer.OrdinalIgnoreCase);
    }

    private StructuredChatRule BuildRuleFromEditor()
    {
        var label = RuleLabelBox.Text.Trim();
        if (string.IsNullOrEmpty(label)) label = "Untitled";

        var channels = _channelChecks
            .Where(kv => kv.Value.IsChecked == true)
            .Select(kv => kv.Key).ToList();

        var keywordsText = KeywordsBox.Text.Trim();
        var matchMode = RegexRadio.IsChecked == true ? MatchMode.Regex : MatchMode.ContainsAny;

        List<string> keywords;
        if (matchMode == MatchMode.Regex)
        {
            // In regex mode, the entire text is one pattern
            keywords = string.IsNullOrEmpty(keywordsText)
                ? new List<string>()
                : new List<string> { keywordsText };
        }
        else
        {
            keywords = string.IsNullOrEmpty(keywordsText)
                ? new List<string>()
                : keywordsText.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToList();
        }

        var cooldown = int.TryParse(RuleCooldownBox.Text, out var cd) ? cd : GetDefaultCooldown();

        return new StructuredChatRule(
            Id: label.ToLowerInvariant().Replace(' ', '_'),
            Label: label,
            Channels: channels,
            Keywords: keywords,
            MatchMode: matchMode,
            CooldownSec: cooldown);
    }

    private void OnAddRule(object sender, RoutedEventArgs e)
    {
        var rule = BuildRuleFromEditor();
        if (rule.Channels.Count == 0)
        {
            StatusText.Text = "Select at least one channel.";
            return;
        }
        _rules.Add(rule);
        RefreshRulesList();
        StatusText.Text = $"Added rule: {rule.Label}";
    }

    private void OnUpdateRule(object sender, RoutedEventArgs e)
    {
        var idx = RulesList.SelectedIndex;
        if (idx < 0 || idx >= _rules.Count)
        {
            StatusText.Text = "Select a rule to update.";
            return;
        }
        _rules[idx] = BuildRuleFromEditor();
        RefreshRulesList();
        RulesList.SelectedIndex = idx;
        StatusText.Text = "Rule updated.";
    }

    private void OnRemoveRule(object sender, RoutedEventArgs e)
    {
        var idx = RulesList.SelectedIndex;
        if (idx < 0 || idx >= _rules.Count) return;
        _rules.RemoveAt(idx);
        RefreshRulesList();
        StatusText.Text = "Rule removed.";
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
                CaptureIntervalMs = int.TryParse(IntervalBox.Text, out var iv) ? iv : 1000,
                OcrConfidenceThreshold = double.TryParse(ConfidenceBox.Text, out var ct) ? ct : 0.65,
                DefaultCooldownSec = GetDefaultCooldown(),
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

    private int GetDefaultCooldown()
        => int.TryParse(CooldownBox.Text, out var cd) ? cd : 600;
}
