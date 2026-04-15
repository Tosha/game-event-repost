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
