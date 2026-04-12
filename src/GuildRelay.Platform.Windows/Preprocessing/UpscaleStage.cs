using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GuildRelay.Core.Capture;
using GuildRelay.Core.Preprocessing;

namespace GuildRelay.Platform.Windows.Preprocessing;

public sealed class UpscaleStage : IPreprocessStage
{
    private readonly int _factor;

    public UpscaleStage(int factor = 2) { _factor = factor; }

    public string Name => "upscale";

    public CapturedFrame Apply(CapturedFrame frame)
    {
        var newW = frame.Width * _factor;
        var newH = frame.Height * _factor;

        // Pin the source array so we can create a Bitmap from it
        var handle = GCHandle.Alloc(frame.BgraPixels, GCHandleType.Pinned);
        try
        {
            using var srcBmp = new Bitmap(frame.Width, frame.Height, frame.Stride,
                PixelFormat.Format32bppArgb, handle.AddrOfPinnedObject());
            using var dstBmp = new Bitmap(newW, newH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dstBmp))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.DrawImage(srcBmp, 0, 0, newW, newH);
            }

            var lockBits = dstBmp.LockBits(
                new Rectangle(0, 0, newW, newH),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[lockBits.Stride * lockBits.Height];
                Marshal.Copy(lockBits.Scan0, bytes, 0, bytes.Length);
                return new CapturedFrame(bytes, newW, newH, lockBits.Stride);
            }
            finally
            {
                dstBmp.UnlockBits(lockBits);
            }
        }
        finally
        {
            handle.Free();
        }
    }
}
