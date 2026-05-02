using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace IconGen;

/// <summary>
/// Renders a swallow-tail heraldic pennant at six resolutions (16/24/32/48/64/256 px)
/// and packs them into a multi-resolution .ico file. Run manually when the icon design
/// changes; the resulting .ico is committed to the repo.
///
/// Default output path: `src/GuildRelay.App/Assets/tray.ico` (relative to the working
/// directory — run from the repo root). Override with a CLI argument:
///     dotnet run --project tools/IconGen -- some/other/path.ico
/// </summary>
public static class Program
{
    private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 256 };
    private static readonly Color GoldColor = Color.FromArgb(0xFF, 0xD4, 0xAF, 0x37);
    private const string DefaultOutputPath = "src/GuildRelay.App/Assets/tray.ico";

    public static int Main(string[] args)
    {
        var outputPath = args.Length > 0 ? args[0] : DefaultOutputPath;

        var pngBuffers = new List<byte[]>(Sizes.Length);
        foreach (var size in Sizes)
        {
            using var bitmap = RenderPennant(size);
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            pngBuffers.Add(ms.ToArray());
        }

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        WriteIco(fullPath, pngBuffers);

        Console.WriteLine($"Wrote {Sizes.Length} resolutions to {fullPath}");
        return 0;
    }

    /// <summary>
    /// Renders the pennant onto an NxN bitmap with a transparent background.
    /// Pole at 25% canvas-x, full height. Pennant attached at the pole's top,
    /// flying right with a triangular V-notch in its fly end.
    /// </summary>
    private static Bitmap RenderPennant(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);

        g.CompositingMode = CompositingMode.SourceCopy;
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        // Anti-alias only at sizes large enough to benefit. At 16x16, sharp edges
        // read better than smudged ones.
        g.SmoothingMode = size >= 32 ? SmoothingMode.AntiAlias : SmoothingMode.None;

        int poleWidth = Math.Max(1, size / 16);
        int poleX = size / 4;

        using var goldBrush = new SolidBrush(GoldColor);

        // Pole: full canvas height
        g.FillRectangle(goldBrush, poleX, 0, poleWidth, size);

        // Pennant: 5-point swallow-tail polygon attached to top of pole
        int pennantWidth = (int)Math.Round(size * 0.65);
        int pennantHeight = (int)Math.Round(size * 0.35);
        int pennantTop = Math.Max(1, size / 16);
        int notchDepth = Math.Max(1, pennantWidth / 4);

        var attachX = poleX + poleWidth;
        // Clamp flyX inside the canvas. Defensive: with the current 0.65 width
        // factor this is a no-op at every target size (attachX + pennantWidth
        // already lands at size-1 or earlier). Raising the factor (e.g. to 0.70)
        // would push flyX into the out-of-bounds column where GDI+ silently
        // clips, leaving the fly corners flat instead of sharp.
        var flyX = Math.Min(attachX + pennantWidth, size - 1);
        var pennantPoints = new[]
        {
            new Point(attachX, pennantTop),                                  // top-left
            new Point(flyX, pennantTop),                                     // top-right (fly top)
            new Point(flyX - notchDepth, pennantTop + pennantHeight / 2),    // V-notch tip
            new Point(flyX, pennantTop + pennantHeight),                     // bottom-right (fly bottom)
            new Point(attachX, pennantTop + pennantHeight),                  // bottom-left
        };
        g.FillPolygon(goldBrush, pennantPoints);

        return bitmap;
    }

    /// <summary>
    /// Writes a multi-resolution .ico file. ICO format reference:
    /// ICONDIR header (6 bytes) + ICONDIRENTRY[] (16 bytes each) + image data.
    /// We embed each image as a PNG (modern .ico format supports this and it
    /// keeps the writer simple — no need for separate BMP encoding).
    /// </summary>
    private static void WriteIco(string path, List<byte[]> pngs)
    {
        if (pngs.Count != Sizes.Length)
            throw new ArgumentException($"Expected {Sizes.Length} PNG buffers, got {pngs.Count}");

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        // ICONDIR (6 bytes)
        bw.Write((ushort)0);             // reserved
        bw.Write((ushort)1);             // type = icon (.ico)
        bw.Write((ushort)pngs.Count);    // count

        // ICONDIRENTRY[] (16 bytes each)
        int dataOffset = 6 + pngs.Count * 16;
        for (int i = 0; i < pngs.Count; i++)
        {
            int size = Sizes[i];
            byte width = size >= 256 ? (byte)0 : (byte)size;  // 0 means 256 in ICO
            byte height = width;
            bw.Write(width);
            bw.Write(height);
            bw.Write((byte)0);              // colorCount = 0 (not palettized)
            bw.Write((byte)0);              // reserved
            bw.Write((ushort)1);            // planes
            bw.Write((ushort)32);           // bitCount (BGRA)
            bw.Write((uint)pngs[i].Length); // bytesInRes
            bw.Write((uint)dataOffset);     // imageOffset
            dataOffset += pngs[i].Length;
        }

        // Image data, in the same order as the directory entries
        foreach (var png in pngs)
            bw.Write(png);
    }
}
