using System.Globalization;
using System.Text.RegularExpressions;
using GuildRelay.Core.Config;

namespace GuildRelay.Features.Chat;

public readonly record struct CounterMatch(bool Success, double Value);

public sealed class CompiledCounterRule
{
    public CompiledCounterRule(CounterRule rule, Regex regex, bool hasValueGroup)
    {
        Rule = rule;
        _regex = regex;
        _hasValueGroup = hasValueGroup;
    }

    public CounterRule Rule { get; }
    private readonly Regex _regex;
    private readonly bool _hasValueGroup;

    public CounterMatch Match(string body)
    {
        var m = _regex.Match(body);
        if (!m.Success) return new CounterMatch(false, 0);
        if (!_hasValueGroup) return new CounterMatch(true, 1);
        var captured = m.Groups["value"].Value;
        if (!double.TryParse(captured, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return new CounterMatch(false, 0);
        return new CounterMatch(true, v);
    }
}

public static class CounterRuleCompiler
{
    private const string ValuePlaceholder = "{value}";
    private const string ValueRegexFragment = @"(?<value>-?\d+(?:\.\d+)?)";

    public static CompiledCounterRule Compile(CounterRule rule)
    {
        const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.IgnoreCase;
        if (rule.MatchMode == CounterMatchMode.Template)
        {
            var pattern = rule.Pattern;
            var hasPlaceholder = pattern.Contains(ValuePlaceholder);
            var parts = pattern.Split(new[] { ValuePlaceholder }, System.StringSplitOptions.None);
            var compiled = string.Join(ValueRegexFragment,
                System.Linq.Enumerable.Select(parts, Regex.Escape));
            return new CompiledCounterRule(rule, new Regex("^" + compiled + "$", Opts), hasPlaceholder);
        }
        else
        {
            var regex = new Regex(rule.Pattern, Opts);
            var hasGroup = System.Linq.Enumerable.Contains(regex.GetGroupNames(), "value");
            return new CompiledCounterRule(rule, regex, hasGroup);
        }
    }
}
