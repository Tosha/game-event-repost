using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GuildRelay.Core.Capture;

namespace GuildRelay.Platform.Windows.Capture;

public sealed class BitBltCapture : IScreenCapture
{
    public CapturedFrame CaptureRegion(Rectangle rect)
    {
        using var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);

        var hdcDest = g.GetHdc();
        var hdcSrc = GetDC(IntPtr.Zero); // desktop DC
        try
        {
            BitBlt(hdcDest, 0, 0, rect.Width, rect.Height,
                   hdcSrc, rect.X, rect.Y, SRCCOPY);
        }
        finally
        {
            g.ReleaseHdc(hdcDest);
            ReleaseDC(IntPtr.Zero, hdcSrc);
        }

        var lockBits = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[lockBits.Stride * lockBits.Height];
            Marshal.Copy(lockBits.Scan0, bytes, 0, bytes.Length);
            return new CapturedFrame(bytes, lockBits.Width, lockBits.Height, lockBits.Stride);
        }
        finally
        {
            bmp.UnlockBits(lockBits);
        }
    }

    private const int SRCCOPY = 0x00CC0020;

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest,
        int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
}
