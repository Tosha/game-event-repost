namespace GuildRelay.Features.Chat;

/// <summary>
/// A chat message assembled from one or more adjacent OCR lines.
/// The header portion (channel / timestamp / player) is taken from the first
/// constituent line; continuation lines are appended to Body with a single
/// space separator.
/// </summary>
public sealed record AssembledMessage(
    string? Timestamp,
    string? Channel,
    string? PlayerName,
    string Body,
    string OriginalText,
    int StartRow,
    int EndRow)
{
    public ParsedChatLine ToParsedChatLine()
        => new(Timestamp, Channel, PlayerName, Body);
}
