using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using GuildRelay.Core.Ocr;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace GuildRelay.Platform.Windows.Ocr;

public sealed class WindowsMediaOcrEngine : IOcrEngine
{
    private readonly OcrEngine _engine;

    public WindowsMediaOcrEngine()
    {
        _engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException(
                "Windows.Media.Ocr could not create an OCR engine. " +
                "Ensure at least one language pack with OCR is installed.");
    }

    public async Task<Core.Ocr.OcrResult> RecognizeAsync(
        ReadOnlyMemory<byte> bgraPixels,
        int width, int height, int stride,
        CancellationToken ct)
    {
        var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(bgraPixels.ToArray().AsBuffer());

        var result = await _engine.RecognizeAsync(bitmap);

        var lines = new List<Core.Ocr.OcrLine>();
        foreach (var line in result.Lines)
        {
            var text = line.Text;
            var bounds = RectangleF.Empty;
            if (line.Words.Count > 0)
            {
                var first = line.Words[0].BoundingRect;
                var last = line.Words[line.Words.Count - 1].BoundingRect;
                bounds = new RectangleF(
                    (float)first.X, (float)first.Y,
                    (float)(last.X + last.Width - first.X),
                    (float)Math.Max(first.Height, last.Height));
            }
            // Windows.Media.Ocr does not expose per-line confidence;
            // default to 1.0. Filtering relies on preprocess quality.
            lines.Add(new Core.Ocr.OcrLine(text, Confidence: 1.0f, bounds));
        }

        return new Core.Ocr.OcrResult(lines);
    }
}
