using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using GuildRelay.Core.Config;

namespace GuildRelay.App.Config;

public partial class AudioConfigTab : UserControl
{
    private CoreHost? _host;
    private bool _loading;

    public AudioConfigTab() { InitializeComponent(); Loaded += OnLoaded; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _host = (DataContext as ConfigViewModel)?.Host;
        if (_host is null) return;

        var audio = _host.Config.Audio;
        _loading = true;
        EnabledToggle.IsChecked = audio.Enabled;
        _loading = false;

        var ruleLines = audio.Rules.Select(r =>
            $"{r.Label}|{r.ClipPath}|{r.Sensitivity:F2}|{r.CooldownSec}");
        RulesBox.Text = string.Join(Environment.NewLine, ruleLines);
    }

    private async void OnToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null) return;
        var enabled = EnabledToggle.IsChecked ?? false;
        var newAudio = _host.Config.Audio with { Enabled = enabled };
        var newConfig = _host.Config with { Audio = newAudio };
        _host.UpdateConfig(newConfig);
        await _host.ConfigStore.SaveAsync(newConfig);

        await _host.Registry.StopAsync("audio");
        if (enabled && _host.Config.Audio.Rules.Count > 0)
            await _host.Registry.StartAsync("audio", CancellationToken.None);

        var window = Window.GetWindow(this) as ConfigWindow;
        if (window is not null && DataContext is ConfigViewModel vm)
            window.UpdateIndicators(vm);

        StatusText.Text = enabled ? "Audio Watcher enabled." : "Audio Watcher disabled.";
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        try
        {
            var rules = ParseRules(RulesBox.Text);
            var newAudio = _host.Config.Audio with
            {
                Enabled = EnabledToggle.IsChecked ?? false,
                Rules = rules
            };
            var newConfig = _host.Config with { Audio = newAudio };
            _host.UpdateConfig(newConfig);
            await _host.ConfigStore.SaveAsync(newConfig);

            await _host.Registry.StopAsync("audio");
            if (newAudio.Enabled && newAudio.Rules.Count > 0)
                await _host.Registry.StartAsync("audio", CancellationToken.None);

            StatusText.Text = "Audio settings saved.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
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
