using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GuildRelay.Core.Config;

namespace GuildRelay.Features.Chat;

public sealed record ChannelMatchResult(StructuredChatRule Rule);

public sealed class ChannelMatcher
{
    private readonly Dictionary<string, List<CompiledStructuredRule>> _byChannel;

    public ChannelMatcher(IEnumerable<StructuredChatRule> rules)
    {
        _byChannel = new Dictionary<string, List<CompiledStructuredRule>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            var compiled = new CompiledStructuredRule(rule);
            foreach (var ch in rule.Channels)
            {
                if (!_byChannel.TryGetValue(ch, out var list))
                {
                    list = new List<CompiledStructuredRule>();
                    _byChannel[ch] = list;
                }
                list.Add(compiled);
            }
        }
    }

    public ChannelMatchResult? FindMatch(ParsedChatLine parsed)
    {
        if (parsed.Channel is null) return null;
        if (!_byChannel.TryGetValue(parsed.Channel, out var candidates)) return null;

        foreach (var compiled in candidates)
        {
            if (compiled.Matches(parsed.Body))
                return new ChannelMatchResult(compiled.Rule);
        }
        return null;
    }

    private sealed class CompiledStructuredRule
    {
        public StructuredChatRule Rule { get; }
        private readonly List<string>? _keywords;
        private readonly Regex? _regex;

        public CompiledStructuredRule(StructuredChatRule rule)
        {
            Rule = rule;
            if (rule.Keywords.Count == 0)
            {
                _keywords = null;
                _regex = null;
            }
            else if (rule.MatchMode == MatchMode.Regex)
            {
                var pattern = string.Join("|", rule.Keywords);
                _regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            }
            else
            {
                _keywords = rule.Keywords.Select(k => k.ToLowerInvariant()).ToList();
            }
        }

        public bool Matches(string body)
        {
            if (_keywords is null && _regex is null) return true;
            if (_regex is not null) return _regex.IsMatch(body);
            var lower = body.ToLowerInvariant();
            return _keywords!.Any(k => lower.Contains(k));
        }
    }
}
