using System.Text.RegularExpressions;
using GuildRelay.Core.Events;

namespace GuildRelay.Publisher;

public sealed class TemplateEngine
{
    private static readonly Regex Placeholder = new(@"\{([a-zA-Z_][a-zA-Z0-9_]*)\}", RegexOptions.Compiled);

    public string Render(string template, DetectionEvent evt)
    {
        return Placeholder.Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            return key switch
            {
                "player" => evt.PlayerName,
                "rule_label" => evt.RuleLabel,
                "matched_text" => evt.MatchedContent,
                "feature" => evt.FeatureId,
                "timestamp" => evt.TimestampUtc.ToString("O"),
                _ => evt.Extras.TryGetValue(key, out var v) ? v : string.Empty
            };
        });
    }
}
