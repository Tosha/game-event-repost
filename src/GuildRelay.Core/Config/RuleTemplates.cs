using System.Collections.Generic;
using System.Linq;

namespace GuildRelay.Core.Config;

public static class RuleTemplates
{
    public static readonly string[] Mo2Locations =
    {
        "Sylvan Sanctum", "Tindremic Heartlands", "Tindrem Sewers",
        "Green Weald", "Fabernum Tower", "Western Talus Mountains",
        "Central Talus Mountains", "Mouth of Gal Barag", "Northern Valley",
        "Northern Foothills", "Eastern Talus Mountains", "Gaul'Kor Wastes",
        "Gaul'Kor Outpost", "Bullhead Gulch", "Eastern Highlands",
        "Morin Khur Sewers", "Khurite Bluffs", "Deepwood", "Colored Forest",
        "Tindremic Midlands", "Western Coastlands", "Plains of Meduli",
        "Melisars Vault", "Western Steppe", "The Undercroft", "Eastern Steppe",
        "Eastern Moorland", "Eastern Glen", "Eastern Greatwoods",
        "Tower of Descensus", "Marlwood", "Southern Greatwoods", "Landfall",
        "Meduli Badlands", "Tekton's Heel", "Sunken Isles", "Northern Canteri",
        "Southern Canteri", "Western Stairs of Echidna", "Western Brood Isles",
        "Shinarian Temple", "Shinarian Labyrinth", "Eastern Stairs of Echidna",
        "Eastern Brood Isles",
    };

    public static IReadOnlyDictionary<string, List<StructuredChatRule>> BuiltIn { get; } =
        new Dictionary<string, List<StructuredChatRule>>
        {
            ["MO2 Game Events"] = new List<StructuredChatRule>
            {
                new(
                    Id: "mo2_game_events",
                    Label: "MO2 Game Events",
                    Channels: new List<string> { "Game" },
                    Keywords: Mo2Locations.ToList(),
                    MatchMode: MatchMode.ContainsAny,
                    CooldownSec: 600)
            }
        };

    public static IReadOnlyList<string> BuiltInNames { get; } = BuiltIn.Keys.ToList();
}
