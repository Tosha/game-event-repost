using System.Text.RegularExpressions;

namespace GuildRelay.Features.Chat;

/// <summary>
/// Normalizes OCR output for dedup hashing and rule matching:
/// lowercase, collapse whitespace, strip known noise characters.
/// </summary>
public static class TextNormalizer
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
    // Strip pipe and curly braces entirely.
    private static readonly Regex StripChars = new(@"[\|\{\}]", RegexOptions.Compiled);
    // Replace bullet/dot characters with spaces (OCR reads these between words:
    // "of•profiteers", "•plains of•meduli"). Replacing with space instead of
    // stripping preserves word boundaries for keyword matching.
    private static readonly Regex BulletChars = new(@"[\u2022\u2023\u25E6\u00B7]", RegexOptions.Compiled);

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var cleaned = StripChars.Replace(input, string.Empty);
        cleaned = BulletChars.Replace(cleaned, " ");
        cleaned = WhitespaceRun.Replace(cleaned, " ").Trim();
        return cleaned.ToLowerInvariant();
    }
}
