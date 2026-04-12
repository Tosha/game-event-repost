using GuildRelay.Core.Capture;
using GuildRelay.Core.Preprocessing;

namespace GuildRelay.Platform.Windows.Preprocessing;

public sealed class GrayscaleStage : IPreprocessStage
{
    public string Name => "grayscale";

    public CapturedFrame Apply(CapturedFrame frame)
    {
        var src = frame.BgraPixels;
        var dst = new byte[src.Length];
        for (int i = 0; i < src.Length; i += 4)
        {
            // ITU-R BT.601 luminance
            var gray = (byte)(0.299 * src[i + 2] + 0.587 * src[i + 1] + 0.114 * src[i]);
            dst[i] = gray;
            dst[i + 1] = gray;
            dst[i + 2] = gray;
            dst[i + 3] = src[i + 3];
        }
        return new CapturedFrame(dst, frame.Width, frame.Height, frame.Stride);
    }
}
