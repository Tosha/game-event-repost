using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GuildRelay.App.RegionPicker;
using GuildRelay.Core.Config;
using GuildRelay.Core.Rules;
using GuildRelay.Features.Chat;

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
        CooldownBox.Text = chat.DefaultCooldownSec.ToString();
        _currentRegion = chat.Region;
        UpdateRegionLabel();

        var ruleLines = chat.Rules.Select(FormatRule);
        RulesBox.Text = string.Join(Environment.NewLine, ruleLines);

        TemplateCombo.ItemsSource = RuleTemplates.BuiltInNames;
        if (RuleTemplates.BuiltInNames.Count > 0)
            TemplateCombo.SelectedIndex = 0;
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
                dpi,
                new ResolutionConfig(resW, resH),
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

    private void OnLoadTemplate(object sender, RoutedEventArgs e)
    {
        if (TemplateCombo.SelectedItem is not string name) return;
        if (!RuleTemplates.BuiltIn.TryGetValue(name, out var templateRules)) return;

        var existingRules = ParseRules(RulesBox.Text, GetDefaultCooldown());
        var newRules = templateRules.Where(r => !existingRules.Any(er => er.Id == r.Id)).ToList();

        if (newRules.Count == 0)
        {
            StatusText.Text = $"Template \"{name}\" rules already present.";
            return;
        }

        var lines = newRules.Select(FormatRule);
        var block = string.Join(Environment.NewLine, lines);

        var existing = RulesBox.Text.TrimEnd();
        RulesBox.Text = string.IsNullOrEmpty(existing)
            ? block
            : existing + Environment.NewLine + block;

        StatusText.Text = $"Loaded template: {name} ({newRules.Count} rules added)";
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        try
        {
            var defaultCooldown = GetDefaultCooldown();
            var rules = ParseRules(RulesBox.Text, defaultCooldown);
            var newChat = _host.Config.Chat with
            {
                Enabled = EnabledCheck.IsChecked ?? false,
                CaptureIntervalMs = int.TryParse(IntervalBox.Text, out var iv) ? iv : 1000,
                OcrConfidenceThreshold = double.TryParse(ConfidenceBox.Text, out var ct) ? ct : 0.65,
                DefaultCooldownSec = defaultCooldown,
                Region = _currentRegion,
                Rules = rules
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

        var rules = ParseRules(RulesBox.Text, GetDefaultCooldown());
        if (rules.Count == 0)
        {
            TestResultText.Text = "No rules defined. Add rules above first.";
            TestResultText.Foreground = Brushes.Gray;
            return;
        }

        var normalized = TextNormalizer.Normalize(message);

        foreach (var rule in rules)
        {
            var pattern = CompiledPattern.Create(rule.Pattern, rule.Regex);
            if (pattern.IsMatch(normalized))
            {
                TestResultText.Text = $"MATCH: rule \"{rule.Label}\" matched.  Normalized: \"{normalized}\"";
                TestResultText.Foreground = Brushes.LimeGreen;
                return;
            }
        }

        TestResultText.Text = $"No match.  Normalized: \"{normalized}\"";
        TestResultText.Foreground = Brushes.OrangeRed;
    }

    private int GetDefaultCooldown()
        => int.TryParse(CooldownBox.Text, out var cd) ? cd : 600;

    /// <summary>
    /// Format: label|pattern|regex  or  label|pattern|regex|cooldown
    /// Only includes the cooldown field if it differs from the default.
    /// </summary>
    private static string FormatRule(ChatRuleConfig r)
    {
        var type = r.Regex ? "regex" : "literal";
        // Always show cooldown so the user can see/edit it
        return $"{r.Label}|{r.Pattern}|{type}|{r.CooldownSec}";
    }

    /// <summary>
    /// Parses rules. Format: label|pattern|type  or  label|pattern|type|cooldown-sec.
    /// Split limited to 4 parts so | inside regex patterns is preserved.
    /// </summary>
    private static List<ChatRuleConfig> ParseRules(string text, int defaultCooldown)
    {
        var rules = new List<ChatRuleConfig>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('|', 4);
            if (parts.Length < 2) continue;
            var label = parts[0].Trim();
            var pattern = parts[1].Trim();

            // Part 3 could be "regex", "literal", or a number (cooldown with type defaulting to literal)
            var isRegex = false;
            var cooldown = defaultCooldown;

            if (parts.Length >= 3)
            {
                var typePart = parts[2].Trim();
                isRegex = typePart.Equals("regex", StringComparison.OrdinalIgnoreCase);
            }
            if (parts.Length >= 4 && int.TryParse(parts[3].Trim(), out var cd))
            {
                cooldown = cd;
            }

            rules.Add(new ChatRuleConfig(
                Id: label.ToLowerInvariant().Replace(' ', '_'),
                Label: label,
                Pattern: pattern,
                Regex: isRegex,
                CooldownSec: cooldown));
        }
        return rules;
    }
}
