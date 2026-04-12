using System;
using System.Text.RegularExpressions;

namespace GuildRelay.Core.Rules;

/// <summary>
/// A single compiled pattern — either a case-insensitive literal substring
/// check or a compiled regex. Used by Chat Watcher rules and Status
/// Watcher disconnect patterns.
/// </summary>
public sealed class CompiledPattern
{
    private readonly Regex? _regex;
    private readonly string? _literal;

    private CompiledPattern(Regex? regex, string? literal)
    {
        _regex = regex;
        _literal = literal;
    }

    public static CompiledPattern Create(string pattern, bool isRegex)
    {
        if (isRegex)
            return new CompiledPattern(
                new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase),
                literal: null);
        return new CompiledPattern(regex: null, literal: pattern);
    }

    public bool IsMatch(string input)
    {
        if (_regex is not null)
            return _regex.IsMatch(input);
        return input.Contains(_literal!, StringComparison.OrdinalIgnoreCase);
    }
}
