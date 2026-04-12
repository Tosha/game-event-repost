using System;
using System.Threading;
using System.Threading.Tasks;

namespace GuildRelay.Core.Ocr;

public interface IOcrEngine
{
    Task<OcrResult> RecognizeAsync(ReadOnlyMemory<byte> bgraPixels,
                                   int width, int height, int stride,
                                   CancellationToken ct);
}
