using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GuildRelay.App.RegionPicker;
using GuildRelay.Core.Config;

namespace GuildRelay.App.Config;

public partial class StatusConfigTab : UserControl
{
    private ConfigViewModel? _vm;
    private bool _loading;

    public StatusConfigTab() { InitializeComponent(); Loaded += OnLoaded; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as ConfigViewModel;
        if (_vm is null) return;

        _loading = true;
        var status = _vm.PendingConfig.Status;
        EnabledToggle.IsChecked = status.Enabled;
        IntervalBox.Text  = status.CaptureIntervalSec.ToString();
        DebounceBox.Text  = status.DebounceSamples.ToString();
        UpdateRegionLabel(status.Region);

        var lines = status.DisconnectPatterns.Select(p =>
            $"{p.Label}|{p.Pattern}|{(p.Regex ? "regex" : "literal")}");
        PatternsBox.Text = string.Join(Environment.NewLine, lines);
        _loading = false;
    }

    private void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _vm is null) return;
        _vm.SetPendingStatus(_vm.PendingConfig.Status with { Enabled = EnabledToggle.IsChecked ?? false });
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
            _vm.SetPendingStatus(_vm.PendingConfig.Status with { Region = region });
            UpdateRegionLabel(region);
        }
    }

    private void UpdateRegionLabel(RegionConfig region)
    {
        RegionLabel.Text = region.IsEmpty
            ? "No region selected"
            : $"{region.X},{region.Y} {region.Width}x{region.Height}";
    }

    private void OnFieldChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _vm is null) return;
        var current = _vm.PendingConfig.Status;
        var interval = int.TryParse(IntervalBox.Text, out var iv) ? iv : current.CaptureIntervalSec;
        var debounce = int.TryParse(DebounceBox.Text, out var db) ? db : current.DebounceSamples;
        var patterns = ParsePatterns(PatternsBox.Text);
        _vm.SetPendingStatus(current with
        {
            CaptureIntervalSec  = interval,
            DebounceSamples     = debounce,
            DisconnectPatterns  = patterns
        });
    }

    private static List<DisconnectPatternConfig> ParsePatterns(string text)
    {
        var patterns = new List<DisconnectPatternConfig>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('|');
            if (parts.Length < 2) continue;
            var label = parts[0].Trim();
            var pattern = parts[1].Trim();
            var isRegex = parts.Length > 2 && parts[2].Trim().Equals("regex", StringComparison.OrdinalIgnoreCase);
            patterns.Add(new DisconnectPatternConfig(
                Id: label.ToLowerInvariant().Replace(' ', '_'),
                Label: label,
                Pattern: pattern,
                Regex: isRegex));
        }
        return patterns;
    }
}
