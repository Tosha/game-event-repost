using System.Text.RegularExpressions;

namespace GuildRelay.Features.Chat;

/// <summary>
/// Normalizes OCR output for dedup hashing and rule matching:
/// lowercase, collapse whitespace, strip known noise characters.
/// </summary>
public static class TextNormalizer
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
    // Only strip pipe and curly braces. Square brackets [ ] are meaningful
    // in MO2 chat (channel tags like [Nave], system tags like [Game],
    // timestamps like [20:27:33]) and must NOT be stripped.
    private static readonly Regex NoiseChars = new(@"[\|\{\}]", RegexOptions.Compiled);

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var cleaned = NoiseChars.Replace(input, string.Empty);
        cleaned = WhitespaceRun.Replace(cleaned, " ").Trim();
        return cleaned.ToLowerInvariant();
    }
}
