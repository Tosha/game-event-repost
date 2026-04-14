using System;
using System.Collections.Generic;

namespace GuildRelay.Features.Chat;

public sealed record ParsedChatLine(
    string? Timestamp,
    string? Channel,
    string? PlayerName,
    string Body);

public static class ChatLineParser
{
    private static readonly Dictionary<string, string> KnownChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["say"] = "Say", ["yell"] = "Yell", ["whisper"] = "Whisper",
        ["guild"] = "Guild", ["skill"] = "Skill", ["combat"] = "Combat",
        ["game"] = "Game", ["server"] = "Server", ["nave"] = "Nave",
        ["trade"] = "Trade", ["help"] = "Help",
        // OCR artifacts (leading dot)
        [".game"] = "Game", [".server"] = "Server", [".nave"] = "Nave",
        [".guild"] = "Guild", [".say"] = "Say", [".yell"] = "Yell",
        [".combat"] = "Combat", [".skill"] = "Skill", [".trade"] = "Trade",
        [".whisper"] = "Whisper", [".help"] = "Help",
    };

    public static IReadOnlyCollection<string> KnownChannelNames { get; } =
        new[] { "Say", "Yell", "Whisper", "Guild", "Skill", "Combat", "Game", "Server", "Nave", "Trade", "Help" };

    public static ParsedChatLine Parse(string line)
    {
        var pos = 0;
        string? timestamp = null;
        string? channel = null;
        string? playerName = null;

        var tag1 = ExtractBracketedTag(line, ref pos);
        if (tag1 is null)
            return new ParsedChatLine(null, null, null, line);

        if (KnownChannels.TryGetValue(tag1, out var ch1))
        {
            // First tag is a channel (no timestamp)
            channel = ch1;
        }
        else
        {
            // First tag is NOT a known channel — treat it as a timestamp or
            // OCR-garbled prefix (e.g., "'16:33:35", "16:33:35", "20:27:33").
            // Skip any whitespace, then look for a channel in the next tag.
            timestamp = tag1;
            while (pos < line.Length && line[pos] == ' ') pos++;
            var tag2 = ExtractBracketedTag(line, ref pos);
            if (tag2 is not null && KnownChannels.TryGetValue(tag2, out var ch2))
            {
                channel = ch2;
            }
            else
            {
                // Neither first nor second tag is a known channel — give up
                return new ParsedChatLine(null, null, null, line);
            }
        }

        while (pos < line.Length && line[pos] == ' ') pos++;

        // Check for player name: [PlayerName]
        if (pos < line.Length && line[pos] == '[')
        {
            var savedPos = pos;
            var maybePlayer = ExtractBracketedTag(line, ref pos);
            if (maybePlayer is not null && !KnownChannels.ContainsKey(maybePlayer))
            {
                playerName = maybePlayer;
            }
            else
            {
                pos = savedPos;
            }
        }

        while (pos < line.Length && line[pos] == ' ') pos++;

        var body = pos < line.Length ? line[pos..] : string.Empty;
        return new ParsedChatLine(timestamp, channel, playerName, body);
    }

    private static string? ExtractBracketedTag(string line, ref int pos)
    {
        if (pos >= line.Length || line[pos] != '[') return null;
        var close = line.IndexOf(']', pos + 1);
        if (close < 0) return null;
        var tag = line[(pos + 1)..close];
        pos = close + 1;
        return tag;
    }
}
