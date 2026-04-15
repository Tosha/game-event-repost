using System.Collections.Generic;

namespace GuildRelay.Features.Chat;

/// <summary>
/// One OCR line fed into the assembler. Normalized is the de-noised text used
/// for parsing/matching; Original is the raw OCR output preserved for debug.
/// </summary>
public sealed record OcrLineInput(string Normalized, string Original, float Confidence);

public sealed record AssemblyResult(
    IReadOnlyList<AssembledMessage> Completed,
    AssembledMessage? Trailing);

public static class ChatMessageAssembler
{
    public static AssemblyResult Assemble(
        IReadOnlyList<OcrLineInput> lines,
        double confidenceThreshold)
    {
        var completed = new List<AssembledMessage>();
        OpenMessage? open = null;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var belowThreshold = line.Confidence < confidenceThreshold;

            if (belowThreshold)
            {
                // We do not TRUST the low-confidence line, but we can still look at
                // its shape to decide whether it structurally terminates the open
                // message. If it looks like a header, close any open message (but do
                // NOT start a new one from this garbled header). If it looks like
                // a continuation, skip it silently and leave the open message alive.
                if (ChatLineParser.IsHeader(line.Normalized))
                {
                    if (open is not null)
                    {
                        completed.Add(open.ToAssembled());
                        open = null;
                    }
                }
                continue;
            }

            var isHeader = ChatLineParser.IsHeader(line.Normalized);

            if (isHeader)
            {
                if (open is not null)
                {
                    completed.Add(open.ToAssembled());
                }
                var parsed = ChatLineParser.Parse(line.Normalized);
                open = new OpenMessage(
                    parsed.Timestamp,
                    parsed.Channel!,
                    parsed.PlayerName,
                    parsed.Body,
                    line.Original,
                    startRow: i,
                    endRow: i);
            }
            else
            {
                if (open is null) continue; // orphan continuation before any header
                open.AppendLine(line.Normalized, line.Original, rowIndex: i);
            }
        }

        AssembledMessage? trailing = open?.ToAssembled();
        return new AssemblyResult(completed, trailing);
    }

    private sealed class OpenMessage
    {
        private string _body;
        private string _original;
        public string? Timestamp { get; }
        public string Channel { get; }
        public string? PlayerName { get; }
        public int StartRow { get; }
        public int EndRow { get; private set; }

        public OpenMessage(string? timestamp, string channel, string? playerName,
            string initialBody, string initialOriginal, int startRow, int endRow)
        {
            Timestamp = timestamp;
            Channel = channel;
            PlayerName = playerName;
            _body = initialBody;
            _original = initialOriginal;
            StartRow = startRow;
            EndRow = endRow;
        }

        public void AppendLine(string normalized, string original, int rowIndex)
        {
            if (_body.Length == 0) _body = normalized;
            else _body = _body + " " + normalized;

            if (_original.Length == 0) _original = original;
            else _original = _original + " " + original;

            EndRow = rowIndex;
        }

        public AssembledMessage ToAssembled() =>
            new(Timestamp, Channel, PlayerName, _body, _original, StartRow, EndRow);
    }
}
