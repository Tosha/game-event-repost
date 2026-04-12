using System.Drawing;

namespace GuildRelay.Core.Ocr;

public sealed record OcrLine(string Text, float Confidence, RectangleF Bounds);
