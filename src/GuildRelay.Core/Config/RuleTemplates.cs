using System.Collections.Generic;
using System.Linq;

namespace GuildRelay.Core.Config;

/// <summary>
/// Built-in rule templates that ship with the app. Users can load these
/// into their Chat Watcher rules with one click.
/// </summary>
public static class RuleTemplates
{
    private static readonly string[] Mo2Locations =
    {
        "Sylvan Sanctum",
        "Tindremic Heartlands",
        "Tindrem Sewers",
        "Green Weald",
        "Fabernum Tower",
        "Western Talus Mountains",
        "Central Talus Mountains",
        "Mouth of Gal Barag",
        "Northern Valley",
        "Northern Foothills",
        "Eastern Talus Mountains",
        "Gaul'Kor Wastes",
        "Gaul'Kor Outpost",
        "Bullhead Gulch",
        "Eastern Highlands",
        "Morin Khur Sewers",
        "Khurite Bluffs",
        "Deepwood",
        "Colored Forest",
        "Tindremic Midlands",
        "Western Coastlands",
        "Plains of Meduli",
        "Melisars Vault",
        "Western Steppe",
        "The Undercroft",
        "Eastern Steppe",
        "Eastern Moorland",
        "Eastern Glen",
        "Eastern Greatwoods",
        "Tower of Descensus",
        "Marlwood",
        "Southern Greatwoods",
        "Landfall",
        "Meduli Badlands",
        "Tekton's Heel",
        "Sunken Isles",
        "Northern Canteri",
        "Southern Canteri",
        "Western Stairs of Echidna",
        "Western Brood Isles",
        "Shinarian Temple",
        "Shinarian Labyrinth",
        "Eastern Stairs of Echidna",
        "Eastern Brood Isles",
    };

    private static string BuildMo2LocationPattern()
    {
        var locations = string.Join("|", Mo2Locations.Select(l => l.ToLowerInvariant()));
        return @"\[game\].*(" + locations + ")";
    }

    public static IReadOnlyDictionary<string, List<ChatRuleConfig>> BuiltIn { get; } =
        new Dictionary<string, List<ChatRuleConfig>>
        {
            ["MO2 Game Events"] = new List<ChatRuleConfig>
            {
                new(
                    Id: "mo2_game_events",
                    Label: "MO2 Game Events",
                    Pattern: BuildMo2LocationPattern(),
                    Regex: true,
                    CooldownSec: 60)
            }
        };

    public static IReadOnlyList<string> BuiltInNames { get; } = BuiltIn.Keys.ToList();
}
