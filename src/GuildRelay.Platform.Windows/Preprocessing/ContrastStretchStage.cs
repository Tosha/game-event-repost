using System;
using GuildRelay.Core.Capture;
using GuildRelay.Core.Preprocessing;

namespace GuildRelay.Platform.Windows.Preprocessing;

public sealed class ContrastStretchStage : IPreprocessStage
{
    private readonly double _low;
    private readonly double _high;

    public ContrastStretchStage(double low = 0.1, double high = 0.9)
    {
        _low = low;
        _high = high;
    }

    public string Name => "contrastStretch";

    public CapturedFrame Apply(CapturedFrame frame)
    {
        var src = frame.BgraPixels;
        var dst = new byte[src.Length];

        byte min = 255, max = 0;
        for (int i = 1; i < src.Length; i += 4)
        {
            if (src[i] < min) min = src[i];
            if (src[i] > max) max = src[i];
        }

        var lo = (byte)(min + (max - min) * _low);
        var hi = (byte)(min + (max - min) * _high);
        var range = Math.Max(hi - lo, 1);

        for (int i = 0; i < src.Length; i += 4)
        {
            for (int c = 0; c < 3; c++)
            {
                var val = Math.Clamp((src[i + c] - lo) * 255 / range, 0, 255);
                dst[i + c] = (byte)val;
            }
            dst[i + 3] = src[i + 3];
        }
        return new CapturedFrame(dst, frame.Width, frame.Height, frame.Stride);
    }
}
