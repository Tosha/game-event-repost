using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GuildRelay.Core.Config;

namespace GuildRelay.App.Config;

public partial class AudioConfigTab : UserControl
{
    private ConfigViewModel? _vm;
    private bool _loading;

    public AudioConfigTab() { InitializeComponent(); Loaded += OnLoaded; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as ConfigViewModel;
        if (_vm is null) return;

        _loading = true;
        var audio = _vm.PendingConfig.Audio;
        EnabledToggle.IsChecked = audio.Enabled;

        var ruleLines = audio.Rules.Select(r =>
            $"{r.Label}|{r.ClipPath}|{r.Sensitivity.ToString("F2", CultureInfo.InvariantCulture)}|{r.CooldownSec}");
        RulesBox.Text = string.Join(Environment.NewLine, ruleLines);
        _loading = false;
    }

    private void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _vm is null) return;
        _vm.SetPendingAudio(_vm.PendingConfig.Audio with { Enabled = EnabledToggle.IsChecked ?? false });
    }

    private void OnRulesChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _vm is null) return;
        var rules = ParseRules(RulesBox.Text);
        _vm.SetPendingAudio(_vm.PendingConfig.Audio with { Rules = rules });
    }

    private static List<AudioRuleConfig> ParseRules(string text)
    {
        var rules = new List<AudioRuleConfig>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('|', 4);
            if (parts.Length < 4) continue;
            var label = parts[0].Trim();
            var clipPath = parts[1].Trim();
            var sensitivity = double.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0.8;
            var cooldown = int.TryParse(parts[3].Trim(), out var c) ? c : 15;
            rules.Add(new AudioRuleConfig(
                Id: label.ToLowerInvariant().Replace(' ', '_'),
                Label: label,
                ClipPath: clipPath,
                Sensitivity: sensitivity,
                CooldownSec: cooldown));
        }
        return rules;
    }
}
