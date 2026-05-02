using System;
using System.Collections.Generic;
using GuildRelay.Core.Config;

namespace GuildRelay.Features.Chat;

public sealed record CounterMatchResult(string Label, double Value);

public sealed class CounterMatcher
{
    private readonly Dictionary<string, List<CompiledCounterRule>> _byChannel;
    private readonly List<CompiledCounterRule> _wildcard;

    public CounterMatcher(IEnumerable<CounterRule> rules)
    {
        _byChannel = new Dictionary<string, List<CompiledCounterRule>>(StringComparer.OrdinalIgnoreCase);
        _wildcard = new List<CompiledCounterRule>();
        foreach (var rule in rules)
        {
            var compiled = CounterRuleCompiler.Compile(rule);
            if (rule.Channels.Count == 0)
            {
                _wildcard.Add(compiled);
                continue;
            }
            foreach (var ch in rule.Channels)
            {
                if (!_byChannel.TryGetValue(ch, out var list))
                {
                    list = new List<CompiledCounterRule>();
                    _byChannel[ch] = list;
                }
                list.Add(compiled);
            }
        }
    }

    public CounterMatchResult? Match(ParsedChatLine parsed)
    {
        if (parsed.Channel is null) return null;

        if (_byChannel.TryGetValue(parsed.Channel, out var candidates))
        {
            foreach (var compiled in candidates)
            {
                var m = compiled.Match(parsed.Body);
                if (m.Success) return new CounterMatchResult(compiled.Rule.Label, m.Value);
            }
        }

        foreach (var compiled in _wildcard)
        {
            var m = compiled.Match(parsed.Body);
            if (m.Success) return new CounterMatchResult(compiled.Rule.Label, m.Value);
        }

        return null;
    }
}
