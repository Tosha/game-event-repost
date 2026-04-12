using System;

namespace GuildRelay.Core.Capture;

/// <summary>
/// Raw BGRA pixel buffer captured from a screen region.
/// </summary>
public sealed class CapturedFrame : IDisposable
{
    public CapturedFrame(byte[] bgraPixels, int width, int height, int stride)
    {
        BgraPixels = bgraPixels;
        Width = width;
        Height = height;
        Stride = stride;
    }

    public byte[] BgraPixels { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }

    public void Dispose() { /* pixel buffer is managed; no-op for now */ }
}
