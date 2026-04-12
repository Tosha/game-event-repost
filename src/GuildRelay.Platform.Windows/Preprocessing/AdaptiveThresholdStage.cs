using System;
using GuildRelay.Core.Capture;
using GuildRelay.Core.Preprocessing;

namespace GuildRelay.Platform.Windows.Preprocessing;

public sealed class AdaptiveThresholdStage : IPreprocessStage
{
    private readonly int _blockSize;

    public AdaptiveThresholdStage(int blockSize = 15) { _blockSize = blockSize | 1; /* ensure odd */ }

    public string Name => "adaptiveThreshold";

    public CapturedFrame Apply(CapturedFrame frame)
    {
        var w = frame.Width;
        var h = frame.Height;
        var src = frame.BgraPixels;
        var dst = new byte[src.Length];
        var half = _blockSize / 2;

        // Integral image of green channel (grayscale proxy after GrayscaleStage)
        var integral = new long[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            long rowSum = 0;
            for (int x = 0; x < w; x++)
            {
                rowSum += src[(y * w + x) * 4 + 1];
                integral[(y + 1) * (w + 1) + (x + 1)] = rowSum + integral[y * (w + 1) + (x + 1)];
            }
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var x1 = Math.Max(x - half, 0);
                var y1 = Math.Max(y - half, 0);
                var x2 = Math.Min(x + half, w - 1);
                var y2 = Math.Min(y + half, h - 1);
                var count = (x2 - x1 + 1) * (y2 - y1 + 1);

                var sum = integral[(y2 + 1) * (w + 1) + (x2 + 1)]
                        - integral[y1 * (w + 1) + (x2 + 1)]
                        - integral[(y2 + 1) * (w + 1) + x1]
                        + integral[y1 * (w + 1) + x1];

                var mean = sum / count;
                var val = src[(y * w + x) * 4 + 1];
                var output = (byte)(val > mean - 10 ? 255 : 0);

                var idx = (y * w + x) * 4;
                dst[idx] = output;
                dst[idx + 1] = output;
                dst[idx + 2] = output;
                dst[idx + 3] = 255;
            }
        }

        return new CapturedFrame(dst, w, h, w * 4);
    }
}
