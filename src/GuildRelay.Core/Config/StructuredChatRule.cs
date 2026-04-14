using System.Collections.Generic;

namespace GuildRelay.Core.Config;

public enum MatchMode { ContainsAny, Regex }

public sealed record StructuredChatRule(
    string Id,
    string Label,
    List<string> Channels,
    List<string> Keywords,
    MatchMode MatchMode,
    int CooldownSec = 600);
