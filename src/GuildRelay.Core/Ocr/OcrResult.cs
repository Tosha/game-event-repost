using System.Collections.Generic;

namespace GuildRelay.Core.Ocr;

public sealed record OcrResult(IReadOnlyList<OcrLine> Lines);
