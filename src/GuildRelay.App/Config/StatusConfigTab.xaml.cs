using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using GuildRelay.App.RegionPicker;
using GuildRelay.Core.Config;

namespace GuildRelay.App.Config;

public partial class StatusConfigTab : UserControl
{
    private CoreHost? _host;
    private RegionConfig _currentRegion = RegionConfig.Empty;
    private bool _loading;

    public StatusConfigTab() { InitializeComponent(); Loaded += OnLoaded; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _host = (DataContext as ConfigViewModel)?.Host;
        if (_host is null) return;

        var status = _host.Config.Status;
        _loading = true;
        EnabledToggle.IsChecked = status.Enabled;
        _loading = false;
        IntervalBox.Text = status.CaptureIntervalSec.ToString();
        DebounceBox.Text = status.DebounceSamples.ToString();
        _currentRegion = status.Region;
        UpdateRegionLabel();

        var lines = status.DisconnectPatterns.Select(p =>
            $"{p.Label}|{p.Pattern}|{(p.Regex ? "regex" : "literal")}");
        PatternsBox.Text = string.Join(Environment.NewLine, lines);
    }

    private async void OnToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null) return;
        var enabled = EnabledToggle.IsChecked ?? false;
        var newStatus = _host.Config.Status with { Enabled = enabled };
        var newConfig = _host.Config with { Status = newStatus };
        _host.UpdateConfig(newConfig);
        await _host.ConfigStore.SaveAsync(newConfig);

        await _host.Registry.StopAsync("status");
        if (enabled && !_host.Config.Status.Region.IsEmpty)
            await _host.Registry.StartAsync("status", CancellationToken.None);

        var window = Window.GetWindow(this) as ConfigWindow;
        if (window is not null && DataContext is ConfigViewModel vm)
            window.UpdateIndicators(vm);

        StatusText.Text = enabled ? "Status Watcher enabled." : "Status Watcher disabled.";
    }

    private void OnPickRegion(object sender, RoutedEventArgs e)
    {
        var picker = new RegionPickerWindow();
        var result = picker.ShowDialog();
        if (result == true && picker.SelectedRegion is { } rect)
        {
            var dpi = Platform.Windows.Dpi.DpiHelper.GetPrimaryMonitorDpi();
            var (resW, resH) = Platform.Windows.Dpi.DpiHelper.GetPrimaryScreenResolution();
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

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        try
        {
            var patterns = ParsePatterns(PatternsBox.Text);
            var newStatus = _host.Config.Status with
            {
                Enabled = EnabledToggle.IsChecked ?? false,
                CaptureIntervalSec = int.TryParse(IntervalBox.Text, out var iv) ? iv : 5,
                DebounceSamples = int.TryParse(DebounceBox.Text, out var db) ? db : 3,
                Region = _currentRegion,
                DisconnectPatterns = patterns
            };
            var newConfig = _host.Config with { Status = newStatus };
            _host.UpdateConfig(newConfig);
            await _host.ConfigStore.SaveAsync(newConfig);

            await _host.Registry.StopAsync("status");
            if (newStatus.Enabled && !newStatus.Region.IsEmpty)
                await _host.Registry.StartAsync("status", CancellationToken.None);

            StatusText.Text = "Status settings saved.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
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
