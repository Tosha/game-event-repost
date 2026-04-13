using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

        // Check for duplicates by Id
        var existingRules = ParseRules(RulesBox.Text);
        var newRules = templateRules.Where(r => !existingRules.Any(er => er.Id == r.Id)).ToList();

        if (newRules.Count == 0)
        {
            StatusText.Text = $"Template \"{name}\" rules already present.";
            return;
        }

        var lines = newRules.Select(r =>
            $"{r.Label}|{r.Pattern}|{(r.Regex ? "regex" : "literal")}");
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
            // Split into at most 3 parts so | inside regex patterns is preserved
            var parts = line.Trim().Split('|', 3);
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
