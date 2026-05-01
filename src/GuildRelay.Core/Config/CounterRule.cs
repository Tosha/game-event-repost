using System.Collections.Generic;

namespace GuildRelay.Core.Config;

public enum CounterMatchMode { Template, Regex }

public sealed record CounterRule(
    string Id,
    string Label,
    List<string> Channels,
    string Pattern,
    CounterMatchMode MatchMode);
