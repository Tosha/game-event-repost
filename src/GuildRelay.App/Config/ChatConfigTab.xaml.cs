using System;
using System.Collections.Generic;
using System.Linq;
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
    private ConfigViewModel? _vm;
    private bool _loading;
    private DebugLiveView? _debugWindow;

    public ChatConfigTab() { InitializeComponent(); Loaded += OnLoaded; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as ConfigViewModel;
        if (_vm is null) return;

        _loading = true;
        var chat = _vm.PendingConfig.Chat;
        EnabledToggle.IsChecked = chat.Enabled;
        UpdateRegionLabel(chat.Region);
        RefreshRulesList();

        TemplateCombo.ItemsSource = RuleTemplates.BuiltInNames;
        if (RuleTemplates.BuiltInNames.Count > 0)
            TemplateCombo.SelectedIndex = 0;
        _loading = false;
    }

    private void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _vm is null) return;
        _vm.SetPendingChat(_vm.PendingConfig.Chat with { Enabled = EnabledToggle.IsChecked ?? false });
    }

    private void OnPickRegion(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var picker = new RegionPickerWindow();
        if (picker.ShowDialog() == true && picker.SelectedRegion is { } rect)
        {
            var dpi = Platform.Windows.Dpi.DpiHelper.GetPrimaryMonitorDpi();
            var (resW, resH) = Platform.Windows.Dpi.DpiHelper.GetPrimaryScreenResolution();
            var region = new RegionConfig(
                rect.X, rect.Y, rect.Width, rect.Height,
                dpi, new ResolutionConfig(resW, resH), "PRIMARY");
            _vm.SetPendingChat(_vm.PendingConfig.Chat with { Region = region });
            UpdateRegionLabel(region);
        }
    }

    private void UpdateRegionLabel(RegionConfig region)
    {
        RegionLabel.Text = region.IsEmpty
            ? "No region selected"
            : $"{region.X},{region.Y} {region.Width}x{region.Height}";
    }

    // --- Rules list ---

    private void RefreshRulesList()
    {
        if (_vm is null) return;
        var selected = RulesList.SelectedIndex;
        RulesList.Items.Clear();
        foreach (var r in _vm.PendingConfig.Chat.Rules)
            RulesList.Items.Add(FormatRuleSummary(r));
        if (selected >= 0 && selected < RulesList.Items.Count)
            RulesList.SelectedIndex = selected;
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
        EditRuleButton.IsEnabled   = hasSelection;
        RemoveRuleButton.IsEnabled = hasSelection;
    }

    private void OnRulesListSelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateActionButtons();

    private void OnRulesListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RulesList.SelectedIndex >= 0) OnEditRule(sender, e);
    }

    private void OnAddRule(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var window = Window.GetWindow(this)!;
        var rule = RuleEditorWindow.Show(window, existing: null, _vm.PendingConfig.Chat.DefaultCooldownSec);
        if (rule is null) return;
        var newRules = new List<StructuredChatRule>(_vm.PendingConfig.Chat.Rules) { rule };
        _vm.SetPendingChat(_vm.PendingConfig.Chat with { Rules = newRules });
        RefreshRulesList();
    }

    private void OnEditRule(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var idx = RulesList.SelectedIndex;
        var rules = _vm.PendingConfig.Chat.Rules;
        if (idx < 0 || idx >= rules.Count) return;

        var window = Window.GetWindow(this)!;
        var rule = RuleEditorWindow.Show(window, existing: rules[idx], _vm.PendingConfig.Chat.DefaultCooldownSec);
        if (rule is null) return;

        var newRules = new List<StructuredChatRule>(rules);
        newRules[idx] = rule;
        _vm.SetPendingChat(_vm.PendingConfig.Chat with { Rules = newRules });
        RefreshRulesList();
        RulesList.SelectedIndex = idx;
    }

    private void OnRemoveRule(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var idx = RulesList.SelectedIndex;
        var rules = _vm.PendingConfig.Chat.Rules;
        if (idx < 0 || idx >= rules.Count) return;

        var newRules = new List<StructuredChatRule>(rules);
        newRules.RemoveAt(idx);
        _vm.SetPendingChat(_vm.PendingConfig.Chat with { Rules = newRules });
        RefreshRulesList();
    }

    private void OnLoadTemplate(object sender, RoutedEventArgs e)
    {
        if (_vm is null || TemplateCombo.SelectedItem is not string name) return;
        if (!RuleTemplates.BuiltIn.TryGetValue(name, out var templateRules)) return;

        var current = _vm.PendingConfig.Chat.Rules;
        var newOnes = templateRules.Where(r => !current.Any(er => er.Id == r.Id)).ToList();
        if (newOnes.Count == 0) return;

        var newRules = new List<StructuredChatRule>(current);
        newRules.AddRange(newOnes);
        _vm.SetPendingChat(_vm.PendingConfig.Chat with { Rules = newRules });
        RefreshRulesList();
    }

    // --- Live view ---

    private void OnOpenLiveView(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var chatFeature = _vm.Host.Registry.Get("chat") as ChatWatcher;
        if (chatFeature is null) return;

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
    }

    // --- Test message (uses PendingConfig's rules only) ---

    private void OnTestMessage(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var message = TestMessageBox.Text;
        if (string.IsNullOrWhiteSpace(message))
        {
            TestResultText.Text = "Enter a message to test.";
            TestResultText.Foreground = Brushes.Gray;
            return;
        }

        var rules = _vm.PendingConfig.Chat.Rules;
        if (rules.Count == 0)
        {
            TestResultText.Text = "No rules defined. Add rules above first.";
            TestResultText.Foreground = Brushes.Gray;
            return;
        }

        var normalized = TextNormalizer.Normalize(message);
        var parsed = ChatLineParser.Parse(normalized);
        var matcher = new ChannelMatcher(rules);
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
