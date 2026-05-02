using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GuildRelay.Core.Config;
using GuildRelay.Features.Chat;

namespace GuildRelay.App.Config;

public partial class CounterRuleEditorWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly Dictionary<string, CheckBox> _channelChecks = new();
    private CounterRule? _result;

    private CounterRuleEditorWindow() { InitializeComponent(); }

    public static CounterRule? Show(Window owner, CounterRule? existing)
    {
        var dlg = new CounterRuleEditorWindow { Owner = owner };
        dlg.Initialize(existing);
        return dlg.ShowDialog() == true ? dlg._result : null;
    }

    private void Initialize(CounterRule? existing)
    {
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
            Title = "Add Counter Rule";
            TitleBarControl.Title = "Add Counter Rule";
            LabelBox.Text = string.Empty;
            PatternBox.Text = string.Empty;
            // Default channel: Game (most common for counter rules).
            if (_channelChecks.TryGetValue("Game", out var gameCb)) gameCb.IsChecked = true;
            TemplateRadio.IsChecked = true;
        }
        else
        {
            Title = $"Edit Counter Rule — {existing.Label}";
            TitleBarControl.Title = $"Edit Counter Rule — {existing.Label}";
            LabelBox.Text = existing.Label;
            PatternBox.Text = existing.Pattern;
            foreach (var (ch, cb) in _channelChecks)
                cb.IsChecked = existing.Channels.Contains(ch, StringComparer.OrdinalIgnoreCase);
            TemplateRadio.IsChecked = existing.MatchMode == CounterMatchMode.Template;
            RegexRadio.IsChecked    = existing.MatchMode == CounterMatchMode.Regex;
        }

        UpdateWildcardHint();
    }

    private void UpdateWildcardHint()
    {
        bool any = _channelChecks.Values.Any(cb => cb.IsChecked == true);
        WildcardHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var label = LabelBox.Text?.Trim() ?? string.Empty;
        if (label.Length == 0)
        {
            MessageBox.Show("Label is required.", "Counter Rule", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var pattern = PatternBox.Text ?? string.Empty;
        if (pattern.Length == 0)
        {
            MessageBox.Show("Pattern is required.", "Counter Rule", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var channels = _channelChecks
            .Where(kv => kv.Value.IsChecked == true)
            .Select(kv => kv.Key)
            .ToList();
        var mode = RegexRadio.IsChecked == true ? CounterMatchMode.Regex : CounterMatchMode.Template;

        _result = new CounterRule(
            Id: Guid.NewGuid().ToString("N"),
            Label: label,
            Channels: channels,
            Pattern: pattern,
            MatchMode: mode);
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
