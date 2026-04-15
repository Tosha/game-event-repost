using System.Collections.Generic;
using System.Linq;
using GuildRelay.Core.Config;

namespace GuildRelay.Features.Chat;

/// <summary>
/// Pure-data conversion from rule-editor dialog field values into a
/// <see cref="StructuredChatRule"/>. Lives outside WPF so it can be unit tested.
/// </summary>
public static class RuleEditorLogic
{
    public static StructuredChatRule BuildRule(
        string label,
        IEnumerable<string> selectedChannels,
        string keywordsText,
        MatchMode matchMode,
        int cooldownSec)
    {
        var trimmedLabel = (label ?? string.Empty).Trim();
        if (trimmedLabel.Length == 0) trimmedLabel = "Untitled";

        var channels = selectedChannels?.ToList() ?? new List<string>();

        var trimmedKeywordsText = (keywordsText ?? string.Empty).Trim();

        List<string> keywords;
        if (trimmedKeywordsText.Length == 0)
        {
            keywords = new List<string>();
        }
        else if (matchMode == MatchMode.Regex)
        {
            keywords = new List<string> { trimmedKeywordsText };
        }
        else
        {
            keywords = trimmedKeywordsText
                .Split(',')
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .ToList();
        }

        return new StructuredChatRule(
            Id: trimmedLabel.ToLowerInvariant().Replace(' ', '_'),
            Label: trimmedLabel,
            Channels: channels,
            Keywords: keywords,
            MatchMode: matchMode,
            CooldownSec: cooldownSec);
    }
}
